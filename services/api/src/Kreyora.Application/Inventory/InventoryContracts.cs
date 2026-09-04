using Kreyora.Application.Models;
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
    string ActorUserId,
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
