using Kreyora.Domain.Common;

namespace Kreyora.Domain.Inventory;

public sealed class InventoryReservation : BaseEntity, ITenantOwned
{
    public const int ReferenceIdMaxLength = 160;

    private InventoryReservation()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string InventoryItemId { get; private set; } = string.Empty;
    public string VariantId { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public InventoryReservationSource Source { get; private set; }
    public string ReferenceId { get; private set; } = string.Empty;
    public string? ActorUserId { get; private set; }
    public InventoryReservationState State { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? CommittedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }

    public static InventoryReservation Create(
        string tenantId,
        string inventoryItemId,
        string variantId,
        int quantity,
        InventoryReservationSource source,
        string referenceId,
        string? actorUserId,
        DateTimeOffset expiresAt,
        DateTimeOffset now) => new()
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            InventoryItemId = Require(inventoryItemId, nameof(inventoryItemId), 26),
            VariantId = Require(variantId, nameof(variantId), 26),
            Quantity = RequirePositive(quantity),
            Source = RequireDefined(source),
            ReferenceId = Require(referenceId, nameof(referenceId), ReferenceIdMaxLength),
            ActorUserId = Optional(actorUserId, 26),
            ExpiresAt = expiresAt > now ? expiresAt : throw new ArgumentOutOfRangeException(nameof(expiresAt)),
            State = InventoryReservationState.Active
        };

    public void Commit(DateTimeOffset now)
    {
        EnsureActive();
        if (ExpiresAt <= now) throw new InvalidOperationException("Expired reservations cannot be committed.");
        State = InventoryReservationState.Committed;
        CommittedAt = now;
    }

    public void Release(DateTimeOffset now)
    {
        EnsureActive();
        State = InventoryReservationState.Released;
        ReleasedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureActive();
        if (ExpiresAt > now) throw new InvalidOperationException("Only due reservations can expire.");
        State = InventoryReservationState.Expired;
        ExpiredAt = now;
    }

    private void EnsureActive()
    {
        if (State != InventoryReservationState.Active)
        {
            throw new InvalidOperationException("Only active reservations can transition.");
        }
    }

    private static int RequirePositive(int value) => value > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value));

    private static InventoryReservationSource RequireDefined(InventoryReservationSource value) =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));

    private static string Require(string value, string parameterName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
        return normalized.Length > maxLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }

    private static string? Optional(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : Require(value, nameof(value), maxLength);
}
