using System.Data;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Tenancy;

public sealed class TenantMembershipService(AppDbContext dbContext) : ITenantMembershipService
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
        await transaction.CommitAsync(cancellationToken);
        return membership;
    }

    public async Task ChangeMembershipRoleAsync(
        string membershipId,
        TenantRole role,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantRole(role);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        await EnsureOwnerCanChangeAsync(membership, membership.IsActive && role != TenantRole.Owner, cancellationToken);
        membership.ChangeRole(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SuspendMembershipAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        await EnsureOwnerCanChangeAsync(membership, membership.IsActive, cancellationToken);
        membership.Suspend(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReactivateMembershipAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        membership.Reactivate();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeMembershipAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await GetMembershipAsync(membershipId, cancellationToken);
        await EnsureOwnerCanChangeAsync(membership, membership.IsActive, cancellationToken);
        membership.Revoke(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
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
        return await dbContext.Memberships.SingleOrDefaultAsync(membership => membership.Id == membershipId, cancellationToken)
            ?? throw new InvalidOperationException("The membership does not exist.");
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
}
