# Milestone 04 — Catalog, Inventory, and Media

## Objective

Implement the real product source of truth: tenant-scoped catalog facts, variants, private media, publication readiness, append-only stock movements, balances, and concurrency-safe expiring reservations. Replace the corresponding frontend fixtures with real API adapters.

## Dependencies

- Milestone 03 tenant and authorization exit gate approved.
- Object-storage provider may remain a development implementation, but the storage contract and tenant paths must be production-safe.

## Implementation design

Catalog owns product identity, description, variants, price facts, media references, and publication state. Inventory owns stock movements, current balance, reservations, release, and commitment. Do not store stock as an unaudited editable product field.

Purchasability requires an active store later, a published product/variant, a valid canonical price, and sufficient available inventory. Reservation operations use PostgreSQL transactions and concurrency control. Every command is tenant-scoped, authorized, idempotent where retryable, and audited.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Catalog and variant domain | `NOT STARTED` |
| 02 | Stock ledger, balances, and adjustments | `NOT STARTED` |
| 03 | Reservation concurrency, expiry, and reconciliation | `NOT STARTED` |
| 04 | Media authorization and object-storage abstraction | `NOT STARTED` |
| 05 | Catalog/inventory APIs and frontend integration | `NOT STARTED` |
| 06 | Contention, isolation, and end-to-end verification | `NOT STARTED` |

## Prompt 01 — Catalog and variant domain

> Implement the Catalog module with Product, ProductVariant, canonical NPR price facts, publication state, optional category/collection primitives needed by the approved UI, and validation/readiness rules. Define stable IDs/slugs, SKU uniqueness per tenant, variant option representation, status transitions, and update concurrency. Persist with tenant-aware mappings and indexes. Add service methods, authorization, idempotency for retryable creates, audit events, migrations, and domain/integration tests. Do not implement inventory or public storefront projection yet.

**Review checkpoint:** approve the catalog model, price ownership, publication rules, and API-independent tests.

## Prompt 02 — Stock ledger, balances, and adjustments

> Implement InventoryItem and append-only StockMovement for receipts, corrections, damage, reservation commit/release effects, and other approved reasons. Derive or transactionally maintain on-hand, reserved, and available balances with a documented consistency model. Stock adjustments require authorization, reason, idempotency key, actor, tenant, and audit event. Prevent negative quantities except through an explicitly rejected-by-default policy. Add migrations, service methods, reconciliation logic, and tests for duplicate requests, corrections, and cross-tenant isolation.

**Review checkpoint:** approve ledger semantics, balance calculations, adjustment permissions, and reconciliation evidence.

## Prompt 03 — Reservation concurrency, expiry, and reconciliation

> Implement InventoryReservation with `active`, `committed`, `released`, and `expired` states, source/reference, quantity, expiry, tenant, and idempotency key. Use PostgreSQL transactions and an explicitly documented locking or optimistic-concurrency strategy so simultaneous reservations cannot oversell. Implement idempotent reserve, release, commit, and expiry jobs; job tenant context must be explicit. Add recovery/reconciliation for interrupted jobs. Run high-contention integration tests, duplicate job tests, and boundary tests around expiry and commitment.

**Review checkpoint:** approve concurrency design and evidence that available stock never becomes incorrectly oversold.

## Prompt 04 — Media authorization and object-storage abstraction

> Implement MediaAsset metadata and a provider-neutral object-storage interface. Support authorized upload initiation, content-type/size allowlists, tenant-scoped object keys, private originals, safe read URLs, attachment to products, ordering, alt text, deletion lifecycle, and orphan cleanup. Use a local development implementation and a configuration seam for Cloudflare R2; do not require production credentials. Treat client filenames and MIME types as untrusted. Add tests for tenant isolation, path traversal, disallowed types/sizes, unauthorized attachment, expired URLs, and cleanup.

**Review checkpoint:** approve storage-key scheme, upload security, media lifecycle, and R2 configuration contract.

## Prompt 05 — Catalog/inventory APIs and frontend integration

> Expose versioned catalog, variant, media, stock movement, balance, reservation, publication, search/filter, pagination, and low-stock APIs following repository conventions. Enforce policies and tenant context on every endpoint. Replace Milestone 01 catalog, inventory, product editor, media, and low-stock fixtures with generated real API clients while preserving explicit demo mode. Implement validation/error mapping, concurrency-conflict UX, audit links, and permission-denied states. Add contract and end-to-end tests.

**Review checkpoint:** approve API/UX behavior and verify feature components no longer depend on fixtures outside demo mode.

## Prompt 06 — Contention, isolation, and end-to-end verification

> Execute a milestone verification campaign with two tenants and high-contention inventory scenarios. Prove SKU/slug uniqueness scope, catalog authorization, private media isolation, duplicate command idempotency, stock-ledger reconciliation, reservation expiry, simultaneous reserve/commit/release behavior, and safe frontend conflict recovery. Include load parameters, database observations, and repeatable test commands in the checkpoint report. Fix milestone-scoped defects and produce a catalog/inventory invariant matrix.

**Review checkpoint:** approve the invariant matrix with all mandatory tests passing.

## Milestone exit gate

- Real catalog and inventory screens work for authorized roles.
- Only valid published variants with canonical prices can become purchasable inputs.
- Stock movements reconcile and cannot be silently edited.
- Duplicate commands/jobs are idempotent.
- Concurrent reservations cannot oversell.
- Reservation expiry safely releases availability.
- Media is private, validated, tenant-scoped, and safely delivered.

