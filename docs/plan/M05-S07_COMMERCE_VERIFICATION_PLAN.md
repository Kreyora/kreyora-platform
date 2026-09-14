# M05-S07 — Commerce Invariant Verification Plan

## Goal

Prove the M05 public storefront and COD commerce boundary against real PostgreSQL. This is a verification-and-defect-fix step: it adds no new customer, payment, store, or seller capability.

## Verification campaign

| Invariant | Attack or failure scenario | Required proof |
|---|---|---|
| Public storefront isolation | Forged tenant/store fields, foreign variant/session IDs, unknown slug, compound host, forwarded host | Verified host/development-slug context is the only selector; foreign data is unavailable and public `404`s are uniform. |
| Server-owned commerce facts | Browser submits extra prices, line totals, totals, delivery fees, payment state, publication state, tenant/store fields | Only bounded request DTO fields are consumed; quote, session, and order totals/payment come from the server. |
| Idempotent writes | Same key/same payload, same key/changed payload, different order keys for one session | Replay has no new side effect; changed-key reuse conflicts; only one order is created. |
| Storefront cache isolation | Send Store A ETag to Store B | Store B returns its own `200` projection, never Store A data or `304`. |
| Last-unit contention | Two independent public quotes/sessions reserve one unit concurrently | Exactly one session succeeds; no partial reservation, negative balance, or oversell. |
| Order contention | Two public order writes for one active session | Exactly one order and one reservation-commit movement persist. |
| Expiry and job retry | Expired session races order creation; execute the real expiry job twice | Order cannot commit; session/reservation expire once; stock releases once; expiry audit is singular. |
| Snapshot immutability | Alter catalog, variant, publication, and delivery rule after COD order creation | Persisted order/item financial and delivery snapshots remain unchanged. |

## Implementation boundary

- Extend the existing `PublicStorefrontEndpointTests` PostgreSQL/WebApplicationFactory suite so middleware, anonymous routes, rate-limit pipeline, tenant scopes, serializable transactions, inventory locks, and the actual `CheckoutSessionExpiryJob` run together.
- Add frontend API-adapter tests that inspect serialized public checkout bodies. They must contain cart/customer/address intent only; browser-authoritative price, total, delivery, payment, publication, tenant, and store fields are absent.
- Small production fixes are allowed only when these checks expose an M05 defect. No migration or public API expansion is planned by default.

## Required verification and cleanup

1. Run release backend build and the full backend suite.
2. Run the focused PostgreSQL integration suite using `PostgresFixture`/Testcontainers, then the complete integration suite.
3. Run frontend typecheck, unit tests, lint, production build, and the existing public storefront mobile Playwright journey.
4. Inspect Testcontainers before and after tests. Preserve project containers, images, and volumes; remove only disposable helper images created by Testcontainers.
5. Populate the invariant matrix and M05-S07 checkpoint with exact commands, pass counts, persisted-database observations, discovered defects, and manual reviewer steps.

## Payment-scope decision

ADR-009 remains binding: public M05 checkout is COD-only. Merchant QR needs real merchant configuration and payment-proof handling in M06, so it is not an M05-S07 failure condition.
