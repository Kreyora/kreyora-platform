using Kreyora.Domain.Common;
using Kreyora.Domain.Integrations;

namespace Kreyora.Domain.Customers;

/// <summary>
/// A customer as seen on one channel connection (e.g. an Instagram-scoped user ID on one business
/// account). Scoped to a single tenant and connection; never merged across connections automatically.
/// Linking to a checkout <see cref="Customer"/> is an explicit later action (ADR-016).
/// </summary>
public sealed class CustomerChannelIdentity : BaseEntity, ITenantOwned
{
    public const int ExternalUserIdMaxLength = 128;
    public const int DisplayNameMaxLength = 160;

    private CustomerChannelIdentity() { }

    public string TenantId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public ChannelType Channel { get; private set; }
    public string ExternalUserId { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string? CustomerId { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? ErasedAt { get; private set; }

    public static CustomerChannelIdentity Create(
        string tenantId,
        string connectionId,
        ChannelType channel,
        string externalUserId,
        DateTimeOffset seenAt)
    {
        return new CustomerChannelIdentity
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            ConnectionId = Require(connectionId, nameof(connectionId), 26),
            Channel = channel,
            ExternalUserId = Require(externalUserId, nameof(externalUserId), ExternalUserIdMaxLength),
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt
        };
    }

    /// <summary>Records customer activity; out-of-order events never move the timestamps backwards.</summary>
    public void RecordActivity(DateTimeOffset seenAt)
    {
        if (seenAt > LastSeenAt)
        {
            LastSeenAt = seenAt;
        }

        if (seenAt < FirstSeenAt)
        {
            FirstSeenAt = seenAt;
        }
    }

    /// <summary>Provider-supplied display name. Ignored after erasure.</summary>
    public void UpdateDisplayName(string? displayName)
    {
        if (ErasedAt.HasValue || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var trimmed = displayName.Trim();
        DisplayName = trimmed.Length > DisplayNameMaxLength ? trimmed[..DisplayNameMaxLength] : trimmed;
    }

    /// <summary>
    /// Right-to-erasure: removes the display name. The external ID is kept so later messages from the same
    /// account still resolve to this identity instead of silently creating a duplicate.
    /// </summary>
    public bool Erase(DateTimeOffset now)
    {
        if (ErasedAt.HasValue)
        {
            return false;
        }

        DisplayName = null;
        ErasedAt = now;
        return true;
    }

    /// <summary>Inbox label without exposing the full provider ID, e.g. "Instagram user ·4821".</summary>
    public static string MaskedLabel(ChannelType channel, string externalUserId)
    {
        var suffix = externalUserId.Length <= 4 ? externalUserId : externalUserId[^4..];
        return $"{channel} user ·{suffix}";
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
}
