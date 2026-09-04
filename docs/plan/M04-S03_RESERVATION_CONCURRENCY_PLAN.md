# M04-S03 — Reservation Concurrency, Expiry, and Reconciliation Plan

## Status and goal

- **Milestone:** 04 — Catalog, Inventory, and Media
- **Step:** 03 — Reservation concurrency, expiry, and reconciliation
- **Status:** Implementation complete; ready for review — 2026-09-04
- **Prerequisite:** M04-S02 approved on 2026-09-04. Its PostgreSQL integration test remains a required local/CI verification rerun with Docker available.

Add the temporary stock-hold layer above the approved M04-S02 ledger. A reservation holds available units without changing on-hand stock. Only committing a reservation reduces on-hand stock and writes a new immutable ledger movement. Releasing or expiring a reservation only returns held capacity.

The design must remain correct with concurrent seller actions, background expiry workers, duplicate messages, process restarts, and multiple application instances. No reservation endpoint, checkout, order, public-store flow, generated client, or frontend work is included in this step.

## Locked decisions

| Decision | Plan |
|---|---|
| Consistency model | `InventoryItem` remains the transactionally maintained balance. `StockMovement` remains the on-hand source of truth; active reservations are the source of truth for reserved quantity. |
| Available quantity | `OnHandQuantity - ReservedQuantity`; it must never fall below zero. |
| Reserve | Adds one active reservation and increases `ReservedQuantity`; it does **not** create a stock movement. |
| Commit | Changes an active reservation to `Committed`, decreases both `ReservedQuantity` and `OnHandQuantity`, and appends one `ReservationCommitted` movement with a reservation reference. |
| Release / expiry | Changes an active reservation to `Released` or `Expired` and decreases only `ReservedQuantity`; it does **not** change on-hand stock or create a stock movement. |
| Expiry | Default 15 minutes; server-owned configuration with a hard maximum of 60 minutes. Callers cannot supply an arbitrary expiration. |
| Concurrency | PostgreSQL serializable transactions plus a `SELECT … FOR UPDATE` lock on the current tenant's `InventoryItem`, bounded retry, and idempotency records. |
| Durable work | A Hangfire recurring job scans active tenants, creates an explicit tenant scope for each through `ITenantJobRunner`, and calls an internal expiry operation. Multiple worker instances are safe because state changes and balance updates are locked and idempotent. |
| Public surface | Application contracts and services only. HTTP, OpenAPI, generated TypeScript, fixture replacement, checkout/orders, and storefront UX remain deferred to M04-S05 or later. |

## Scope

### Included

- `InventoryReservation` aggregate with `Active`, `Committed`, `Released`, and `Expired` states.
- Reservation sources: `Checkout`, `Conversation`, and `Manual`; every reservation has a bounded source reference and verified initiating actor.
- Idempotent reserve, release, and commit operations; deterministic idempotent expiry processing.
- Transactional updates to `InventoryItem.ReservedQuantity` and, on commit only, `OnHandQuantity`.
- `ReservationCommitted` ledger movement linked to its reservation.
- PostgreSQL locking, same-tenant foreign keys, partial expiry index, state checks, mappings, migration, audit events, reconciliation, and a bounded background expiry worker.
- Domain, PostgreSQL integration, concurrency, tenant-boundary, durable-work, and migration-drift tests.
- ADR-006 documenting reservation consistency and locking.

### Explicitly deferred

- HTTP controllers, API contracts, OpenAPI, generated client, frontend adapters/pages, fixture replacement, or demo-mode changes.
- Checkout/order entities, payment confirmation, order cancellation, refund/return movements, fulfilment, provider callbacks, or customer identity modelling.
- Product media, object storage, public catalog projection, custom domains, notifications, queues, or external schedulers.
- Warehouse/location allocation, backorders, negative-stock policy changes, batches/serials, supplier purchase orders, and bulk operations.

## Domain model

### `InventoryReservation`

The reservation is tenant-owned and mutable only through explicit state transitions.

| Field | Rule |
|---|---|
| `Id`, `TenantId`, `InventoryItemId`, `VariantId` | Immutable. Composite same-tenant foreign keys protect both relations. |
| `Quantity` | Required positive whole unit; immutable after creation. |
| `Source`, `ReferenceId` | Typed source and required trimmed reference (maximum 160 characters). A reference is an opaque business identifier, never customer free text. |
| `ActorUserId` | Verified actor that created the hold; never client supplied. It provides audit attribution if an automated expiry occurs. |
| `State` | `Active`, `Committed`, `Released`, or `Expired`. Only `Active` may transition. Terminal states never transition again. |
| `ExpiresAt` | Server-calculated UTC timestamp. An active reservation is valid only before this instant. |
| `CommittedAt`, `ReleasedAt`, `ExpiredAt` | Exactly one terminal timestamp, set only for the matching terminal state. |

Allowed transitions:

```text
Active ──commit──► Committed
  │
  ├──release──► Released
  │
  └──expiry worker / lazy expiry──► Expired
```

Attempting to commit or release a terminal reservation is a conflict unless it is an exact replay of the original command. A commit arriving after `ExpiresAt` first expires the reservation in the same locked transaction, restores capacity, and returns a conflict; it can never consume expired stock.

### Reservation command idempotency

Use a new append-only `InventoryReservationCommand` record instead of overloading the M04-S02 stock-adjustment idempotency key.

| Field | Rule |
|---|---|
| `TenantId`, `Operation`, `IdempotencyKey` | Unique together. Operations are `Reserve`, `Commit`, `Release`, and `Expire`. |
| `ReservationId` | Points to the created or transitioned reservation through a tenant-inclusive relationship. |
| `RequestFingerprint` | SHA-256 of canonical, normalized operation input. Same key plus a different fingerprint returns `409 Conflict`. |
| `CreatedAt` | Immutable evidence of the accepted command. |

For expiry, the worker uses deterministic key `expiry:{reservationId}`. A retry therefore replays safely even if a process stops after persisting a transaction response but before the scheduler records success. A manual adjustment never shares this namespace; the eventual `ReservationCommitted` stock movement receives a distinct internal key derived from the accepted reservation-command record.

### Ledger extension

Extend `StockMovementType` only with `ReservationCommitted`. It has a negative delta equal to the committed reservation quantity. Add nullable immutable `ReferenceType` and `ReferenceId` columns to `StockMovement`; M04-S02 adjustment rows remain valid with null references, while commit movements use `reservation` and the reservation ID.

No movement is written for reserve, release, or expiry because these actions do not change on-hand stock. This preserves the reconciliation identity:

```text
SUM(all stock-movement deltas) == InventoryItem.OnHandQuantity
SUM(active reservation quantities) == InventoryItem.ReservedQuantity
AvailableQuantity == OnHandQuantity - ReservedQuantity
```

## Application contract and authorization plan

Extend the existing `IInventoryService`; do not add a controller.

- `ReserveStockAsync(ReserveStockRequest)` returns the reservation and updated balance.
- `CommitReservationAsync(CommitReservationRequest)` returns the reservation, resulting balance, and committed movement.
- `ReleaseReservationAsync(ReleaseReservationRequest)` returns the reservation and resulting balance.
- `GetReservationsAsync(variantId, state, cursor, pageSize)` is a tenant-scoped read for later UI/API work.
- `ReconcileInventoryAsync` expands its result to report ledger/on-hand match, active-reservation/reserved match, available value, and the count of overdue active rows still awaiting expiry work.

Reserve, commit, release, and reservation reads use existing `InventoryWrite`/`InventoryRead` permissions. Owner, Admin, and Operator may mutate; Viewer is read-only; PlatformSupport remains read-only. No request supplies the tenant ID or actor ID.

The expiry operation is deliberately **internal**, not a normal `InventoryWrite` command: it requires a verified background tenant context and is reachable only through the hosted worker's scoped service. It records the original reservation actor as `ActorUserId` and includes `automated: true` in audit metadata so audit evidence does not falsely imply a new user action.

Expected outcomes use the existing `Result<T>` style:

- `400 ValidationError`: empty/overlong IDs or references, invalid source/state, non-positive quantity, or malformed cursor.
- `404 NotFound`: current tenant cannot find the selected inventory item/reservation/variant.
- `409 Conflict`: insufficient available stock, terminal or expired transition, idempotency payload mismatch, stale serializable retry exhaustion, or a concurrent transition that lost the lock race.
- `403`: existing role enforcement rejects a Viewer or PlatformSupport write.

## Transaction, locking, and recovery design

Every state-changing operation uses the same sequence under PostgreSQL `SERIALIZABLE` isolation. The service obtains the exact tenant-scoped inventory row with `SELECT … FOR UPDATE`; this is the one lock protecting availability for that variant. It does not lock rows from any other tenant.

```text
verified tenant + permission (or internal expiry context)
  → canonicalize request and calculate fingerprint
  → begin Serializable transaction
  → look up idempotency command
       ├─ same fingerprint: replay recorded reservation/result
       └─ different fingerprint: conflict
  → lock current tenant's InventoryItem FOR UPDATE
  → expire any due Active reservations for that item, in stable ID order
  → validate requested transition and available quantity
  → change reservation state and InventoryItem balance atomically
  → for commit only: append ReservationCommitted StockMovement
  → append command record and an audit event in the same transaction
  → commit
```

The initial inventory item must already exist; reserve against an untracked variant returns not-found instead of creating a zero-stock record. This prevents a missing stock record from becoming implicitly sellable.

The bounded retry policy matches M04-S02: retry only PostgreSQL serialization failure, deadlock, or expected unique-key races, clear the tracker between attempts, and stop after three attempts. The idempotency command guarantees that a successful retry never creates a second hold, movement, or audit record.

Releasing and expiring reduce `ReservedQuantity`; committing reduces both `ReservedQuantity` and `OnHandQuantity` in the same lock scope. Existing M04-S02 manual adjustments continue to reject any adjustment that would leave on-hand lower than reserved, so an adjustment cannot invalidate an active hold.

## Persistence and migration

1. Add `InventoryReservationState`, `InventoryReservationSource`, `InventoryReservation`, and immutable `InventoryReservationCommand` in Domain.
2. Add transition methods to `InventoryItem` (`Reserve`, `ReleaseReservation`, `CommitReservation`) so application services never assign balances directly.
3. Extend `StockMovement` and its mapping with nullable `ReferenceType`/`ReferenceId`, and permit the new `ReservationCommitted` type without weakening append-only enforcement.
4. Add `DbSet`s, tenant filters, and `SaveChangesAsync` append-only protection for reservation command records.
5. Map `inventory_reservations` with:

   - primary key and alternate `(tenant_id, id)` key;
   - composite foreign keys to `(tenant_id, inventory_item_id)` and `(tenant_id, variant_id)`;
   - `quantity > 0` and terminal-timestamp/state consistency checks;
   - index `(tenant_id, inventory_item_id, state, expires_at, id)` for locking/expiry;
   - PostgreSQL partial index for active expiry scans: `WHERE state = 'Active'`.

6. Map `inventory_reservation_commands` with a tenant-inclusive reservation foreign key and unique `(tenant_id, operation, idempotency_key)` index.
7. Generate one additive, backward-compatible migration. It must preserve existing M04-S02 inventory facts and permit null movement references for historical adjustment rows.

## Hangfire expiry job

Use the repository's locked MVP job runtime: Hangfire with PostgreSQL storage. Add a recurring `InventoryReservationExpiryJob`, with typed options for enabled state, one-minute interval, and maximum 100 reservations per tenant per pass. Its composition code registers the recurring job only when enabled. The job must:

1. Create a fresh DI scope per execution and read only active tenant IDs from the unfiltered tenant table.
2. Create a fresh scoped `ITenantJobRunner` for each tenant and call `RunAsync` with a named `inventory-reservation-expiry` job envelope.
3. Resolve the internal reservation-expiry service inside that tenant scope; it processes due rows in stable `(ExpiresAt, Id)` order and uses the normal locked transition/idempotency logic.
4. Log a tenant-safe failure and continue to other tenants; never retain a tenant context after success or exception.
5. Use Hangfire's recurring-job registration and `DisableConcurrentExecution`/distributed PostgreSQL lock for one logical scheduler pass. Multiple deployed application instances remain safe even if scheduling overlaps because row locks and terminal-state checks make each expiry operation idempotent.

No external queue, cron provider, production scheduler credential, or notification is introduced. Future deployment infrastructure may invoke the same internal expiry operation through a durable job runner without changing reservation semantics.

## Audit and reconciliation

Write these audit actions in the same transaction as their state changes:

- `inventory.reservation.created`
- `inventory.reservation.committed`
- `inventory.reservation.released`
- `inventory.reservation.expired`

Metadata includes only inventory item ID, variant ID, reservation ID, source, reference ID, quantity, and `automated` where applicable. It excludes customer/payment content. Commit movement metadata references the reservation; release/expiry actions are represented by their reservation audit events.

Reconciliation does not auto-repair. It reports independently whether the ledger matches on-hand and whether active reservation totals match `ReservedQuantity`; overdue active rows are shown separately so a delayed worker is visible. An operator can then run/retry the internal expiry path, but this step has no UI or API repair action.

## Test and acceptance evidence

### Domain/unit tests

- Validate every reservation transition, terminal-state rejection, quantity/reference limits, server expiry bounds, and balance operations.
- Prove reserve/release changes only reserved quantity; commit changes reserved and on-hand; expired reservations cannot commit.
- Prove `ReservationCommitted` is a negative immutable ledger movement with reservation reference and existing M04-S02 movements remain valid.
- Prove deterministic idempotency keys/fingerprints and reconciliation calculations.

### PostgreSQL integration tests

- Reserve/release/commit/expiry flows preserve all three balance equations and write the right movement/audit evidence.
- Same key/same payload replays without a second reservation, movement, balance change, or audit record; same key/different payload conflicts.
- Two simultaneous holds competing for the last units yield no oversell: accepted reservations plus available quantity never exceed on-hand.
- Concurrent release/commit/expiry of one reservation produces exactly one terminal state and no negative reserved quantity.
- Delayed expiry is cleaned during the next reservation command; worker expiry restores capacity and is safe when run twice or by two worker instances.
- Commit followed by a rollback/failure leaves state, balance, movement, command record, and audit record unchanged; replay can then complete once.
- Cross-tenant guessed IDs, Viewer mutation, PlatformSupport mutation, inactive-tenant durable work, and worker context cleanup are rejected/proven.
- Migration applies to an empty database; EF reports no pending model changes; M04-S02 adjustment rows still reconcile after the additive migration.

### Required verification commands

```bash
dotnet test services/api/tests/Kreyora.UnitTests/Kreyora.UnitTests.csproj
dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --filter FullyQualifiedName~InventoryReservation
dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
git diff --check
```

## Implementation order

1. Obtain M04-S02 approval and rerun its integration tests with Docker available.
2. Add ADR-006 documenting the balance equations, lock target, transition semantics, and recovery model.
3. Implement reservation domain/state transitions and unit tests.
4. Add application contracts, idempotency-command persistence, `InventoryItem` balance methods, movement reference fields, mappings, filters, and the additive migration.
5. Implement locked reservation lifecycle service and extend reconciliation.
6. Add the tenant-scoped Hangfire expiry job and options; verify scope cleanup and inactive-tenant handling.
7. Add PostgreSQL integration/high-contention tests, run verification, and create `artifacts/checkpoints/M04-S03.md` for review.
8. Stop before M04-S04; do not add HTTP/frontend/storefront/order work.

## Review gate

Approve only when active reservations cannot oversell, every terminal transition is idempotent and auditable, commit is represented in the immutable on-hand ledger, expiry is tenant-safe and restart-safe, reconciliation exposes both balance equations, high-contention PostgreSQL tests pass, and no later-step public/API/frontend/order/media work has begun.
