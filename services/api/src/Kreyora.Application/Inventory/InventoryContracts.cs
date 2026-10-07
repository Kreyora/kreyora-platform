using Kreyora.Application.Models;
using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;

namespace Kreyora.Application.Inventory;

public interface IInventoryService
{
    Task<Result<StockAdjustmentResult>> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<Result<InventoryBalance>> GetInventoryAsync(string variantId, CancellationToken cancellationToken = default);
    Task<Result<InventoryMovementPage>> GetStockMovementsAsync(string variantId, string? cursor, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<InventoryBalance>>> GetLowStockAsync(CancellationToken cancellationToken = default);
    Task<Result<InventoryBalance>> SetLowStockThresholdAsync(SetLowStockThresholdRequest request, CancellationToken cancellationToken = default);
    Task<Result<InventoryReconciliation>> ReconcileInventoryAsync(string variantId, CancellationToken cancellationToken = default);
    Task<Result<InventoryReservationResult>> ReserveStockAsync(ReserveStockRequest request, CancellationToken cancellationToken = default);
    Task<Result<InventoryReservationResult>> CommitReservationAsync(ReservationTransitionRequest request, CancellationToken cancellationToken = default);
    Task<Result<InventoryReservationResult>> ReleaseReservationAsync(ReservationTransitionRequest request, CancellationToken cancellationToken = default);
    Task<Result<InventoryReservationPage>> GetReservationsAsync(string variantId, InventoryReservationState? state, string? cursor, int pageSize, CancellationToken cancellationToken = default);
    Task<int> ExpireDueReservationsAsync(CancellationToken cancellationToken = default);
}

public interface ICheckoutInventoryReservationService
{
    Task<Result<IReadOnlyList<CheckoutInventoryReservation>>> ReserveForCheckoutAsync(CheckoutInventoryReservationRequest request, CancellationToken cancellationToken = default);
    Task ExpireForCheckoutAsync(string checkoutSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a conversation's active holds on these variants inside the caller's transaction, so checkout can reserve
    /// the same units atomically (M09-S05 hold handover). Returns how many holds were released.
    /// </summary>
    Task<int> ReleaseConversationHoldsAsync(string conversationId, IReadOnlyList<string> variantIds, CancellationToken cancellationToken = default);
}

public sealed record ConversationHoldLine(string VariantId, int Quantity);

public sealed record ConversationHoldRequest(string ConversationId, IReadOnlyList<ConversationHoldLine> Lines, string IdempotencyKey);

public sealed record ConversationHold(string ReservationId, string VariantId, int Quantity, DateTimeOffset ExpiresAt, InventoryReservationState State);

/// <summary>
/// System entry point for assistant holds (M09-S05): the same locked, serializable, idempotent and audited reservation
/// logic as checkout, with <see cref="InventoryReservationSource.Conversation"/> and the conversation as reference. One
/// active hold per variant per conversation; another hold for the same variant returns the existing one.
/// </summary>
public interface IConversationInventoryHoldService
{
    Task<Result<IReadOnlyList<ConversationHold>>> HoldAsync(ConversationHoldRequest request, CancellationToken cancellationToken = default);

    /// <summary>Releases this conversation's active holds (all, or the given reservation IDs); other reservations are never touched.</summary>
    Task<Result<IReadOnlyList<ConversationHold>>> ReleaseAsync(string conversationId, IReadOnlyList<string>? reservationIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationHold>> GetActiveAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Holds created for this conversation since <paramref name="since"/> (daily cap).</summary>
    Task<int> CountCreatedSinceAsync(string conversationId, DateTimeOffset since, CancellationToken cancellationToken = default);
}

public interface IOrderInventoryReservationService
{
    Task<Result<IReadOnlyList<OrderInventoryCommit>>> CommitForOrderAsync(OrderInventoryCommitRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<OrderInventoryRestock>>> RestockForOrderAsync(OrderInventoryRestockRequest request, CancellationToken cancellationToken = default);
}

public sealed record StockAdjustmentRequest(
    string VariantId,
    StockMovementType Type,
    int Quantity,
    string Reason,
    string IdempotencyKey);

public sealed record SetLowStockThresholdRequest(string VariantId, int Threshold, uint ExpectedVersion);

public sealed record InventoryBalance(
    string Id,
    string TenantId,
    string VariantId,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int LowStockThreshold,
    bool IsLowStock,
    uint Version);

public sealed record InventoryStockMovement(
    string Id,
    string InventoryItemId,
    string VariantId,
    StockMovementType Type,
    int QuantityDelta,
    string Reason,
    string? ActorUserId,
    CommerceActorKind ActorKind,
    DateTimeOffset CreatedAt);

public sealed record StockAdjustmentResult(
    InventoryBalance Balance,
    InventoryStockMovement Movement,
    bool WasReplayed);

public sealed record InventoryMovementPage(IReadOnlyList<InventoryStockMovement> Items, string? NextCursor);

public sealed record InventoryReconciliation(
    string InventoryItemId,
    string VariantId,
    int LedgerOnHandQuantity,
    int MaterializedOnHandQuantity,
    bool IsMatch);

public sealed record ReserveStockRequest(
    string VariantId,
    int Quantity,
    InventoryReservationSource Source,
    string ReferenceId,
    string IdempotencyKey);

public sealed record ReservationTransitionRequest(string ReservationId, string IdempotencyKey);

public sealed record InventoryReservationItem(
    string Id,
    string InventoryItemId,
    string VariantId,
    int Quantity,
    InventoryReservationSource Source,
    string ReferenceId,
    InventoryReservationState State,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CommittedAt,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset? ExpiredAt);

public sealed record InventoryReservationResult(
    InventoryReservationItem Reservation,
    InventoryBalance Balance,
    InventoryStockMovement? Movement,
    bool WasReplayed);

public sealed record InventoryReservationPage(IReadOnlyList<InventoryReservationItem> Items, string? NextCursor);
public sealed record CheckoutInventoryReservationRequest(string CheckoutSessionId, IReadOnlyList<CheckoutInventoryLine> Lines, DateTimeOffset ExpiresAt);
public sealed record CheckoutInventoryLine(string VariantId, int Quantity);
public sealed record CheckoutInventoryReservation(string VariantId, string InventoryReservationId);
public sealed record OrderInventoryCommitRequest(string OrderId, string CheckoutSessionId, IReadOnlyList<OrderInventoryCommitLine> Lines);
public sealed record OrderInventoryCommitLine(string InventoryReservationId, string VariantId, int Quantity);
public sealed record OrderInventoryCommit(string InventoryReservationId, string VariantId, string StockMovementId);
public sealed record OrderInventoryRestockRequest(string OrderId, string Reason, IReadOnlyList<OrderInventoryRestockLine> Lines);
public sealed record OrderInventoryRestockLine(string VariantId, int Quantity);
public sealed record OrderInventoryRestock(string VariantId, int QuantityRestocked, string StockMovementId);
