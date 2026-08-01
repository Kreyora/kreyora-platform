using Kreyora.Domain.Common;

namespace Kreyora.Application.Tenancy;

public interface ITenantQueryService
{
    IQueryable<TEntity> ForCurrentTenant<TEntity>(IQueryable<TEntity> query)
        where TEntity : class, ITenantOwned;
}

public interface ITenantKeyBuilder
{
    string BuildStorageObjectKey(params string[] segments);

    string BuildCacheKey(params string[] segments);

    string BuildSearchKey(params string[] segments);
}

public sealed record TenantJobEnvelope(string TenantId, string JobName, string Payload);

public interface ITenantJobRunner
{
    Task RunAsync(
        TenantJobEnvelope job,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}

public interface ITenantOutboxProcessor
{
    Task ProcessAsync(
        string tenantId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
