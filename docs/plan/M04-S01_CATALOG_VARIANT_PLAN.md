# M04-S01 — Catalog and Variant Domain Plan

## Status and goal

- **Milestone:** 04 — Catalog, Inventory, and Media
- **Step:** 01 — Catalog and variant domain
- **Status:** Implementation complete; review pending
- **Prerequisite:** M03 exit gate approved on 2026-09-04

Establish the tenant-scoped, authoritative product and variant model. This step creates the catalog facts that later inventory, media, storefront, checkout, and AI tools will read. It does not make stock, media, public storefronts, or frontend catalog screens real yet.

## Locked boundaries

This plan follows the accepted architecture without a new ADR:

- .NET modular monolith with traditional controllers and service-layer orchestration (ADR-001).
- PostgreSQL and EF Core persistence.
- Verified tenant context remains authoritative; every catalog entity is tenant-owned and has a tenant query filter.
- Existing `catalog.read` and `catalog.write` permissions control access. Owner, Admin, and Operator may write; Viewer remains read-only; PlatformSupport remains read-only and cannot mutate catalog data.
- Existing audit infrastructure records successful catalog mutations with actor, tenant, target, correlation ID, and safe metadata.
- Canonical commerce price is server-owned NPR data. Neither the client nor a future AI tool can set an arbitrary currency or derive a price elsewhere.

## Scope

### Included

- `Product` and `ProductVariant` domain entities, state transitions, validation, and domain-level tests.
- Canonical NPR price and optional compare-at price stored with each variant.
- Tenant-scoped product slug and SKU uniqueness.
- Product/variant create, read, update, archive, and publication-transition application services.
- EF configurations, migration, indexes, query filters, optimistic concurrency, audit events, idempotent product creation, and focused unit/integration tests.

### Explicitly deferred

- Inventory items, stock balance, movements, reservations, and availability calculations (M04-S02/S03).
- Product images, uploads, media metadata, storage, and signed URLs (M04-S04).
- HTTP catalog APIs, generated contracts, real frontend adapters, search/filter UI, and fixture replacement (M04-S05).
- Public-store publication projection, cart, checkout, discounts, tax, delivery, and custom domains (later milestones).
- Categories, collections, and product media relations unless a current approved UI cannot function without a minimal read-only placeholder. The source of truth for them should otherwise be planned with M04-S05 instead of added speculatively here.

## Domain design

### Product

`Product` is a tenant-owned aggregate root. It owns its variant collection and never stores editable stock.

| Field | Rule |
|---|---|
| `Id` | Existing ULID-style `BaseEntity` ID. |
| `TenantId` | Required, immutable, and taken only from verified tenant context. |
| `Title` | Required trimmed seller-facing title; max length defined beside the entity. |
| `Description` | Optional/required according to existing product editor needs; normalized and bounded. |
| `Slug` / `NormalizedSlug` | Stable lowercase slug; unique within the tenant. Changes only through an explicit update command. |
| `PublishState` | `Draft`, `Published`, `Unpublished`, `Archived`. |
| `Variants` | Private mutable collection; only aggregate methods can add or update variants. |
| `CreatedAt` / `ModifiedAt` | Existing `BaseEntity` timestamps. |
| `xmin` | PostgreSQL optimistic-concurrency token for seller updates. |

Allowed state transitions:

```text
Draft ──────────────> Published
Draft ──────────────> Archived
Published ──────────> Unpublished
Published ──────────> Archived
Unpublished ────────> Published
Unpublished ────────> Archived
Archived ───────────> no transition in M04-S01
```

Publishing requires at least one publishable variant. A publishable variant has a non-empty unique SKU, a valid positive NPR price, and valid normalized option data. Inventory availability is deliberately not a publish prerequisite in this step; that rule belongs after M04-S02/S03.

### ProductVariant

`ProductVariant` is tenant-owned and belongs to exactly one product in the same tenant.

| Field | Rule |
|---|---|
| `Id`, `TenantId`, `ProductId` | Required and immutable after creation. The persistence relation prevents cross-tenant attachment. |
| `Sku` / `NormalizedSku` | Required normalized seller SKU; unique per tenant, including across different products. |
| `Name` | Required display name. A default variant may use the product title only through an explicit creation rule. |
| `Options` | Canonical key/value option set (for example, `Size: M`, `Color: Black`), normalized and persisted as JSONB. Duplicate normalized option names are rejected. |
| `PriceNpr` | Positive decimal, scale 2, represented as NPR only. |
| `CompareAtPriceNpr` | Optional; when supplied it cannot be below `PriceNpr`. |
| `IsPublished` | A product cannot be published unless it has at least one published valid variant. |
| `xmin`, timestamps | Supports safe concurrent editor updates. |

No stock quantity, image URL, media ID, cart price, or storefront-specific field is added to either entity in this step.

## Persistence and migration plan

1. Add `DbSet<Product>` and `DbSet<ProductVariant>` to `AppDbContext`.
2. Add EF configurations in the established persistence configuration folder, with snake_case table and column names.
3. Apply tenant query filters to both entities, matching the existing `AuditEvent` and durable-work model.
4. Create database constraints and indexes:

   - `products`: primary key `id`; unique `(tenant_id, normalized_slug)`; browse index `(tenant_id, publish_state, modified_at)`.
   - `product_variants`: primary key `id`; composite same-tenant product relation; unique `(tenant_id, normalized_sku)`; lookup index `(tenant_id, product_id)`.
   - Numeric checks for positive sale price and compare-at price not below sale price when present.
   - JSONB options column, with application validation before persistence.
   - PostgreSQL `xmin` concurrency tokens for product and variant updates.

5. Generate one backward-compatible migration. It creates only catalog tables/indexes/constraints; it does not change identity, tenancy, stock, or media tables.
6. Confirm the migration applies to a fresh database and the current local development database without pending-model drift.

## Application-service plan

Create an Application-layer catalog contract and an Infrastructure implementation. Controllers are not part of this step; tests may call the service directly.

Required operations:

- `CreateProductAsync` — accepts a product with its initial variant set and an idempotency key.
- `GetProductAsync` and `ListProductsAsync` — tenant-scoped read models for future APIs.
- `UpdateProductAsync` — title, description, slug, and expected concurrency value.
- `AddVariantAsync` and `UpdateVariantAsync` — SKU, name, options, prices, published flag, and expected concurrency value.
- `ChangePublicationStateAsync` — applies the state machine and publish-readiness rules.
- `ArchiveProductAsync` — terminal archive operation; it does not physically delete catalog facts.

Every write operation must:

1. Require verified tenant context.
2. Demand `catalog.write`; reads demand `catalog.read`.
3. Load and query only within the active tenant.
4. Return a typed result for not-found, duplicate slug/SKU, invalid transition, validation failure, and concurrency conflict rather than exposing database exception text.
5. Append a safe audit event after a successful mutation: `catalog.product.created`, `catalog.product.updated`, `catalog.product.published`, `catalog.product.unpublished`, `catalog.product.archived`, `catalog.variant.created`, or `catalog.variant.updated`.

For retryable creates, use a catalog command idempotency record keyed by tenant, operation, and supplied key. Do not repurpose the current durable-message `IdempotencyRecord` until its scope and stored-result semantics are explicitly reviewed; it currently cannot prove a catalog-create replay is safe.

## Tests and acceptance evidence

### Domain tests

- Reject empty/overlong title, invalid slug, empty SKU, duplicate normalized option keys, invalid NPR values, and compare-at price below sale price.
- Enforce the publication state transition table.
- Reject publication without a valid published variant.
- Verify archive is terminal in this milestone.

### Persistence and service integration tests

- Tenant A cannot read or mutate Tenant B's products or variants, including by guessed product/variant IDs.
- Product slugs and SKUs may repeat across tenants but not inside one tenant.
- A variant cannot be attached to a product from another tenant.
- Viewer and read-only PlatformSupport cannot create or modify catalog records; Owner/Admin/Operator permissions follow the existing matrix.
- Duplicate create requests with the same tenant/operation/idempotency key return the original result and do not create another aggregate or audit event.
- Concurrent updates with a stale `xmin` produce a typed conflict and preserve the winning update.
- Every successful mutation has the expected tenant-scoped audit entry and correlation ID.
- Migration and model snapshot are clean; no tests require media, stock, or HTTP endpoints.

## Implementation order

1. Inspect existing catalog fixtures and permission/audit conventions; confirm final field limits from the seller UI.
2. Add domain entities, enums/value types, aggregate methods, and unit tests.
3. Add Application contracts and typed command results.
4. Add EF mapping, query filters, constraints, migration, and tenant-isolation tests.
5. Implement the catalog service with authorization, idempotency, concurrency handling, and audit writes.
6. Run focused unit/integration tests, full backend suite, formatting, migration validation, and `git diff --check`.
7. Produce `artifacts/checkpoints/M04-S01.md` for review. Stop; do not begin stock ledger work.

## Review gate

Approve M04-S01 only when the catalog aggregate is tenant-safe, price ownership is clear, uniqueness and state transitions are database-backed, all write paths are authorized/audited/idempotent where required, stale updates have a safe conflict outcome, and M04-S02 has not begun.
