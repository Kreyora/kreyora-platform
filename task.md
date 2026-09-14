# Handoff: Milestone 06 Step 03 — Inventory Allocation, Cancellation, and Fulfilment Coordination

## 1. Overview

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** 03 — Inventory allocation, cancellation, and fulfilment coordination
- **Phase:** Phase 2 (Builder) Complete — Awaiting Review Checkpoint Approval
- **Governing Plan:** `docs/plan/M06-S03_INVENTORY_ALLOCATION_FULFILMENT_PLAN.md`
- **Checkpoint Report:** `artifacts/checkpoints/M06-S03.md`
- **Milestone Reference:** `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`

---

## 2. Implementation Tasks (Phase 2 Builder)

- [x] **Task 1: Domain Entities & Enums**
  - Added `OrderRestock` to `StockMovementType` in `Kreyora.Domain.Inventory`.
  - Added `Expire(DateTimeOffset now)` method to `PaymentAttempt` in `Kreyora.Domain.Payments`.
  - Added unit tests for `PaymentAttempt.Expire` in `Kreyora.UnitTests`.

- [x] **Task 2: Application Contracts**
  - Added `RestockForOrderAsync` method to `IOrderInventoryReservationService` in `Kreyora.Application.Inventory.InventoryContracts`.
  - Defined `OrderInventoryRestockRequest`, `OrderInventoryRestockLine`, and `OrderInventoryRestock` records.

- [x] **Task 3: Infrastructure Inventory Service Implementation**
  - Implemented `RestockForOrderAsync` in `Kreyora.Infrastructure.Inventory.InventoryService`.
  - Acquired row locks (`SELECT ... FOR UPDATE`), updated `OnHandQuantity` via `item.ApplyMovement`, and appended `StockMovement` of type `StockMovementType.OrderRestock`.

- [x] **Task 4: Order Operation Service Coordination**
  - Injected `IOrderInventoryReservationService` into `OrderOperationService`.
  - Ensured `order.Items` are loaded via `.Include(o => o.Items)`.
  - Coordinated atomic stock restock and unverified `PaymentAttempt.Expire` when `OrderAction.Cancel` is executed.
  - Recorded provider-neutral `OutboxMessage` records across all successful operational actions (`order.confirmed.v1`, `order.cancelled.v1`, `order.prepared.v1`, `order.dispatched.v1`, `order.delivered.v1`, `order.delivery_failed.v1`, `payment.verified.v1`, `payment.rejected.v1`, `payment.cod_collected.v1`).

- [x] **Task 5: Real PostgreSQL Integration Testing**
  - Created `OrderFulfilmentInventoryCoordinationTests.cs` in `Kreyora.IntegrationTests.Orders` (11 tests).
  - Tested all terminal lifecycles and verified exact inventory reconciliation (`ReconcileInventoryAsync`).
  - Tested concurrent Cancel vs. Dispatch conflicts and serializable resolution.
  - Tested duplicate Cancel command replay with idempotent zero double-restock.
  - Updated existing test helpers in `OrderOperationServiceTests.cs` and `PaymentServiceTests.cs`.

- [x] **Task 6: Quality Gates & Review Checkpoint**
  - Verified full test suite passes (170 Unit, 6 Architecture, 5 Contract, 110 Integration = 291 total).
  - Verified EF migration model has no pending changes.
  - Created review checkpoint `artifacts/checkpoints/M06-S03.md` with status `REVIEW`.
  - Updated `docs/context/CURRENT_WORK.md` and `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`.
