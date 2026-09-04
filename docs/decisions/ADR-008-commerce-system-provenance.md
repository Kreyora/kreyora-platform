# ADR-008 — Commerce-system provenance

- **Status:** Accepted
- **Date:** 2026-09-05
- **Owner:** Project owner
- **Affected milestones:** M05-S04 and later order/payment work

## Context

M05-S03 permits a guest-originated checkout session and its temporary inventory reservations to have no ASP.NET Identity user actor. M05-S04 must commit those reservations and create an auditable order. Existing `StockMovement` and `AuditEvent` records require a user ID, which would force a fabricated seller identity for a customer-originated commerce action.

## Decision

Audit and stock-ledger provenance distinguish an authenticated member from an automated commerce action. `ActorKind` is `Member` for existing authenticated work and `CommerceSystem` for trusted server-side conversion of an active checkout session into an order. `ActorUserId` is required for `Member` and null for `CommerceSystem`; no magic user, service account, or seller identity is invented.

Order creation may emit only minimal non-PII audit/outbox facts with `CommerceSystem` provenance. Existing seller/manual inventory operations retain authenticated member provenance.

## Consequences

- Guest checkout remains attributable without pretending that the seller performed the purchase.
- Seller audit reads can represent a system-origin action without resolving an imaginary user.
- Database and domain checks prevent invalid actor-kind/user combinations.
- This is a forward-only compatibility migration: historical rows become `Member` records with their existing user IDs.

## Alternatives considered

| Alternative | Decision |
|---|---|
| Store a fixed fake seller/system user ID | Rejected: it corrupts audit attribution and can imply membership that never existed. |
| Omit audit and stock movement for guest-created orders | Rejected: violates commerce traceability and inventory reconciliation invariants. |
| Defer guest order creation until customer identity exists | Rejected: breaks approved guest checkout. |
