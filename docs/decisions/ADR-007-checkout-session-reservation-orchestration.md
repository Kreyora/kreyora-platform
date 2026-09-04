# ADR-007 — Checkout-session reservation orchestration

- **Status:** Accepted
- **Date:** 2026-09-05
- **Owner:** Project owner
- **Affected milestones:** M05-S03 and later checkout/order work

## Decision

An internal tenant-scoped checkout session may create a batch of `Checkout` inventory reservations without an ASP.NET Identity user actor. Such reservations retain a nullable initiator user identifier; authenticated seller/manual reservations continue to record their user actor. The checkout-session application orchestrator owns the single PostgreSQL serializable transaction that revalidates a quote, allocates every inventory line through an Inventory contract, persists session snapshots/idempotency, and commits once.

The session and its reservations share the same expiry. Hangfire expires sessions through tenant job context and the Inventory module’s idempotent transition rules. Generic inventory expiry remains safe to race with session expiry.

## Consequences

- Guest checkout does not fabricate a seller actor ID or bypass tenant context.
- Multi-line allocation is all-or-nothing and follows ADR-006 row-lock/retry guarantees.
- Inventory remains the owner of balance mutation and reservation state; Storefront does not access inventory repositories.
- Public endpoint exposure, rate limiting, payment selection, and order creation remain deferred.
