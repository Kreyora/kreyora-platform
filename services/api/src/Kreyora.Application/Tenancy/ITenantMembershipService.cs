using Kreyora.Domain.Tenancy;

namespace Kreyora.Application.Tenancy;

public interface ITenantMembershipService
{
    Task<Tenant> CreateTenantForOwnerAsync(CreateTenantForOwnerRequest request, CancellationToken cancellationToken = default);
    Task<Membership> GrantMembershipAsync(string tenantId, string userId, TenantRole role, CancellationToken cancellationToken = default);
    Task ChangeMembershipRoleAsync(string membershipId, TenantRole role, CancellationToken cancellationToken = default);
    Task SuspendMembershipAsync(string membershipId, CancellationToken cancellationToken = default);
    Task ReactivateMembershipAsync(string membershipId, CancellationToken cancellationToken = default);
    Task RevokeMembershipAsync(string membershipId, CancellationToken cancellationToken = default);
}

public sealed record CreateTenantForOwnerRequest(string OwnerUserId, string DisplayName, string Slug);
