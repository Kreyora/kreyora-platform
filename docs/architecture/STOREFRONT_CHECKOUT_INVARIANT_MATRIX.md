# Storefront and Checkout Invariant Matrix

**Scope:** Milestone 05, Step M05-S07  
**Authority:** ADR-007, ADR-008, and ADR-009

| Invariant | Verification location | Expected result | Evidence status |
|---|---|---|---|
| Store selection is not caller-controlled | `PublicStorefrontEndpointTests` public route/host tests | Forged tenant/store selectors and foreign IDs cannot select another tenant. | Passed: focused PostgreSQL suite, 6/6. |
| Public errors do not aid enumeration | `PublicStorefrontEndpointTests` missing slug/invalid host cases | Same safe not-found behavior; no internal IDs. | Passed: focused PostgreSQL suite, 6/6. |
| Browser sends intent only | `public-storefront-client.test.ts` | Quote sends lines/destination; order sends checkout session ID; anonymous credentials and idempotency remain required. | Passed: frontend suite, 450/450. |
| Server owns price, fees, status, publication | `PublicCheckout_IgnoresTamperedCommerceFacts_AndPreservesImmutableSnapshots` | Tampered fields do not change quote/session/order facts. | Passed: focused PostgreSQL suite, 6/6. |
| Public write replay is safe | Same public checkout test | Same key replays, changed request conflicts, different order key cannot create a second order. | Passed: focused PostgreSQL suite, 6/6. |
| Cache cannot cross storefronts | `PublicStorefront_SeparatesStorefrontsAcrossEtagHostSlugAndSessionSelectors` | Store A ETag cannot return Store A data or `304` for Store B. | Passed: focused PostgreSQL suite, 6/6. |
| Last unit is never oversold | `PublicCheckout_ReservesTheLastUnitOnce_AndCreatesOnlyOneOrderUnderContention` | One checkout session and one committed order; reconciled inventory balance. | Passed: focused PostgreSQL suite, 6/6. |
| Expiry is safe and repeatable | `CheckoutSessionExpiryJob_IsRetrySafe_AndWinsAgainstAnExpiredOrder` | Expired order conflicts; job retry releases stock and writes one expiry audit event. | Passed: focused PostgreSQL suite, 6/6. |
| Order snapshots are immutable | `PublicCheckout_IgnoresTamperedCommerceFacts_AndPreservesImmutableSnapshots` | Catalog and delivery edits do not mutate the canonical order. | Passed: focused PostgreSQL suite, 6/6. |

The focused campaign passed 6/6 against Testcontainers PostgreSQL. The complete Release integration suite passed 80/80 after the same changes.
