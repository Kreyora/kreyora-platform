using Kreyora.Domain.Common;

namespace Kreyora.Domain.Orders;

/// <summary>
/// Failed chat verifications for one order (M09-S04 Q4). Five failures inside a 24-hour window lock chat lookups of
/// that order until the window ends, so the four phone digits cannot be guessed through the assistant.
/// </summary>
public sealed class OrderLookupGuard : BaseEntity, ITenantOwned
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private OrderLookupGuard() { }

    public string TenantId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;
    public int FailureCount { get; private set; }
    public DateTimeOffset WindowStartedAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public static OrderLookupGuard Create(string tenantId, string orderId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        return new OrderLookupGuard { TenantId = tenantId, OrderId = orderId, WindowStartedAt = now };
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>Records a failed verification; returns true when this failure locks the order.</summary>
    public bool RegisterFailure(DateTimeOffset now)
    {
        if (now - WindowStartedAt >= Window)
        {
            WindowStartedAt = now;
            FailureCount = 0;
            LockedUntil = null;
        }

        FailureCount++;
        if (FailureCount >= MaxFailures && !IsLocked(now))
        {
            LockedUntil = WindowStartedAt + Window;
            return true;
        }

        return false;
    }

    public void Reset(DateTimeOffset now)
    {
        FailureCount = 0;
        WindowStartedAt = now;
        LockedUntil = null;
    }
}
