# Handoff: Milestone 06 Step 06 — End-to-End Lifecycle and Failure Verification

## 1. Overview

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** 06 — End-to-end lifecycle and failure verification
- **Phase:** Phase 2 (Builder) Implementation — Completed
- **Governing Plan:** `docs/plan/M06-S06_LIFECYCLE_AND_FAILURE_VERIFICATION_PLAN.md`
- **Active Milestone File:** `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`
- **Prior Checkpoint:** `artifacts/checkpoints/M06-S05.md` (APPROVED)
- **Current Checkpoint:** `artifacts/checkpoints/M06-S06.md` (REVIEW)
- **Status:** `REVIEW`

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Test Suite Scaffolding (`services/api/tests/Kreyora.IntegrationTests`)**
  - Create `services/api/tests/Kreyora.IntegrationTests/Orders/Milestone06LifecycleAndFailureTests.cs` using `PostgresFixture`.
  - Wire helpers for seed store, stock ledger, checkout sessions, orders, payment configurations, and mutable clock.

- [x] **Task 2: Scenario 1 — Complete COD Order Lifecycle**
  - Checkout -> PendingConfirmation -> Confirm -> Prepare -> Dispatch -> Deliver + MarkCodCollected.
  - Verify stock ledger commitment, reconciled balances (`OnHand - Reserved == Available`), outbox notifications, and actor/reason audit trail.

- [x] **Task 3: Scenario 2 — Complete Merchant-QR Order Lifecycle with Proof Verification**
  - Checkout -> AwaitingVerification -> Upload JPEG proof -> VerifyPayment -> Confirm -> Dispatch -> Deliver.
  - Verify proof upload validation (magic bytes), payment attempt status transition to `Verified`, order transition to `Paid`, and notifications.

- [x] **Task 4: Scenario 3 — Merchant-QR Rejected Proof & Order Cancellation with Restock**
  - Order placed -> Customer uploads proof -> Operator rejects proof with reason -> Operator cancels order with reason.
  - Verify automated restock (`StockMovementType.OrderRestock`), inventory balances fully restored, payment attempt expired, and cancellation audit record.

- [x] **Task 5: Scenario 4 — Stale Version Concurrency Rejection (409 Conflict)**
  - Operator A and Operator B read Version $V_1$.
  - Operator A executes action -> Version advances to $V_2$.
  - Operator B executes action with expectedVersion $V_1$ -> Rejected with HTTP `409 Conflict`.
  - Verify database integrity and refresh behavior.

- [x] **Task 6: Scenario 5 — Concurrent Terminal Collision (Cancel vs Dispatch)**
  - Parallel execution of conflicting terminal actions on same order.
  - Verify exactly one operation wins; second receives concurrency conflict.
  - Verify stock ledger consistency (no double restock or orphaned reservation).

- [x] **Task 7: Scenario 6 — Idempotency & Key Reuse Conflicts**
  - Replay identical action with same `Idempotency-Key` -> Returns cached result (`WasReplayed: true`), 0 duplicate side-effects.
  - Replay different action with same `Idempotency-Key` -> Rejected with conflict/validation error.

- [x] **Task 8: Scenario 7 — Notification Delivery Retries, DLQ & Manual Replay**
  - Deliver with failing provider -> Attempt 1 recorded with failure reason -> 30s backoff -> Verify retry skipped when `NextRetryAt > Now`.
  - Advance clock -> Attempt 2 fails -> Advance clock -> Attempt 3 fails -> Transition to `DeadLettered` with dead-letter timestamp.
  - Authorize manual replay -> Reset status to `Pending` -> Delivery succeeds.
  - Verify PII redaction on recipient contact info throughout logs.

- [x] **Task 9: Scenario 8 — Multi-Tenant Isolation Verification**
  - Tenant A creates orders, payments, proofs, notifications.
  - Tenant B attempts read, action execution, and proof download -> All return `404 Not Found` or `403 Forbidden`.
  - Verify zero data leakage across tenant boundaries.

- [x] **Task 10: Scenario 9 — Immutable Order Snapshot Invariant**
  - Order placed at price $P_1$, delivery fee $F_1$.
  - Seller updates catalog price to $P_2$, product title, and delivery rule fee to $F_2$.
  - Verify order detail retains original $P_1$, $F_1$, and historical line item titles.

- [x] **Task 11: Defect Remediation & Full Quality Gates**
  - Fix any milestone-scoped defects discovered during test execution.
  - Run full backend solution test suite: `dotnet test services/api/Kreyora.slnx --configuration Release` (351/351 passed).
  - Run EF Core model changes check: `dotnet ef migrations has-pending-model-changes ...` (0 pending).
  - Run full frontend CI gate: `pnpm ci:frontend` (0 lint errors, 0 type errors, 454/454 passed, build clean).
  - Run git diff check: `git diff --check` (clean).

- [x] **Task 12: Checkpoint & Documentation**
  - Compile Lifecycle Evidence Table in checkpoint report `artifacts/checkpoints/M06-S06.md` (`REVIEW`).
  - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`.
  - Wait for project owner review and approval.
