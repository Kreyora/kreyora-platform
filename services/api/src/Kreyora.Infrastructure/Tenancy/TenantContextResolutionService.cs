using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Tenancy;

public sealed class TenantContextResolutionService(AppDbContext dbContext) : ITenantContextResolutionService
{
    public async Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
                from membership in dbContext.Memberships.AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                where membership.UserId == userId
                    && membership.Status == MembershipStatus.Active
                    && tenant.Status == TenantStatus.Active
                orderby tenant.DisplayName
                select new WorkspaceSummary(tenant.Id, tenant.DisplayName, tenant.Slug, membership.Role))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantContext?> ResolveMembershipContextAsync(
        string userId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return await (
                from membership in dbContext.Memberships.AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                where membership.UserId == userId
                    && membership.TenantId == tenantId
                    && membership.Status == MembershipStatus.Active
                    && tenant.Status == TenantStatus.Active
                select new TenantContext(tenant.Id, membership.UserId, membership.Id, membership.Role))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<TenantContext?> ResolveBackgroundContextAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await dbContext.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == tenantId && candidate.Status == TenantStatus.Active, cancellationToken);

        return tenant is null ? null : new TenantContext(tenant.Id, null, null, null);
    }
}
