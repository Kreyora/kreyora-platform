using System.Data;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Tenancy;

public sealed class TenantMembershipService(
    AppDbContext dbContext,
    ITenantContextAccessor? tenantContext = null,
    ITenantPermissionAuthorizer? permissionAuthorizer = null,
    IAuditEventService? auditEvents = null) : ITenantMembershipService
{
    public async Task<Tenant> CreateTenantForOwnerAsync(
        CreateTenantForOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await EnsureUserExistsAsync(request.OwnerUserId, cancellationToken);

        var tenant = Tenant.Create(request.DisplayName, request.Slug);
        dbContext.Tenants.Add(tenant);
        dbContext.Memberships.Add(Membership.Grant(tenant.Id, request.OwnerUserId, TenantRole.Owner));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return tenant;
    }

    public async Task<Membership> GrantMembershipAsync(
        string tenantId,
        string userId,
        TenantRole role,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantRole(role);
        DemandMembershipManagement(role);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await EnsureTenantExistsAsync(tenantId, cancellationToken);
        await EnsureUserExistsAsync(userId, cancellationToken);

        var existingMembership = await dbContext.Memberships
            .SingleOrDefaultAsync(membership => membership.TenantId == tenantId && membership.UserId == userId, cancellationToken);
        if (existingMembership is not null)
        {
            throw new InvalidOperationException("The user already has a membership for this tenant.");
        }

        var membership = Membership.Grant(tenantId, userId, role);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AppendAuditAsync("membership.granted", membership, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return membership;
    }

    public async Task<Membership> GrantMembershipByEmailAsync(string email, TenantRole role, CancellationToken cancellationToken = default)
    {
        var context = tenantContext?.RequireCurrent() ?? throw new InvalidOperationException("A verified tenant context is required.");
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken)
            ?? throw new InvalidOperationException("No registered Kreyora account exists for that email address.");
        return await GrantMembershipAsync(context.TenantId, user.Id, role, cancellationToken);
    }

    public async Task<IReadOnlyList<MembershipSummary>> GetMembersAsync(CancellationToken cancellationToken = default)
    {
        permissionAuthorizer?.Demand(TenantPermissions.MembershipManage);
        var context = tenantContext?.RequireCurrent() ?? throw new InvalidOperationException("A verified tenant context is required.");
        return await (from membership in dbContext.Memberships.AsNoTracking()
                      join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                      where membership.TenantId == context.TenantId
                      orderby membership.CreatedAt
                      select new MembershipSummary(membership.Id, user.Id, user.DisplayName, user.Email!, membership.Role, membership.Status, membership.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task ChangeMembershipRoleAsync(
        string membershipId,
        TenantRole role,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantRole(role);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        DemandMembershipManagement(membership.Role);
        DemandMembershipManagement(role);
        await EnsureOwnerCanChangeAsync(membership, membership.IsActive && role != TenantRole.Owner, cancellationToken);
        membership.ChangeRole(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AppendAuditAsync("membership.role-changed", membership, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SuspendMembershipAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        DemandMembershipManagement(membership.Role);
        await EnsureOwnerCanChangeAsync(membership, membership.IsActive, cancellationToken);
        membership.Suspend(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AppendAuditAsync("membership.suspended", membership, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReactivateMembershipAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        DemandMembershipManagement(membership.Role);
        membership.Reactivate();
        await dbContext.SaveChangesAsync(cancellationToken);
        await AppendAuditAsync("membership.reactivated", membership, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeMembershipAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        DemandMembershipManagement(membership.Role);
        await EnsureOwnerCanChangeAsync(membership, membership.IsActive, cancellationToken);
        membership.Revoke(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AppendAuditAsync("membership.revoked", membership, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnsureTenantRole(TenantRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
    }

    private async Task EnsureTenantExistsAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Tenants.AnyAsync(tenant => tenant.Id == tenantId, cancellationToken))
        {
            throw new InvalidOperationException("The tenant does not exist.");
        }
    }

    private async Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            throw new InvalidOperationException("The user does not exist.");
        }
    }

    private async Task<Membership> GetMembershipAsync(string membershipId, CancellationToken cancellationToken)
    {
        var membership = await dbContext.Memberships.SingleOrDefaultAsync(candidate => candidate.Id == membershipId, cancellationToken)
            ?? throw new InvalidOperationException("The membership does not exist.");

        var context = tenantContext?.Current;
        if (context is not null && membership.TenantId != context.TenantId)
        {
            throw new UnauthorizedAccessException("The membership is not available in the selected workspace.");
        }

        return membership;
    }

    private async Task EnsureOwnerCanChangeAsync(Membership membership, bool removesActiveOwner, CancellationToken cancellationToken)
    {
        if (!removesActiveOwner || membership.Role != TenantRole.Owner)
        {
            return;
        }

        var activeOwnerCount = await dbContext.Memberships.CountAsync(
            candidate => candidate.TenantId == membership.TenantId
                && candidate.Role == TenantRole.Owner
                && candidate.Status == MembershipStatus.Active,
            cancellationToken);

        if (activeOwnerCount <= 1)
        {
            throw new InvalidOperationException("A tenant must retain at least one active Owner membership.");
        }
    }

    private void DemandMembershipManagement(TenantRole targetRole)
    {
        var context = tenantContext?.Current;
        if (context is null)
        {
            return; // bootstrap/seeding calls run before a seller context exists.
        }

        permissionAuthorizer?.Demand(TenantPermissions.MembershipManage);
        if (!TenantPermissions.CanManageMembership(context, targetRole))
        {
            throw new UnauthorizedAccessException("The current role cannot manage an Owner membership.");
        }
    }

    private Task AppendAuditAsync(string action, Membership membership, CancellationToken cancellationToken) =>
        tenantContext?.Current?.UserId is not null && auditEvents is not null
            ? auditEvents.AppendAsync(new AuditEventWrite(action, "membership", membership.Id,
                Metadata: $"{{\"userId\":\"{membership.UserId}\",\"role\":\"{membership.Role}\",\"status\":\"{membership.Status}\"}}"), cancellationToken)
            : Task.CompletedTask;
}
