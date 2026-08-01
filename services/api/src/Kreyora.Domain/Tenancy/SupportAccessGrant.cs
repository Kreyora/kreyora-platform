using Kreyora.Domain.Common;

namespace Kreyora.Domain.Tenancy;

/// <summary>
/// A time-limited, owner-issued exception that lets a global PlatformSupport user
/// inspect one tenant's audit history. It is deliberately not a membership.
/// </summary>
public sealed class SupportAccessGrant : BaseEntity, ITenantOwned
{
    public const int ReasonMaxLength = 500;

    private SupportAccessGrant()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string SupportUserId { get; private set; } = string.Empty;
    public string GrantedByUserId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedByUserId { get; private set; }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public static SupportAccessGrant Create(
        string tenantId,
        string supportUserId,
        string grantedByUserId,
        string reason,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(supportUserId) || string.IsNullOrWhiteSpace(grantedByUserId))
        {
            throw new ArgumentException("Tenant, support user, and granting user IDs are required.");
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A support access reason is required.", nameof(reason))
            : reason.Trim();
        if (normalizedReason.Length > ReasonMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), $"A support access reason cannot exceed {ReasonMaxLength} characters.");
        }

        if (expiresAt <= now || expiresAt > now.AddHours(8))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Support access must expire in the next eight hours.");
        }

        return new SupportAccessGrant
        {
            TenantId = tenantId,
            SupportUserId = supportUserId,
            GrantedByUserId = grantedByUserId,
            Reason = normalizedReason,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke(string revokedByUserId, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(revokedByUserId))
        {
            throw new ArgumentException("A revoking user ID is required.", nameof(revokedByUserId));
        }

        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("The support access grant has already been revoked.");
        }

        RevokedByUserId = revokedByUserId;
        RevokedAt = occurredAt;
    }
}
