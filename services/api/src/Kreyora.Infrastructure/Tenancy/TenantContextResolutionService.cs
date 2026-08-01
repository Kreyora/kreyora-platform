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

    public async Task<TenantContext?> ResolveSupportContextAsync(
        string userId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await (
                from grant in dbContext.SupportAccessGrants.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on grant.TenantId equals tenant.Id
                join userRole in dbContext.UserRoles on grant.SupportUserId equals userRole.UserId
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where grant.TenantId == tenantId
                    && grant.SupportUserId == userId
                    && grant.RevokedAt == null
                    && grant.ExpiresAt > now
                    && tenant.Status == TenantStatus.Active
                    && role.Name == RoleDefinitions.PlatformSupport
                    && !dbContext.Memberships.Any(membership => membership.TenantId == tenantId && membership.UserId == userId)
                select new TenantContext(tenant.Id, userId, null, null, grant.Id))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
