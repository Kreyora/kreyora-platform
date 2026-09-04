# M04-S02 — Stock Ledger, Balances, and Adjustments Plan

## Status and goal

- **Milestone:** 04 — Catalog, Inventory, and Media
- **Step:** 02 — Stock ledger, balances, and adjustments
- **Status:** Implementation complete; ready for review — 2026-09-04
- **Prerequisite:** M04-S01 catalog and variant domain approved on 2026-09-04

Create the inventory source of truth for each tenant-owned product variant. The source of truth is an immutable sequence of stock movements; a per-variant balance is a transactionally maintained read model. Sellers never edit `onHand` directly.

This step creates the safe foundation that M04-S03 reservations will use. It does not reserve, commit, release, or expire stock, and does not expose any HTTP endpoint or replace the frontend fixtures.

## Locked boundaries

- Use the existing .NET modular monolith, Application contracts, Infrastructure services, EF Core, and PostgreSQL.
- `InventoryRead` and `InventoryWrite` are already defined in `TenantPermissions`. Owner, Admin, and Operator can adjust stock; Viewer can read only; PlatformSupport remains read-only.
- Every inventory record is tenant-owned, filtered by the verified tenant context, enforced in `SaveChangesAsync`, and audited through the existing audit service.
- Product and variant data stay in the Catalog module. Inventory references a variant; it does not copy editable product title, SKU, price, or availability facts.
- Quantities are whole units (`int`), not currency decimals. Negative balance is rejected by default.
- No inventory HTTP controller, OpenAPI change, generated TypeScript client, frontend change, media, checkout, order, reservation, or public-store availability behavior is in scope.

## Scope

### Included

- `InventoryItem` materialized balance for a tenant-scoped product variant.
- Immutable `StockMovement` ledger rows and adjustment reasons.
- Opening, receipt, correction increase/decrease, and damage stock adjustments.
- Low-stock threshold configuration on an inventory item.
- Tenant-scoped reads needed by later APIs: one variant balance, movement page, and low-stock list.
- Idempotent adjustment commands, serializable transactions, audit events, EF mapping, migration, unit tests, and PostgreSQL integration tests.
- Reconciliation service/query that compares the movement ledger with the materialized on-hand balance.

### Explicitly deferred

- `InventoryReservation`, reserved/committed/released/expired states, expiry jobs, and checkout/conversation locks (M04-S03).
- Decrementing stock for orders, returns, refunds, fulfilment, or provider events (later milestone work).
- Any public API, seller UI, frontend client adapter, fixture replacement, search, pagination UI, or low-stock page wiring (M04-S05).
- Product media, object storage, and public storefront projection (M04-S04/M04-S05).
- Bulk import/export, warehouse/location allocation, serial/batch tracking, suppliers, and purchase orders.

## Authoritative inventory model

### `InventoryItem` — materialized balance

One `InventoryItem` exists per `(TenantId, VariantId)` after stock is first introduced. It is a tenant-owned aggregate and contains only inventory facts.

| Field | Rule |
|---|---|
| `Id`, `TenantId`, `VariantId` | Immutable. `VariantId` is protected by a same-tenant composite foreign key. |
| `OnHandQuantity` | Non-negative whole-unit balance maintained only by the inventory service. |
| `ReservedQuantity` | Present as `0` in this step so the balance invariant is stable; only M04-S03 may change it. |
| `AvailableQuantity` | Derived, never independently stored: `OnHandQuantity - ReservedQuantity`. |
| `LowStockThreshold` | Non-negative whole-unit seller configuration; `0` disables low-stock alerting. |
| `xmin`, timestamps | PostgreSQL concurrency token and existing base timestamps. |

The current frontend fixture field named `committed` is not a backend contract. M04-S05 will map the real `ReservedQuantity`/available semantics deliberately, after reservation behavior exists. Until then, a missing `InventoryItem` means no tracked-stock record exists; it is not silently interpreted as a public availability decision.

### `StockMovement` — append-only source of truth

Each accepted adjustment writes one immutable movement in the same transaction as the balance update. The movement stores a signed `QuantityDelta`, but callers submit a positive quantity and a typed adjustment kind; the service determines the sign.

| Field | Rule |
|---|---|
| `Id`, `TenantId`, `InventoryItemId`, `VariantId` | Immutable. Both inventory item and variant must belong to the verified tenant. |
| `Type` | `OpeningBalance`, `Receipt`, `CorrectionIncrease`, `CorrectionDecrease`, or `Damage` in M04-S02. Reservation/order/return types are deferred. |
| `QuantityDelta` | Non-zero signed `int`; positive for opening/receipt/increase, negative for decrease/damage. |
| `Reason` | Required trimmed operational explanation, bounded to 500 characters. |
| `ActorUserId` | Taken from verified tenant context, never supplied by a client. |
| `IdempotencyKey`, `RequestFingerprint` | Required for manual adjustments; unique per tenant and key. A repeated key with another payload conflicts. |
| `CreatedAt` | Immutable ledger timestamp. `ModifiedAt` is not exposed or used to alter a movement. |

The database and `AppDbContext` must reject updates and deletes to `StockMovement`, just as audit events are append-only. Reconciliation computes `SUM(QuantityDelta)` for an inventory item and compares it to `OnHandQuantity`; no mutable stock field is accepted as evidence over the ledger.

### Adjustment command rules

| Command kind | Delta | Additional rule |
|---|---:|---|
| `OpeningBalance` | positive | Allowed only when the variant has no prior movement. It creates the initial inventory item when absent. |
| `Receipt` | positive | Increases on-hand stock. |
| `CorrectionIncrease` | positive | Requires a reason explaining the reconciliation/correction. |
| `CorrectionDecrease` | negative | Reject if it would make on-hand stock negative. |
| `Damage` | negative | Reject if it would make on-hand stock negative; reason records the loss. |

`OpeningBalance` is a movement, not a special editable initial quantity. Zero quantity, a raw client-supplied signed delta, a threshold below zero, a missing idempotency key, a forged variant ID, an archived product, or an insufficient-role request is rejected. Normal seller adjustments against an archived product are rejected; historic inventory and movements remain readable for later operational history.

## Persistence design and migration

1. Add `InventoryItem`, `StockMovement`, and their typed enums to the Domain layer. Both are `ITenantOwned`.
2. Add `DbSet`s and tenant query filters in `AppDbContext`.
3. Add a tenant-scoped alternate key `(TenantId, Id)` to `ProductVariant`, then use it for a composite `(TenantId, VariantId)` foreign key from `InventoryItem`. This prevents a stock record being attached to a variant from another tenant at the database layer.
4. Map `inventory_items` with:

   - primary key `id`;
   - unique `(tenant_id, variant_id)`;
   - browse index `(tenant_id, low_stock_threshold, modified_at)`;
   - checks: `on_hand_quantity >= 0`, `reserved_quantity >= 0`, `reserved_quantity <= on_hand_quantity`, and `low_stock_threshold >= 0`;
   - PostgreSQL `xmin` token.

5. Map `stock_movements` with:

   - primary key `id`;
   - foreign key to inventory item and same-tenant variant;
   - unique `(tenant_id, idempotency_key)`;
   - ledger/read index `(tenant_id, inventory_item_id, created_at, id)`;
   - checks: `quantity_delta <> 0` and an allowed enum conversion;
   - no cascade deletion of inventory history.

6. Generate one backward-compatible migration. It may add the ProductVariant alternate key required for the composite relation, but must not modify prices, product publication rules, reservations, media, or existing tenant data.

## Application-service plan

Add an `IInventoryService` contract in Application and an Infrastructure implementation. Controllers are explicitly out of scope.

Required operations:

- `AdjustStockAsync(StockAdjustmentRequest)` — creates an inventory item if necessary, adds exactly one movement, updates its balance, and returns the immutable movement plus resulting balance.
- `GetInventoryAsync(variantId)` — tenant-scoped item/balance; not-found means no tracked inventory record.
- `GetStockMovementsAsync(variantId, cursor, pageSize)` — tenant-scoped, stable cursor pagination by `(CreatedAt, Id)`.
- `GetLowStockAsync()` — only items with a positive threshold and `AvailableQuantity <= LowStockThreshold`; reservation is zero in this step, but the query shape remains future-compatible.
- `SetLowStockThresholdAsync(variantId, threshold, expectedVersion)` — changes configuration only, not stock quantity; uses `xmin` and audit evidence.
- `ReconcileInventoryAsync(variantId)` — trusted internal/service operation that returns ledger total, materialized on-hand value, and match/mismatch result. It does not silently repair data.

### Command transaction and idempotency flow

```text
verified tenant + inventory.write
  → validate request and load tenant-owned variant
  → Serializable PostgreSQL transaction
  → find idempotency key
       ├─ matching fingerprint: return original movement/result without another side effect
       └─ different fingerprint: typed 409 conflict
  → load/create one InventoryItem for tenant + variant
  → derive signed delta from allowed adjustment kind
  → reject a result below zero
  → append immutable StockMovement + update materialized balance
  → append inventory.stock.adjusted audit event in the same transaction
  → commit
```

Use serializable isolation with a small bounded retry only for PostgreSQL serialization/deadlock conflicts. The idempotency key makes a retry safe. A unique-key race while first creating an item is retried by loading the winning item; it must never create a second movement. No unbounded retry loop is permitted.

### Typed outcomes

Use the existing `Result<T>` model for expected results:

- `400 ValidationError`: invalid quantity, reason, threshold, key, forbidden opening-balance state, or invalid adjustment kind.
- `404 NotFound`: selected variant or tracked inventory item is absent in the current tenant.
- `409 Conflict`: idempotency-key payload mismatch, stale threshold update, or serializable retry exhaustion.
- `403`: existing permission authorizer rejection for Viewer and PlatformSupport writes.

No raw PostgreSQL exception detail, foreign tenant ID, or client-controlled actor is returned.

### Audit events

- `inventory.stock.adjusted` for every committed movement; metadata contains inventory item ID, variant ID, movement type, and absolute quantity—not customer or payment data.
- `inventory.low-stock-threshold.updated` when the threshold changes.
- A detected reconciliation mismatch is surfaced to the caller/logging path and may be audited as `inventory.reconciliation.mismatch`; it is not auto-corrected in this step.

## Tests and acceptance evidence

### Domain/unit tests

- Validate allowed adjustment kinds, non-zero/positive submitted quantity, derived direction, reason bounds, and threshold bounds.
- Prove a negative result is rejected and that a movement cannot be altered after creation.
- Prove `AvailableQuantity` is derived from on-hand minus reserved and reserved remains zero in this step.
- Prove low-stock logic is disabled at threshold `0` and triggers at/below a positive threshold.

### PostgreSQL integration tests

- Two tenants can use the same variant-shaped IDs only within their own scopes; one cannot read, adjust, or configure another tenant's inventory by a guessed ID.
- A stock adjustment creates one movement and a matching materialized balance; reconciliation matches the ledger total.
- A duplicate same-key/same-payload adjustment creates one movement and replays safely; the same key with a different payload conflicts.
- Opening balance cannot be applied twice; negative, damage, and correction decreases cannot reduce on-hand below zero.
- Owner/Admin/Operator write behavior and Viewer/PlatformSupport denial follow the existing permission matrix.
- Audit records contain verified tenant and actor context; ledger rows reject update/delete attempts.
- Concurrent adjustments against the same variant retain every accepted delta, have no lost update, and leave ledger total equal to balance.
- Migration applies to an empty PostgreSQL database and does not report model drift.

### Out of scope verification

- No reservation table, reservation background job, checkout/order movement, media model, controller, OpenAPI document, generated TypeScript, or frontend adapter is added.
- Existing mock inventory UI remains unchanged until M04-S05.

## Implementation order

1. Inspect the approved M04-S01 catalog mappings and current inventory fixture vocabulary; retain only the backend facts needed here.
2. Add the inventory domain aggregate, movement enum, immutable ledger rules, and focused unit tests.
3. Add application contracts and typed result models.
4. Add EF mapping, tenant filters, append-only enforcement, composite Catalog variant relationship, and migration.
5. Implement the inventory service with permission checks, serializable/idempotent adjustment path, balance updates, threshold configuration, audit events, and reconciliation read.
6. Add integration tests for tenancy, role denial, duplicate commands, negative-balance prevention, concurrency, append-only ledger, audit, migration, and reconciliation.
7. Run focused and full relevant backend tests, migration script validation, and `git diff --check`; produce `artifacts/checkpoints/M04-S02.md` for review. Stop before M04-S03.

## Review gate

Approve implementation only when stock is ledger-derived and append-only, balance updates are transactional and tenant-safe, adjustments are authorized/audited/idempotent, negative stock is rejected, reconciliation is observable, concurrency has evidence, and no reservation/API/frontend/media work has started.
