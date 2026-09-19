# Milestone 06 Exit Gate Checkpoint — Order Operations, Manual Payments, Fulfilment, and Notifications

## Identification

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** M06 Exit Gate — Milestone Completion Review
- **Date:** 2026-09-20
- **Branch / Head:** `master` at commit `985c6e3` (`Merge pull request #91 from Kreyora/feat/master/m06-s06`)
- **Status:** `REVIEW`

## Scope Completed

Consolidated exit-gate review for Milestone 06. All six milestone implementation steps (M06-S01 through M06-S06) are approved. This checkpoint establishes factual evidence that all six exit criteria are satisfied through automated integration, domain, and contract tests under real PostgreSQL Testcontainers, and clean frontend CI gates. No new product code, tests, migrations, or dependencies were added.

## Source Revision and Diff Hygiene

- `git log -n 1 --oneline`: `985c6e3 Merge pull request #91 from Kreyora/feat/master/m06-s06`
- `git diff --check`: Passed (clean diff).
- `git status --short`: No executable files changed; changes strictly confined to approval and checkpoint documentation.

## Exit Criteria and Evidence Consolidation

| # | Milestone Exit Criterion | Automated Evidence | Invariant / Domain Enforcement | Status |
|---|---|---|---|---|
| 1 | Authorized sellers can process, cancel, verify, and fulfil orders safely | `OrderOperationServiceTests` (8/8), `SellerOrderWorkspaceIntegrationTests` (8/8), `Milestone06LifecycleAndFailureTests` (Scenarios 1, 2, 4, 5) | Domain `OrderTransitionPolicy` evaluates allowed actions based on current order, payment, and fulfilment states. RBAC policies enforce `OrdersWrite` for general operations and `PaymentsManage` for financial verifications. PostgreSQL `xmin` concurrency token prevents lost updates (`409 Conflict`). | **Satisfied** |
| 2 | COD and merchant QR follow documented manual operating policies | `PaymentServiceTests` (12/12), `Milestone06LifecycleAndFailureTests` (Scenarios 1, 2, 3) | COD remains payment `Pending` throughout preparation/dispatch; payment collection requires dispatched or delivered status. Merchant QR orders require proof upload and authorized verification before dispatch can occur. Proof uploads validate JPEG/PNG/WebP/PDF magic bytes. | **Satisfied** |
| 3 | No client input alone can mark payment paid | `OrderTransitionPolicyTests` (45/45), `PaymentAttemptTests` (25/25), `Milestone06LifecycleAndFailureTests` (Scenarios 2, 6, 8) | Status cannot be set by client payload. Only authorized execution of `VerifyPayment` (with valid merchant QR proof) or `MarkCodCollected` (for dispatched COD) can transition payment status to `Paid`. Both actions strictly require `PaymentsManage` role (Owner/Admin only; Operator denied with 403 Forbidden). | **Satisfied** |
| 4 | Inventory reconciles across confirmation, cancellation, failure, and fulfilment | `OrderFulfilmentInventoryCoordinationTests` (11/11), `Milestone06LifecycleAndFailureTests` (Scenarios 1, 3, 5) | On checkout, stock is committed (`OnHand - Q`, `Reserved - Q`). On cancellation, atomic `StockMovementType.OrderRestock` restores stock (`OnHand + Q`, `Available + Q`) with row-level locks (`SELECT ... FOR UPDATE`). Stock ledger reconciles at every terminal path: `OnHand - Reserved == Available`. Racing Cancel vs Dispatch resolves with exactly one winner and zero stock leakage. | **Satisfied** |
| 5 | Every transition has actor/reason/time/correlation evidence | `OrderOperationServiceTests`, `OrderAggregateTests`, `Milestone06LifecycleAndFailureTests` (Scenarios 1, 2, 3, 5, 7) | Every state transition logs an immutable `IAuditEventService` record (`order.confirm`, `order.prepare`, `order.dispatch`, `order.deliver`, `order.cancel`, `order.markcodcollected`, `payment.verified`, `payment.rejected`). Destructive actions (`Cancel`, `RejectPayment`, `MarkDeliveryFailed`) mandate non-whitespace reason between 3 and 500 characters. | **Satisfied** |
| 6 | Notifications are durable, retryable, redacted, and visible without using a live provider | `NotificationOutboxLifecycleTests` (10/10), `NotificationRequestTests` (15/15), `Milestone06LifecycleAndFailureTests` (Scenario 7) | Transitions emit transactional `OutboxMessage` events. Hangfire `OutboxNotificationProcessorJob` creates `NotificationRequest`s. `NotificationDeliveryJob` executes bounded retries (3 max) with exponential backoff (1m, 5m, 15m), transitioning exhausted attempts to `DeadLettered`. `DevelopmentNotificationProvider` safely logs rendered templates without external network calls. Customer PII (`+97798****0010`, `c***@example.com`) is redacted throughout logs and APIs. Operators can authorize manual replay. | **Satisfied** |

## Step-by-Step Evidence Summary (S01 through S06)

- **M06-S01 (State-Transition Policies and Action Authorization):**
  - Implemented `OrderTransitionPolicy` defining explicit matrices for `OrderStatus`, `PaymentStatus`, and `FulfilmentStatus`.
  - Added transition methods to `Order` aggregate with invariant guardrails.
  - Implemented `OrderOperationService` with RBAC (`OrdersWrite`, `PaymentsManage`), `xmin` concurrency checks, `OrderCommand` idempotency, and audit event emission.
  - Verified with 45 unit tests and 8 PostgreSQL Testcontainers integration tests.
- **M06-S02 (COD and Merchant-QR Payment Domain):**
  - Implemented `PaymentAttempt` and `PaymentProof` aggregates with status lifecycles (`AwaitingProof` -> `ProofSubmitted` -> `Verified` / `Rejected`).
  - Implemented `PaymentService` with magic-byte file validation (`FF D8 FF` for JPEG, `89 50 4E 47` for PNG, `RIFF...WEBP`, `%PDF`) and private S3-compatible storage.
  - Implemented `StorePaymentConfiguration` for merchant QR setup (account details, QR asset).
  - Generated EF migration `AddPaymentDomain` (0 pending model changes).
  - Verified with 25 unit tests and 12 PostgreSQL integration tests.
- **M06-S03 (Inventory Allocation, Cancellation, and Fulfilment Coordination):**
  - Implemented `StockMovementType.OrderRestock` and `RestockForOrderAsync` in `InventoryService` with row-level locks.
  - Updated `OrderOperationService` to atomically restock inventory and expire unverified payment attempts upon order cancellation.
  - Emitted transactional `OutboxMessage` events across all order and payment transitions.
  - Verified with 11 PostgreSQL integration tests in `OrderFulfilmentInventoryCoordinationTests`.
- **M06-S04 (Notification Outbox and Development Provider):**
  - Implemented `NotificationRequest`, `NotificationDeliveryAttempt`, and `NotificationDeliveryLog` aggregates.
  - Implemented `NotificationTemplateRegistry` supporting 8 commerce events with dynamic token rendering.
  - Implemented `DevelopmentNotificationProvider` safe dev sink.
  - Implemented Hangfire jobs: `OutboxNotificationProcessorJob` and `NotificationDeliveryJob` with exponential backoff and DLQ.
  - Implemented `PiiRedaction` utility for masking customer contacts.
  - Generated EF migration `AddNotificationOutboxAndTables` (0 pending model changes).
  - Verified with 33 unit tests and 10 PostgreSQL integration tests.
- **M06-S05 (Seller Order Workspace Integration):**
  - Implemented `IOrderQueryService` and exposed `/v1/orders` endpoints (list, detail, allowed actions, execute action, activity, notifications).
  - Implemented real typed `apiOrderClient` and `apiPaymentClient` in `apps/web`.
  - Integrated Seller Orders List (`/orders`) with status/payment/channel filters, search, and pagination.
  - Integrated Seller Order Detail (`/orders/[id]`) with live server-allowed action panel, reason capture modal, QR proof inspection modal with direct verify/reject, 409 concurrency conflict alert & refresh CTA, real notification delivery tracking, and activity timeline.
  - Verified with 8 PostgreSQL integration tests, 454 frontend tests (0 lint errors, build clean).
- **M06-S06 (End-to-End Lifecycle and Failure Verification):**
  - Implemented `Milestone06LifecycleAndFailureTests` covering all 9 end-to-end scenarios: COD lifecycle, merchant-QR lifecycle, proof rejection with automated restock, optimistic concurrency (409 Conflict), concurrent terminal collision (cancel vs dispatch), idempotency replay and key conflicts, notification retries, DLQ and manual replay, multi-tenant isolation, and immutable order snapshots.
  - Verified all 9 scenarios pass under real PostgreSQL Testcontainers.

## Quality Gates Summary

| Quality Gate | Command | Result |
|---|---|---|
| Backend Solution Tests | `dotnet test services/api/Kreyora.slnx --configuration Release` | **PASS** (351 passed: 203 unit, 137 integration, 6 arch, 5 contract; 0 failed) |
| EF Core Pending Migrations | `dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --no-build` | **PASS** (0 pending changes) |
| Frontend CI Gate | `pnpm ci:frontend` | **PASS** (0 lint errors, 0 type errors, 454 vitest passed across 29 test files, Next.js 16 build passed: 35 routes) |
| Git Hygiene Check | `git diff --check` | **PASS** (clean) |

## Known Issues and Risks

| Severity | Issue | Owner | Required Action |
|---|---|---|---|
| None | All 6 exit criteria satisfied; all regression suites green. | Antigravity | None |

## Reviewer Procedure

1. Run the full backend quality gate:
   ```bash
   dotnet test services/api/Kreyora.slnx --configuration Release
   ```
   Confirm all 351 tests pass.
2. Verify EF Core model synchronization:
   ```bash
   dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --no-build
   ```
   Confirm "No changes have been made to the model since the last migration."
3. Run the full frontend CI gate:
   ```bash
   pnpm ci:frontend
   ```
   Confirm 0 errors and successful Next.js 16 production build.
4. Verify Git hygiene:
   ```bash
   git diff --check
   ```
   Confirm clean output.

## Approval

- **Reviewer:** Project Owner
- **Decision:** Pending Review
- **Notes:** All six Milestone 06 implementation steps and exit criteria verified.
- **Next allowed prompt:** Milestone 07 Step 01 (`docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`) upon approval of this exit gate.

Milestone 07 was not started as part of this exit gate.

