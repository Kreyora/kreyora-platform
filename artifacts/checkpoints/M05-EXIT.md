# Milestone 05 Exit Gate Checkpoint — Storefront, Delivery, Checkout, and Canonical Orders

## Identification

- **Milestone:** 05 — Storefront, Delivery, Checkout, and Canonical Orders
- **Step:** M05 Exit Gate — Milestone Completion Review
- **Date:** 2026-09-14
- **Branch / Head:** `master` at commit `5d45274` (`Merge pull request #78 from Kreyora/feat/master/m05-s07`)
- **Status:** `APPROVED`

## Scope Completed

Consolidated exit-gate review for Milestone 05. All seven milestone implementation steps (M05-S01 through M05-S07) are approved. This checkpoint establishes factual evidence that all six exit criteria are satisfied through existing automated tests and live Docker Compose manual walkthrough. No new product code, tests, migrations, or dependencies were added.

## Source Revision and Diff Hygiene

- `git log -n 1 --oneline`: `5d45274 Merge pull request #78 from Kreyora/feat/master/m05-s07`
- `git diff --check`: Passed (clean diff).
- `git status --short`: No executable files changed since the verified M05-S07 commit; changes are strictly confined to approval and checkpoint documentation.

## Exit Criteria and Evidence Consolidation

| # | Milestone Exit Criterion | Automated Evidence | Live Docker Compose Manual Walkthrough Evidence | Status |
|---|---|---|---|---|
| 1 | A fresh authorized seller configures and publishes a ready platform-subdomain store | `PublicStorefrontEndpointTests` seeds fresh isolated tenant, store, catalog, and delivery per test; unit and contract tests verify administration boundaries. | Fresh seller `Sita Shrestha` (`sita.seller@kreyora.test`) registered workspace `Himalayan Crafts` (`01M2F1R3EPX2ENRHKQ39NMTM3F`). Store created with platform slug `himalayan-crafts`. Observed readiness blockers before configuration (`policies_incomplete`, `catalog_not_ready`, `delivery_not_configured`, `cod_not_configured`). Added catalog product, variant with canonical price (2,500 NPR), stock movement (+10), Kathmandu COD delivery rule (150 NPR), published to storefront. Readiness transitioned to `canActivate=True`, `canAcceptOrders=True`, `blockers=[]`. Store activated to `active`. | **Satisfied** |
| 2 | A customer completes the real COD checkout path | `PublicStorefrontEndpointTests.PublicStorefront_ResolvesByRoute_AndCompletesTheCodCheckoutFlowWithoutInternalIdentifiers` (6/6 focused, 80/80 integration), Playwright mobile journey test (`public-storefront.spec.ts`). | Anonymous customer browsed `himalayan-crafts` catalog, requested delivery quote for 2 units to Thamel Kathmandu (5,000 merchandise + 150 delivery = 5,150 NPR total), created checkout session with stock reservation, and submitted COD order. Order `ORD-01M2F21YAPFYHQ2Q6G05GSFXCB` created (201 Created) with `cashOnDelivery`, payment status `pending`, order status `pendingConfirmation`. Next.js storefront route `/store/himalayan-crafts` served 200 OK. | **Satisfied** |
| 3 | The server recalculates and owns every commerce fact | [Invariant Matrix](../../docs/architecture/STOREFRONT_CHECKOUT_INVARIANT_MATRIX.md) rows 3–4 (`Browser sends intent only` + `Server owns price, fees, status, publication`), `public-storefront-client.test.ts` (450/450). | In the live quote and order requests, client submitted forged payloads (`totalNpr: 1.0`, `deliveryFeeNpr: 0.0`, `paymentStatus: paid`, `paymentMethod: merchantQr`, `tenantId: forged-tenant`). Server discarded all tampered fields and enforced authoritative calculation: 2 × 2,500 + 150 = 5,150 NPR; forced payment status to `pending` and method to `cashOnDelivery`. | **Satisfied** |
| 4 | Order financial, customer, delivery, and item snapshots are immutable | Invariant Matrix row 8 (`Order snapshots are immutable`), `PublicCheckout_IgnoresTamperedCommerceFacts_AndPreservesImmutableSnapshots`. | Database inspection confirmed immutable snapshots in `orders` (`total_npr: 5150.00`, `merchandise_subtotal_npr: 5000.00`, `delivery_fee_npr: 150.00`) and `order_items` (`product_title: Pashmina Shawl`, `variant_name: Classic Red`, `unit_price_npr: 2500.00`, `line_subtotal_npr: 5000.00`). | **Satisfied** |
| 5 | Checkout reservation commit/release/expiry is safe under concurrency and retry | Invariant Matrix rows 6–7 (`Last unit is never oversold` + `Expiry is safe and repeatable`), M04-S06 12-worker PostgreSQL contention campaign. | Stock movement recorded append-only: `Receipt` (+10) and `ReservationCommitted` (-2). Persisted inventory balance verified: `on_hand: 8, reserved: 2`. Idempotent replay of order creation returned HTTP 200 with identical order `ORD-01M2F21YAPFYHQ2Q6G05GSFXCB`. Reusing session idempotency key with modified payload returned safe HTTP 409 Conflict. | **Satisfied** |
| 6 | Public routing cannot expose another tenant or unpublished data | Invariant Matrix rows 1–2 (`Store selection is not caller-controlled` + `Public errors do not aid enumeration`), ADR-009. | Verified `himalayan-crafts.kreyora.local` resolved 200 OK while forged subdomain `forged.kreyora.local` safely returned 404 Not Found. Public profile, quote, session, and order responses hid all internal `tenantId`, `storeId`, and `inventoryReservationId` values. Audit events logged with `CommerceSystem` actor provenance per ADR-008. | **Satisfied** |

## Live Docker Compose Walkthrough Log

- **Environment:** `docker compose up -d --build` (PostgreSQL 16, API, Next.js Web, Mailpit)
- **API Base:** `http://localhost:5001`
- **Web Base:** `http://localhost:3000`
- **Script Executed:** `uv run --with requests python3 scratch/walk_m05_flow.py`

### Step-by-Step Observed Evidence:
1. `GET /v1/auth/csrf`: 200 OK (obtained token & antiforgery cookie).
2. `POST /v1/auth/sign-in`: 204 No Content (authenticated as `sita.seller@kreyora.test`).
3. `GET /v1/workspaces`: 200 OK (resolved tenant `01M2F1R3EPX2ENRHKQ39NMTM3F`, slug `himalayan-crafts`).
4. `PUT /v1/store`: 200 OK (store `01M2F1T3R3ZF6Y6WPTPE7NNKAR`, platform slug `himalayan-crafts`).
5. `GET /v1/store/readiness`: 200 OK (before setup: `canActivate=False`, blockers `policies_incomplete`, `catalog_not_ready`, `delivery_not_configured`, `cod_not_configured`).
6. `POST /v1/catalog/products`: 201 Created (product `01M2F1WMWPVEB2D5NCXXXWWYS6`, variant `01M2F1WMWYYXZKBAMDTYYKCKFK`, price 2,500 NPR).
7. `POST /v1/catalog/products/{id}/publication`: 200 OK (published in catalog).
8. `POST /v1/inventory/adjustments`: 200 OK (Receipt +10 units; on-hand=10, available=10).
9. `POST /v1/store/delivery-rules`: 201 Created (rule `01M2F1WN6H28QM2EGX8YYD0NE9`, flat fee 150 NPR, COD enabled).
10. `PUT /v1/store/publications/{id}`: 200 OK (visibility set to Visible).
11. `GET /v1/store/readiness`: 200 OK (after setup: `canActivate=True`, `canAcceptOrders=True`, `blockers=[]`).
12. `POST /v1/store/activate`: 200 OK (status transitioned to `active`).
13. `GET /public/v1/dev/stores/himalayan-crafts`: 200 OK (`Cache-Control: public, max-age=60`, `ETag` returned; zero internal tenant/store IDs in body).
14. `GET /public/v1/store` with `Host: himalayan-crafts.kreyora.local`: 200 OK (subdomain routing validated).
15. `GET /public/v1/store` with `Host: forged.kreyora.local`: 404 Not Found (safe isolation validated).
16. `GET /public/v1/dev/stores/himalayan-crafts/products`: 200 OK (product `Pashmina Shawl` returned).
17. `POST /public/v1/dev/stores/himalayan-crafts/checkout/quotes`: 200 OK (merchandise 5,000 + delivery 150 = 5,150 NPR total; tampered price/fee/payment fields ignored).
18. `POST /public/v1/dev/stores/himalayan-crafts/checkout/sessions`: 201 Created (session `01M2F21Y91V3ZN32WE9JJR6J4E`, reservation ID hidden).
19. `POST /public/v1/dev/stores/himalayan-crafts/checkout/orders`: 201 Created (order `ORD-01M2F21YAPFYHQ2Q6G05GSFXCB`, COD pending, total 5,150 NPR; tampered fields ignored).
20. `POST /public/v1/dev/stores/himalayan-crafts/checkout/orders` (replay): 200 OK (same order returned).
21. `GET http://localhost:3000/store/himalayan-crafts`: 200 OK on both iPhone mobile and Mac desktop user-agents.
22. Database verification:
    - `orders`: status `PendingConfirmation`, payment `Pending` / `CashOnDelivery`, total `5150.00`.
    - `order_items`: snapshot `Pashmina Shawl` / `Classic Red`, unit price `2500.00`, quantity `2`, subtotal `5000.00`.
    - `stock_movements`: `Receipt` (+10) and `ReservationCommitted` (-2).
    - `audit_events`: `order.created` and `checkout-session.created` with `CommerceSystem` actor provenance.

## Scope Exclusions

The following capabilities are deliberately excluded from Milestone 05 per the project roadmap and ADR-009:
- Merchant-QR payment configuration, QR upload, proof handling, and seller verification (Milestone 06).
- Public order lookup by code/phone (Milestone 06).
- Seller order management UI and state transitions (Milestone 06).
- Custom domains and automated TLS provisioning (Phase 2 Expansion).

## Reviewer Approval

- **Reviewer:** Project owner
- **Decision:** `APPROVED`
- **Notes:** Approved on 2026-09-14. All 6 exit criteria satisfied; Milestone 05 complete.
- **Next Allowed Action:** Milestone 06 planning (Phase 1 Architect) upon explicit user request.

