using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Inventory;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/inventory")]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    [HttpGet("variants/{variantId}"), Authorize(Policy = TenantPermissions.InventoryRead)]
    public async Task<ActionResult<InventoryBalance>> Get(string variantId, CancellationToken cancellationToken) => this.ToActionResult(await inventory.GetInventoryAsync(variantId, cancellationToken));
    [HttpGet("low-stock"), Authorize(Policy = TenantPermissions.InventoryRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryBalance>>> LowStock(CancellationToken cancellationToken) => this.ToActionResult(await inventory.GetLowStockAsync(cancellationToken));
    [HttpPost("adjustments"), Authorize(Policy = TenantPermissions.InventoryWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<StockAdjustmentResult>> Adjust(StockAdjustmentRequest request, CancellationToken cancellationToken) => this.ToActionResult(await inventory.AdjustStockAsync(request, cancellationToken));
    [HttpGet("variants/{variantId}/movements"), Authorize(Policy = TenantPermissions.InventoryRead)]
    public async Task<ActionResult<InventoryMovementPage>> Movements(string variantId, [FromQuery] string? cursor, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) => this.ToActionResult(await inventory.GetStockMovementsAsync(variantId, cursor, pageSize, cancellationToken));
    [HttpPut("variants/{variantId}/threshold"), Authorize(Policy = TenantPermissions.InventoryWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<InventoryBalance>> Threshold(string variantId, ThresholdBody body, CancellationToken cancellationToken) => this.ToActionResult(await inventory.SetLowStockThresholdAsync(new SetLowStockThresholdRequest(variantId, body.Threshold, body.ExpectedVersion), cancellationToken));
    [HttpGet("variants/{variantId}/reconciliation"), Authorize(Policy = TenantPermissions.InventoryRead)]
    public async Task<ActionResult<InventoryReconciliation>> Reconciliation(string variantId, CancellationToken cancellationToken) => this.ToActionResult(await inventory.ReconcileInventoryAsync(variantId, cancellationToken));
    [HttpPost("reservations"), Authorize(Policy = TenantPermissions.InventoryWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<InventoryReservationResult>> Reserve(ReserveStockRequest request, CancellationToken cancellationToken) => this.ToActionResult(await inventory.ReserveStockAsync(request, cancellationToken));
    [HttpPost("reservations/{id}/commit"), Authorize(Policy = TenantPermissions.InventoryWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<InventoryReservationResult>> Commit(string id, ReservationBody body, CancellationToken cancellationToken) => this.ToActionResult(await inventory.CommitReservationAsync(new ReservationTransitionRequest(id, body.IdempotencyKey), cancellationToken));
    [HttpPost("reservations/{id}/release"), Authorize(Policy = TenantPermissions.InventoryWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<InventoryReservationResult>> Release(string id, ReservationBody body, CancellationToken cancellationToken) => this.ToActionResult(await inventory.ReleaseReservationAsync(new ReservationTransitionRequest(id, body.IdempotencyKey), cancellationToken));
    [HttpGet("variants/{variantId}/reservations"), Authorize(Policy = TenantPermissions.InventoryRead)]
    public async Task<ActionResult<InventoryReservationPage>> Reservations(string variantId, [FromQuery] Kreyora.Domain.Inventory.InventoryReservationState? state, [FromQuery] string? cursor, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) => this.ToActionResult(await inventory.GetReservationsAsync(variantId, state, cursor, pageSize, cancellationToken));
}

public sealed record ThresholdBody(int Threshold, uint ExpectedVersion);
public sealed record ReservationBody(string IdempotencyKey);
