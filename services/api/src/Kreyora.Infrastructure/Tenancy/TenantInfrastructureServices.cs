using Kreyora.Application.Tenancy;
using Kreyora.Domain.Common;

namespace Kreyora.Infrastructure.Tenancy;

public sealed class TenantQueryService(ITenantContextAccessor tenantContext) : ITenantQueryService
{
    public IQueryable<TEntity> ForCurrentTenant<TEntity>(IQueryable<TEntity> query)
        where TEntity : class, ITenantOwned
    {
        var context = tenantContext.RequireCurrent();
        return query.Where(entity => entity.TenantId == context.TenantId);
    }
}

public sealed class TenantKeyBuilder(ITenantContextAccessor tenantContext) : ITenantKeyBuilder
{
    public string BuildStorageObjectKey(params string[] segments) => Build("tenants", "/", segments);

    public string BuildCacheKey(params string[] segments) => Build("tenant", ":", segments);

    public string BuildSearchKey(params string[] segments) => Build("tenant", ":", segments);

    private string Build(string prefix, string separator, IEnumerable<string> segments)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var safeSegments = segments.Select(segment =>
        {
            if (string.IsNullOrWhiteSpace(segment) || segment.Contains("..", StringComparison.Ordinal)
                || segment.Contains('/') || segment.Contains('\\'))
            {
                throw new ArgumentException("Tenant key segments must be non-empty path-safe values.", nameof(segments));
            }

            return segment.Trim();
        });

        return string.Join(separator, new[] { prefix, tenantId }.Concat(safeSegments));
    }
}

public sealed class TenantJobRunner(
    ITenantContextAccessor tenantContext,
    ITenantContextResolutionService resolver) : ITenantJobRunner
{
    public async Task RunAsync(
        TenantJobEnvelope job,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(operation);

        var context = await resolver.ResolveBackgroundContextAsync(job.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("The tenant job cannot run because its tenant is inactive or unavailable.");

        using (tenantContext.BeginScope(context))
        {
            await operation(cancellationToken);
        }
    }
}

public sealed class TenantOutboxProcessor(
    ITenantContextAccessor tenantContext,
    ITenantContextResolutionService resolver) : ITenantOutboxProcessor
{
    public async Task ProcessAsync(
        string tenantId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var context = await resolver.ResolveBackgroundContextAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException("The outbox message cannot run because its tenant is inactive or unavailable.");

        using (tenantContext.BeginScope(context))
        {
            await operation(cancellationToken);
        }
    }
}
