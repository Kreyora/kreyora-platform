# M04-S06 — Catalog, Inventory, and Media Verification Plan

## Gate, goal, and boundary

M04-S05 is approved. M04-S06 is a verification and milestone-defect-fix campaign, not a feature step. It must prove the M04 invariants against real PostgreSQL with two tenants and repeatable concurrent work before the Milestone 04 exit gate can be reviewed.

In scope: focused backend/API/frontend tests, measured database assertions, small fixes exposed by those tests, the invariant matrix, and the review checkpoint. Out of scope: storefront publication/projection, checkout/orders, public media, collections, new domain features, migrations unless a verification-discovered M04 defect strictly requires one, provider credentials, deployment, and starting Milestone 05.

## Required environment and cleanup

- Use the existing `PostgresFixture` and `Testcontainers.PostgreSql`; do not substitute SQLite or an in-memory provider for concurrency evidence.
- Every contention case uses PostgreSQL `SERIALIZABLE` transactions and the production lock path, not test-only synchronization.
- After every full verification run, confirm Testcontainers has removed its containers, stop any local compose stack used for OpenAPI/browser checks, remove the disposable `postgres:16-alpine` and `testcontainers/ryuk` images by immutable ID when present, and preserve named project volumes.
- Record exact commands, test counts, load parameters, elapsed duration if available, and database observations in the checkpoint. Do not claim a performance SLA.

## Verification matrix

| Invariant | Scenario and parameters | Required evidence |
|---|---|---|
| Tenant-scoped product uniqueness | Tenant A and Tenant B each create the same slug and SKU; Tenant A repeats each value. | Cross-tenant succeeds; same-tenant is `409`; query results expose only current tenant rows. |
| Catalog API authorization | Anonymous, Viewer, Operator, Admin/Owner requests against list/read/write/archive/variant/publication routes using a verified tenant selection. | `401`/`403` as applicable; allowed role succeeds; foreign ID returns `404` with no data leak. |
| Private-media isolation | Tenant A uploads/attaches a ready image; Tenant B lists, reads, attaches, reorders, and deletes that media ID. | Tenant B sees `404`/denial and receives no bytes/object key; Tenant A can read its own content. Test invalid type, size, traversal-like filename, expired/incomplete asset and cleanup lifecycle where supported. |
| Idempotent commands | Replay identical product create, stock adjustment, reserve, commit, and release requests; retry every operation once with the same key and once with a differing payload/key reuse. | Identical retry returns original result with no duplicate row/audit/movement; differing payload returns conflict. |
| Ledger reconciliation | Receipt/correction/damage, reserve, release, commit, and expiry sequence. | `ledgerOnHand == materializedOnHand`; active-reservation sum equals `ReservedQuantity`; available equals on-hand minus reserved. |
| Expiry boundary | Reservation just past expiry and a terminal transition racing expiry. | Exactly one terminal state; reserved balance releases once; no duplicated movement/audit; job context is tenant explicit. |
| High contention reserve | One tenant, one variant, on-hand `10`; **12 concurrent** independent reservations of quantity `1`; repeat **3 times** on fresh variants. | Exactly 10 success + 2 availability failure each run; final on-hand `10`, reserved `10`, active reservations `10`; no oversell. |
| Simultaneous terminal operations | One active reservation, parallel commit and release, then repeat commit/release calls using their original idempotency keys. | One terminal state wins; no negative balance; at most one commit movement; replay has no additional side effect. |
| Frontend conflict recovery | Mock a `409` RFC 7807 response for catalog product update and inventory threshold/adjustment mutation through real API adapter boundaries. | Exactly one visible error/toast path, stale local state is not applied, reload/returned server state remains authoritative; fixture mode is never selected after an API failure. |

## Test implementation order

1. Inspect and extend existing `CatalogServiceTests`, `InventoryServiceTests`, media tests, `PolicyEndpointTests`, and frontend API/client tests. Keep shared fixture setup deterministic; generate fresh tenant/slug/SKU/idempotency values per case.
2. Add missing two-tenant API and private-media HTTP coverage. Use a test authentication/verified-tenant factory, not untrusted headers or direct controller calls, so policy and middleware order are exercised.
3. Add the three-run, twelve-worker reservation campaign. Each worker must use its own DbContext and tenant scope. Capture all results before assertions; never assert task scheduling order.
4. Add terminal-operation and expiry races. Assert the persisted reservation state, movement count, audit records, and reconciled balance after new verification contexts load the rows.
5. Add frontend adapter/component coverage for a machine-readable `409` problem response and explicit fixture-vs-real selection. Do not introduce a second error system or the deferred M03-S07 localization architecture.
6. Run all quality gates and create `artifacts/checkpoints/M04-S06.md` with the matrix populated from actual results. Update the M04 step status to `REVIEW`; do not start Milestone 05.

## Acceptance conditions

- Every matrix row has passing reproducible evidence from real PostgreSQL where persistence/concurrency is relevant.
- No test observes a cross-tenant catalog, inventory, reservation, audit, or media record.
- The high-contention campaign never grants more than the available quantity across all three repetitions.
- Reconciliation holds after every lifecycle/race case.
- Generated OpenAPI has no unexplained drift; backend and frontend checks pass; container/image cleanup is documented.
- The checkpoint includes the completed invariant matrix, manual reviewer procedure, known limitations, and an explicit statement that Milestone 05 was not started.

## Approval boundary

Approve this plan before implementation. On completion, M04-S06 remains `REVIEW` until the project owner approves the invariant matrix and the Milestone 04 exit gate.
