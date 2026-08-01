using Kreyora.Domain.Common;

namespace Kreyora.Domain.Tenancy;

public sealed class Membership : BaseEntity
{
    private Membership()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public TenantRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    public static Membership Grant(string tenantId, string userId, TenantRole role)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        EnsureValidRole(role);

        return new Membership
        {
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            Status = MembershipStatus.Active
        };
    }

    public void ChangeRole(TenantRole role)
    {
        EnsureValidRole(role);
        Role = role;
    }

    public void Suspend(DateTimeOffset occurredAt)
    {
        Status = MembershipStatus.Suspended;
        SuspendedAt = occurredAt;
    }

    public void Reactivate()
    {
        Status = MembershipStatus.Active;
        SuspendedAt = null;
        RevokedAt = null;
    }

    public void Revoke(DateTimeOffset occurredAt)
    {
        Status = MembershipStatus.Revoked;
        RevokedAt = occurredAt;
    }

    private static void EnsureValidRole(TenantRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
    }
}
