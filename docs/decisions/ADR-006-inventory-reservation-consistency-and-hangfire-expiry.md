# ADR-006 — Inventory reservation consistency and Hangfire expiry

- **Status:** Accepted
- **Date:** 2026-09-04
- **Owner:** Project owner
- **Reviewers:** Codex
- **Affected milestones:** M04-S03 and later order/checkout work

## Context

The inventory module needs temporary holds without overselling while M04 intentionally precedes checkout and orders. The baseline architecture in `docs/plan/plan.md` §§10.4–10.5 requires atomic stock allocation with tenant-scoped background jobs, and the repository's MVP runtime is Hangfire with PostgreSQL storage. M04-S02 has already established a tenant-scoped `InventoryItem` balance and append-only on-hand ledger.

## Decision

An active reservation increases `InventoryItem.ReservedQuantity` but does not alter on-hand stock. Commit atomically decreases both reserved and on-hand quantities and appends one immutable `ReservationCommitted` stock movement. Release and expiry atomically decrease only reserved quantity.

Every reservation lifecycle operation uses a PostgreSQL serializable transaction, locks the exact tenant-scoped inventory row with `SELECT … FOR UPDATE`, and uses an append-only idempotency command record. The reservation expiry process is a Hangfire recurring job backed by PostgreSQL; it enters every tenant through `ITenantJobRunner` and uses the same locked lifecycle service. No separate `BackgroundService` scheduler is introduced.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| PostgreSQL lock plus serializable transaction and idempotency record | Clear single-row availability boundary; safe retry/replay; aligns with existing balance aggregate | Contended variants serialize briefly | Chosen. |
| Optimistic `xmin` only | Fewer explicit locks | Higher retry complexity and difficult expiry/commit race reasoning | Rejected for allocation paths. |
| Store reservation state without a materialized reserved balance | Simpler write model | Availability requires aggregate scans and becomes more error-prone under contention | Rejected. |
| `BackgroundService` polling loop | Minimal dependencies | Conflicts with locked Hangfire MVP runtime and lacks shared operational controls | Rejected. |

## Consequences

- **Product impact:** Temporary stock holds expire safely; a seller or future checkout cannot sell the same last unit twice.
- **Architecture impact:** Inventory owns reservation state, balance mutation, and commit movement; later Orders and Storefront use the inventory service rather than repositories.
- **Security/privacy impact:** All reservation records, job envelopes, queries, and audits carry tenant context; worker execution never trusts a request header. Audit attribution records the initiating actor and marks automated expiry.
- **Cost/operations impact:** Hangfire and PostgreSQL storage become an active local/runtime dependency in this step; the recurring job has bounded batches and observable failures.
- **Migration or rollback impact:** Adds tables/columns/indexes only. Existing M04-S02 movement rows retain null reservation references. Application rollback is safe only after pausing the recurring job; data migration is forward-fixed.

## Validation evidence

- PostgreSQL integration tests demonstrate competing holds do not oversell, release/expiry restore capacity, commit writes exactly one negative movement, and tenant/job boundaries hold.
- Duplicate commands and overlapping job runs do not duplicate side effects.
- Reconciliation verifies ledger-to-on-hand and active-reservation-to-reserved equations.

## Supersession conditions

Revisit if allocation becomes multi-warehouse, if measured contention requires a dedicated inventory worker/service, or if a different durable scheduler is approved through a replacement ADR.
