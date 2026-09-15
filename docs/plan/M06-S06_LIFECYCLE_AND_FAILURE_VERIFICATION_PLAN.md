# Kreyora — Milestone 06 Step 06 Plan
# End-to-End Lifecycle and Failure Verification

## Goal

Execute complete COD and merchant-QR order scenarios from checkout through cancellation or delivery. Verify duplicate action idempotency, stale version concurrency rejection (`409 Conflict`), concurrent cancel/dispatch collisions, rejected payment proof, partial infrastructure failure with bounded notification retries and DLQ dead-lettering, manual replay authorization, cross-tenant isolation across all endpoints, and immutable order financial snapshots. Produce a definitive lifecycle evidence table and fix any milestone-scoped defects discovered during verification.

---

## Gate, Boundaries, and Scope

- **Gate Status:** M06-S05 is `APPROVED`. M06-S06 is a **verification and milestone defect-fix campaign**, not an unconstrained new feature phase.
- **Allowed Scope:**
  1. Create a dedicated real PostgreSQL Testcontainers integration test suite in `services/api/tests/Kreyora.IntegrationTests/Orders/Milestone06LifecycleAndFailureTests.cs`.
  2. Implement comprehensive end-to-end lifecycle and failure scenarios:
     - **Scenario 1:** Complete COD order lifecycle (Checkout -> Confirm -> Prepare -> Dispatch -> Deliver + COD Collection) with stock ledger commitment and reconciled balances.
     - **Scenario 2:** Complete Merchant-QR order lifecycle with customer proof upload (`image/jpeg` magic bytes), review, `VerifyPayment`, and delivery.
     - **Scenario 3:** Merchant-QR rejected proof (`RejectPayment` with reason) and order cancellation (`Cancel` with reason) with automated inventory restock (`StockMovementType.OrderRestock`).
     - **Scenario 4:** Stale version optimistic concurrency rejection (HTTP `409 Conflict`) and recovery refresh.
     - **Scenario 5:** Concurrent conflicting terminal operations (simultaneous Cancel vs. Dispatch collision) ensuring exactly one winner and zero ledger inconsistency.
     - **Scenario 6:** Idempotent command replay with identical `Idempotency-Key` returning cached result (`WasReplayed: true`) without duplicate events, plus conflict on key reuse with different payload.
     - **Scenario 7:** Notification delivery failures, backoff schedule, bounded retries (3 attempts), transition to `DeadLettered`, and manual replay authorization.
     - **Scenario 8:** Cross-tenant isolation across orders, items, payment proofs, activity timelines, notifications, and actions.
     - **Scenario 9:** Immutable order snapshots verifying that subsequent catalog title/price changes and delivery rule edits do not mutate placed orders.
  3. Produce a complete **Lifecycle Evidence Table** mapping each transition, actor, reason, state, stock movement, outbox message, and audit log.
  4. Fix any milestone-scoped defects discovered during test execution.
  5. Run and verify all repository quality gates:
     - `dotnet test services/api/Kreyora.slnx --configuration Release`
     - `pnpm ci:frontend`
     - `dotnet ef migrations has-pending-model-changes`
     - `git diff --check`
  6. Generate checkpoint report `artifacts/checkpoints/M06-S06.md` with status `REVIEW`.

- **Prohibited Scope:**
  - Starting Milestone 07 (Constrained AI assistant and seller knowledge) or any exit gate before Step 06 approval.
  - Adding fake or live external payment gateways (eSewa, Khalti) or live SMS/WhatsApp/email providers (development notification provider is the approved invariant).
  - Modifying the PostgreSQL database schema unless a verification-discovered bug strictly requires it.
  - Relaxing tenant isolation, RBAC policies, or business invariant checks.
  - Modifying accepted ADRs without authorization.

---

## Scenarios & Invariant Verification Matrix

| Scenario | Actions & Events | Invariant & Evidence Required |
|---|---|---|
| **1. Complete COD Lifecycle** | Storefront Checkout -> PendingConfirmation / Pending / Unfulfilled -> Operator Confirm -> Processing -> Prepare -> Ready -> Dispatch -> Dispatched -> Deliver + MarkCodCollected -> Delivered / Paid / Fulfilled | Stock committed (on hand decreased, reserved cleared, available = on hand - reserved). Chronological audit trail records each actor and timestamp. Outbox notifications created and delivered with redacted contacts. |
| **2. Complete Merchant-QR Lifecycle** | Storefront Checkout -> PendingConfirmation / AwaitingVerification / Unfulfilled -> Proof Upload (JPEG magic bytes) -> Operator VerifyPayment -> Paid -> Confirm -> Prepare -> Dispatch -> Deliver -> Fulfilled | Proof validated and linked to payment attempt. Payment state cannot become paid without authorized verification. All order events emit outbox notifications. |
| **3. Rejected Proof & Order Restock** | Merchant QR order placed -> Proof uploaded -> Operator RejectPayment (with reason) -> Failed -> Operator Cancel (with reason) -> Cancelled | Automatic restock via `InventoryService.RestockForOrderAsync` restoring on hand and available balances. Payment attempt marked expired/cancelled. Audit log records actor and reason. |
| **4. Stale Version Concurrency (409)** | Operator A reads Version $V_1$, Operator B reads Version $V_1$. Operator A confirms (order moves to $V_2$). Operator B attempts action with $V_1$. | Operator B request rejected with HTTP `409 Conflict` (RFC 7807 ProblemDetails). Database state remains consistent at $V_2$. Operator B reloads latest state before retrying. |
| **5. Concurrent Terminal Collision** | Parallel execution of Cancel and Dispatch on the same order from two independent workers/contexts. | Exactly one terminal operation succeeds; the other fails with concurrency conflict (`409 Conflict`). Ledger reconciles with zero duplicate movements or orphaned commitments. |
| **6. Idempotency & Key Reuse** | 1. Execute `Confirm` with `Idempotency-Key: K1`.<br>2. Replay identical `Confirm` with `Idempotency-Key: K1`.<br>3. Send `Cancel` with same `Idempotency-Key: K1`. | Replay returns identical result with `WasReplayed: true`. No duplicate audit event or notification. Differing payload with same key is rejected with conflict/validation error. |
| **7. Notification Retries & DLQ** | Notification created -> Delivery attempts fail (simulated provider exception) -> 30s backoff -> Clock advances -> Retry 2 -> Clock advances -> Retry 3 -> Max retries reached -> DeadLettered. Manual replay authorized. | Bounded retries strictly respected; skips delivery when `NextRetryAt > Now`; transitions to `DeadLettered` with error detail; contact info redacted; manual replay resets to `Pending` for redelivery. |
| **8. Multi-Tenant Isolation** | Tenant A creates orders, payments, proofs, notifications. Tenant B attempts list, detail query, activity query, notification query, action execution, and proof retrieval. | Every Tenant B request returns HTTP `404 Not Found` (or `403 Forbidden`). Zero cross-tenant leakage of IDs, customer details, or status. |
| **9. Immutable Order Snapshots** | Order placed at price $P_1$, fee $F_1$. Seller edits catalog price to $P_2$, product title, and delivery rule fee to $F_2$. | Order query returns original $P_1$, $F_1$, and historical title. Placed orders are immutable snapshots unaffected by subsequent catalog/delivery modifications. |

---

## Test Execution Order

1. **Test Setup & Shared Fixtures:**
   - Use `PostgresFixture` with real PostgreSQL Testcontainers.
   - Set up fresh tenant scopes per test using `TenantContextAccessor`.
   - Use `MutableTimeProvider` to test time-dependent backoff and retry gates deterministically.
2. **Implement Integration Tests:**
   - Create `services/api/tests/Kreyora.IntegrationTests/Orders/Milestone06LifecycleAndFailureTests.cs`.
   - Add test methods for each of the 9 scenarios.
3. **Run and Verify Tests:**
   - Run focused test suite:
     ```bash
     dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~Milestone06LifecycleAndFailureTests"
     ```
   - Fix any defects discovered during execution.
4. **Run Full Quality Gates:**
   - Backend:
     ```bash
     dotnet test services/api/Kreyora.slnx --configuration Release
     dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
     ```
   - Frontend:
     ```bash
     pnpm ci:frontend
     ```
   - Git hygiene:
     ```bash
     git diff --check
     ```
5. **Compile Lifecycle Evidence Table & Checkpoint:**
   - Create `artifacts/checkpoints/M06-S06.md` with status `REVIEW`.
   - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`.
   - Stop and wait for human review.

---

## Acceptance Criteria

- All 9 verification scenarios implemented and passing against real PostgreSQL Testcontainers.
- Zero cross-tenant data leakage observed.
- Stock ledger perfectly reconciled in all terminal paths (delivery, cancellation, failed delivery).
- Optimistic concurrency conflict returns HTTP 409 and prevents inconsistent database states.
- Bounded retries and dead-lettering verified with deterministic time provider.
- All backend (342+) and frontend (454) tests pass with 0 errors and 0 warnings.
- Next implementation prompt (Milestone 07) is NOT started.

