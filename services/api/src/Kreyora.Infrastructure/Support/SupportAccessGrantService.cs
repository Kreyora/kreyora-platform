using System.Data;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Support;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Support;

public sealed class SupportAccessGrantService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents) : ISupportAccessGrantService
{
    public async Task<SupportAccessGrantSummary> CreateAsync(CreateSupportAccessGrantRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.SupportGrantManage);
        var context = tenantContext.RequireCurrent();
        var actor = context.UserId ?? throw new InvalidOperationException("Support grants require an authenticated owner.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var isSupport = await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == request.SupportUserId && role.Name == RoleDefinitions.PlatformSupport
            select role.Id).AnyAsync(cancellationToken);
        if (!isSupport)
        {
            throw new InvalidOperationException("The target user is not an active PlatformSupport user.");
        }

        if (await dbContext.Memberships.AnyAsync(membership => membership.TenantId == context.TenantId && membership.UserId == request.SupportUserId, cancellationToken))
        {
            throw new InvalidOperationException("PlatformSupport access cannot be combined with a tenant membership.");
        }

        var duplicate = await dbContext.SupportAccessGrants.IgnoreQueryFilters().AnyAsync(grant =>
            grant.TenantId == context.TenantId && grant.SupportUserId == request.SupportUserId &&
            grant.RevokedAt == null && grant.ExpiresAt > now, cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException("The support user already has active access to this tenant.");
        }

        var grant = SupportAccessGrant.Create(context.TenantId, request.SupportUserId, actor, request.Reason, request.ExpiresAt, now);
        dbContext.SupportAccessGrants.Add(grant);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEvents.AppendAsync(new AuditEventWrite("support-access.granted", "support-access-grant", grant.Id, request.Reason,
            $"{{\"supportUserId\":\"{request.SupportUserId}\",\"expiresAt\":\"{grant.ExpiresAt:O}\"}}"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SupportAccessGrantSummary(grant.Id, grant.SupportUserId, grant.ExpiresAt, grant.Reason, grant.RevokedAt);
    }

    public async Task RevokeAsync(string grantId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.SupportGrantManage);
        var context = tenantContext.RequireCurrent();
        var actor = context.UserId ?? throw new InvalidOperationException("Support grants require an authenticated owner.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var grant = await dbContext.SupportAccessGrants.SingleOrDefaultAsync(item => item.Id == grantId, cancellationToken)
            ?? throw new InvalidOperationException("The support access grant does not exist for this tenant.");
        grant.Revoke(actor, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEvents.AppendAsync(new AuditEventWrite("support-access.revoked", "support-access-grant", grant.Id, grant.Reason), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
