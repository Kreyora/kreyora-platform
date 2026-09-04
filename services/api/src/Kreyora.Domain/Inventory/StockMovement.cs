using Kreyora.Domain.Common;

namespace Kreyora.Domain.Inventory;

public sealed class StockMovement : BaseEntity, ITenantOwned
{
    public const int ReasonMaxLength = 500;
    public const int IdempotencyKeyMaxLength = 256;

    private StockMovement()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string InventoryItemId { get; private set; } = string.Empty;
    public string VariantId { get; private set; } = string.Empty;
    public StockMovementType Type { get; private set; }
    public int QuantityDelta { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ActorUserId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;

    public static StockMovement Create(
        string tenantId,
        string inventoryItemId,
        string variantId,
        StockMovementType type,
        int quantityDelta,
        string reason,
        string actorUserId,
        string idempotencyKey,
        string requestFingerprint) => new()
    {
        TenantId = Require(tenantId, nameof(tenantId), 26),
        InventoryItemId = Require(inventoryItemId, nameof(inventoryItemId), 26),
        VariantId = Require(variantId, nameof(variantId), 26),
        Type = RequireDefined(type),
        QuantityDelta = RequireNonZero(quantityDelta),
        Reason = Require(reason, nameof(reason), ReasonMaxLength),
        ActorUserId = Require(actorUserId, nameof(actorUserId), 26),
        IdempotencyKey = Require(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength),
        RequestFingerprint = Require(requestFingerprint, nameof(requestFingerprint), 64)
    };

    private static StockMovementType RequireDefined(StockMovementType type) =>
        Enum.IsDefined(type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(type));

    private static int RequireNonZero(int value) => value != 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value), "Stock movement quantity cannot be zero.");

    private static string Require(string value, string parameterName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
        return normalized.Length > maxLength
            ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.")
            : normalized;
    }
}
