# M04-S05 — Catalog, Inventory, and Media API/Frontend Integration Plan

## Goal and boundary

Expose the approved M04 catalog, inventory, reservation, and private-media capabilities through versioned ASP.NET Core controllers, regenerate the TypeScript OpenAPI contract, and replace seller workspace fixture adapters with authenticated API adapters. Preserve explicit demo mode. Do not start storefront publishing, checkout/orders, or M04-S06’s verification campaign.

## Backend contract

- Add `v1/catalog`, `v1/inventory`, and protected `v1/media` controllers using existing `[ApiController]`, tenant-context, policy, correlation, and ProblemDetails conventions.
- Map existing `Result<T>` statuses consistently: validation `400`, missing/foreign resource `404`, authorization `403`, concurrency/idempotency conflicts `409`, create `201`, and no-content deletion initiation `202`/returned lifecycle object.
- Catalog: list/search/cursor pagination, get/create/update/archive, variants, publish state.
- Inventory: balance, movements cursor page, adjustments with idempotency key, threshold, reconciliation, reservations (reserve/commit/release/list), low-stock.
- Media: initiate/complete protected server-side upload, attach/reorder/list/delete, and authenticated read delivery. Read delivery validates a ready same-tenant asset and streams only from private storage; no static files, bucket URL, or storage key reaches the browser.
- Add OpenAPI operation names, response types, multipart/binary upload schema, and contract tests. Regenerate `apps/web/src/lib/api/generated/openapi-v1.json` and `v1.ts`; do not hand-edit generated files.

## Frontend integration

- Implement API-backed `CatalogClient` and `InventoryClient` adapters against generated types. Add a `MediaClient` only if the existing port composition needs one; otherwise keep media operations in the catalog adapter.
- `ClientProvider` selects real adapters outside explicit demo mode and fixture adapters only in demo mode. Never silently fall back to fixture data after an API error.
- Replace seller catalog list/detail/new/edit, inventory adjustment/low-stock, reservation display, and media-tab fixture calls. Preserve loading, empty, denied, validation-toast, conflict, error, and success states.
- Use backend values as authoritative after mutations; refresh or apply returned versions instead of recalculating stock, availability, price, or media order in the browser.
- Keep storefront fixture behavior unchanged; public catalog/storefront work belongs to Milestone 05.

## Security and media rules

- Every endpoint uses verified tenant context and the existing catalog/inventory policies; no tenant ID comes from the route/body as authority.
- Media upload uses server-side stream validation already established in M04-S04. Reject oversized/non-image requests before storage write; never return local paths, R2 endpoint, credentials, signed provider URLs, or original object keys.
- Browser errors use the existing API error mapper and one toast per failed operation. Do not introduce M03-S07 localization migration in this step.

## Tests and gates

- Backend integration: policy/tenant boundaries, response shape/statuses, idempotency replay, conflict mapping, private-media read/upload denial, and OpenAPI snapshot/contract generation.
- Frontend: real-adapter request mapping, provider mode selection, catalog/inventory/media loading/error/denied/conflict states, and no fixture dependency outside demo mode.
- Validate each implementation round with Release build, backend unit/architecture/contract tests, full Testcontainers integration suite, `pnpm ci:frontend`, OpenAPI generation diff check, EF drift check if model changes, and `git diff --check`.
- Testcontainers resources are verified and cleaned after the run; remove only test-created images if a clean cache is requested.

## Approval

Approve this M04-S05 plan before implementation. M04-S06 will not start automatically.
