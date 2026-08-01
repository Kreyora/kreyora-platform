using Kreyora.Domain.Tenancy;

namespace Kreyora.Application.Tenancy;

public interface ITenantContextResolutionService
{
    Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default);

    Task<TenantContext?> ResolveMembershipContextAsync(
        string userId,
        string tenantId,
        CancellationToken cancellationToken = default);

    Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default);
}

public sealed record WorkspaceSummary(string TenantId, string DisplayName, string Slug, TenantRole Role);
