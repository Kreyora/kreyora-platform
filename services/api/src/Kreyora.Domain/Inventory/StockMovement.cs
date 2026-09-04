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
    public string? ActorUserId { get; private set; }
    public CommerceActorKind ActorKind { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string? ReferenceType { get; private set; }
    public string? ReferenceId { get; private set; }

    public static StockMovement Create(
        string tenantId,
        string inventoryItemId,
        string variantId,
        StockMovementType type,
        int quantityDelta,
        string reason,
        string? actorUserId,
        string idempotencyKey,
        string requestFingerprint,
        string? referenceType = null,
        string? referenceId = null,
        CommerceActorKind actorKind = CommerceActorKind.Member) => new()
    {
        TenantId = Require(tenantId, nameof(tenantId), 26),
        InventoryItemId = Require(inventoryItemId, nameof(inventoryItemId), 26),
        VariantId = Require(variantId, nameof(variantId), 26),
        Type = RequireDefined(type),
        QuantityDelta = RequireNonZero(quantityDelta),
        Reason = Require(reason, nameof(reason), ReasonMaxLength),
        ActorUserId = RequireActorUserId(actorUserId, actorKind),
        ActorKind = RequireActorKind(actorKind),
        IdempotencyKey = Require(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength),
        RequestFingerprint = Require(requestFingerprint, nameof(requestFingerprint), 64),
        ReferenceType = Optional(referenceType, nameof(referenceType), 64),
        ReferenceId = Optional(referenceId, nameof(referenceId), 160)
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

    private static string? Optional(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length > maxLength
            ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.")
            : normalized;
    }

    private static CommerceActorKind RequireActorKind(CommerceActorKind value) =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));

    private static string? RequireActorUserId(string? value, CommerceActorKind actorKind) => actorKind switch
    {
        CommerceActorKind.Member => Require(value ?? string.Empty, nameof(value), 26),
        CommerceActorKind.CommerceSystem when string.IsNullOrWhiteSpace(value) => null,
        CommerceActorKind.CommerceSystem => throw new ArgumentException("Commerce-system movements cannot have a user actor.", nameof(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(actorKind))
    };
}
