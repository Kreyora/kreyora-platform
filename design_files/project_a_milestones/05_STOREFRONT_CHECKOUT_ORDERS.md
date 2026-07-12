# Milestone 05 — Storefront, Delivery, Checkout, and Canonical Orders

## Objective

Implement the deterministic commerce path from a seller’s published catalog to a stock-safe COD or merchant-QR order. Replace the public storefront and checkout fixtures with real APIs while keeping the server authoritative for tenant, publication, price, stock, delivery, fees, payment availability, and totals.

## Dependencies

- Milestone 04 exit gate approved.
- A platform-domain/slug convention is recorded for development and pilot environments.
- COD and merchant-QR operating policies are documented sufficiently for order creation; detailed seller processing follows in Milestone 06.

## Implementation design

The MVP has a first-class `Store` but permits one active store per tenant. Tenant-owned catalog and inventory remain shared truth; the store controls publication and configuration. Public requests resolve the store from a trusted host or explicit development slug, never from an arbitrary tenant header.

The browser may hold cart intent, but the API creates an expiring quote/reservation and recalculates every financial fact. Order and order-item snapshots are immutable after confirmation. `OrderStatus`, `PaymentStatus`, and `FulfilmentStatus` remain independent.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Store entity, readiness, settings, and publication | `NOT STARTED` |
| 02 | Delivery rules and server-side quote engine | `NOT STARTED` |
| 03 | Customer/contact, cart intent, and checkout reservation | `NOT STARTED` |
| 04 | Canonical order aggregate and transactional creation | `NOT STARTED` |
| 05 | Public APIs, host/slug resolution, and abuse controls | `NOT STARTED` |
| 06 | Frontend integration and complete customer journey | `NOT STARTED` |
| 07 | Tampering, concurrency, expiry, and isolation verification | `NOT STARTED` |

## Prompt 01 — Store entity, readiness, settings, and publication

> Implement the Storefront administration domain with Store, platform slug, status, controlled brand/theme settings, contact/social links, policy content, product publication scope, delivery/payment readiness placeholders, and one-active-store-per-tenant entitlement rule. Define readiness checks that explain every blocker and prevent public purchasing before catalog, delivery, and payment requirements pass. Implement seller commands/queries, authorization, audit, idempotency, mappings, indexes, migration, and tests. Do not implement public checkout yet.

**Review checkpoint:** approve store model, safe theme boundary, slug rules, readiness output, and publication behavior.

## Prompt 02 — Delivery rules and server-side quote engine

> Implement seller-defined DeliveryRule with zone/area matching, flat or threshold-based fee, optional ETA text, minimum/maximum order constraints if approved, and COD availability. Define a deterministic server-side quote service that accepts product/variant/quantity and normalized destination intent, then reads canonical prices, publication, inventory availability, and delivery rules. Return an expiring quote with line totals, discount placeholder fixed at zero unless explicitly configured later, delivery, tax/fee fields, total, currency NPR, payment-method availability, and a signed or server-stored identity. Add tests for rule priority, unsupported address, boundary totals, unpublished/changed products, insufficient stock, and tenant isolation.

**Review checkpoint:** approve delivery semantics, quote snapshot, expiry, and all server-owned calculations.

## Prompt 03 — Customer/contact, cart intent, and checkout reservation

> Implement the minimal Customers boundary needed for checkout: customer profile/contact, normalized phone/email as approved, delivery address snapshot, consent/retention metadata, and optional anonymous checkout linkage. Implement CheckoutSession that converts cart intent into a short-lived inventory reservation and quote reference. Make create/retry idempotent; prevent duplicate reservations; expire and release safely. Apply public rate limits and privacy-safe logs. Add tests for customer deduplication boundaries, PII redaction, repeated submits, quote expiry, price/stock changes, and abandoned checkout cleanup.

**Review checkpoint:** approve customer data minimization, checkout-session lifecycle, reservation linkage, and abuse limits.

## Prompt 04 — Canonical order aggregate and transactional creation

> Implement Order and OrderItem with source, customer/contact/address snapshot, item identity/version, price snapshot, delivery-rule snapshot, merchandise subtotal, discount, delivery, tax/VAT placeholder, provider/platform fee placeholders, total, currency, notes, timestamps, and independent order/payment/fulfilment states. Transactionally validate the checkout session, commit or associate its reservation, create the immutable order, and initialize COD as payment pending or merchant QR as awaiting verification. Prevent browser-supplied totals/status/tenant identity from becoming authoritative. Add allowed initial-state rules, idempotency, audit/outbox events, migrations, and integration tests.

**Review checkpoint:** approve aggregate/state design, immutable financial snapshots, transaction boundary, and retry behavior.

## Prompt 05 — Public APIs, host/slug resolution, and abuse controls

> Expose cacheable public store/catalog/product reads and non-cacheable quote, checkout-session, and order-creation endpoints. Resolve the store from an allowlisted development path or trusted host-to-store mapping; reject ambiguous, inactive, or unready stores. Add safe product pagination/search, request-size limits, rate limits, anti-automation controls appropriate for MVP, idempotency keys, correlation IDs, privacy-safe errors, and no exposure of internal tenant identifiers. Test host confusion, forged slugs, unpublished data leakage, stale projections, replayed writes, enumeration, and rate behavior.

**Review checkpoint:** approve public API data exposure, host resolution, caching split, and abuse controls.

## Prompt 06 — Frontend integration and complete customer journey

> Replace the Milestone 01 storefront, cart, delivery quote, checkout, and confirmation fixture adapters with generated real API clients while retaining explicit demo mode. Preserve the approved UI and implement real recovery for price change, insufficient stock, expired quote/session, unavailable delivery, duplicate submit, validation error, and transient server failure. The UI may display calculations returned by the server but must not become their authority. Add mobile end-to-end coverage from public store to COD order and merchant-QR-awaiting-verification order.

**Review checkpoint:** approve real customer journey on mobile/desktop and confirm fixture imports are absent outside demo adapters.

## Prompt 07 — Tampering, concurrency, expiry, and isolation verification

> Run the milestone commerce verification campaign. Attempt browser/API tampering with tenant/store IDs, prices, quantities, line totals, delivery fees, payment status, publication state, quote identity, and expired reservations. Run concurrent checkout attempts against the last units of stock, duplicate requests with the same/different idempotency keys, job retries, and cross-tenant host/slug cases. Prove order snapshots remain unchanged after catalog/delivery edits. Fix milestone-scoped defects and produce a storefront/checkout invariant matrix.

**Review checkpoint:** approve the invariant matrix with no unresolved critical/high defect.

## Milestone exit gate

- A fresh authorized seller configures and publishes a ready platform-subdomain store.
- A customer completes real COD and merchant-QR-awaiting-verification checkout paths.
- The server recalculates and owns every commerce fact.
- Order financial, customer, delivery, and item snapshots are immutable.
- Checkout reservation commit/release/expiry is safe under concurrency and retry.
- Public routing cannot expose another tenant or unpublished data.

