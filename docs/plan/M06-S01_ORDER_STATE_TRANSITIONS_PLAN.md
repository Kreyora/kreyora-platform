# M06-S01 — State-Transition Policies and Action Authorization Plan

## 1. Context & Objective

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** 01 — State-transition policies and action authorization
- **Status:** `PLANNING`
- **Objective:** Implement explicit, server-authoritative transition policies for `OrderStatus`, `PaymentStatus`, and `FulfilmentStatus` based on the approved state model. Expose allowed actions with explicit denial reasons for UI consumption, and enforce every action strictly server-side. Enforce role authorization, reason capture where relevant, optimistic concurrency (`xmin`), idempotency (`OrderCommand`), and immutable audit logging. Prevent invalid combinations (e.g. fulfilment after cancellation or unverified manual payment becoming paid). Provide exhaustive table-driven domain and authorization test coverage.

---

## 2. Invariants & Guardrails

1. **Server Owns State Truth:** No client request or payload can directly set state enum values. All mutations happen via authorized semantic actions (e.g. `Confirm`, `Cancel`, `Prepare`, `Dispatch`, `Deliver`, `VerifyPayment`, `RejectPayment`, `MarkCodCollected`).
2. **Three-Dimensional Decoupled State:** `OrderStatus`, `PaymentStatus`, and `FulfilmentStatus` are distinct state machines coordinated through explicit application rules.
3. **No Unverified Payment as Paid:** Manual payments (Merchant QR) cannot become `Paid` without explicit `VerifyPayment` action by an authorized user (`Owner` or `Admin`). COD payments remain `Pending` until explicit `MarkCodCollected` action.
4. **No Fulfilment after Cancellation:** Once an order is `Cancelled`, no further preparation, dispatch, or delivery actions are permitted.
5. **No Cancellation after Delivery:** Once an order is `Delivered` / `Fulfilled`, cancellation is strictly prohibited.
6. **QR Payment Precondition on Dispatch:** Merchant QR orders cannot be dispatched until payment is verified and `Paid`. COD orders may be dispatched while payment is `Pending`.
7. **Role-Based Execution:**
   - Order and Fulfilment actions (`Confirm`, `Cancel`, `Prepare`, `Dispatch`, `Deliver`, `MarkDeliveryFailed`) require `OrdersWrite` (permitted to `Owner`, `Admin`, `Operator`).
   - Payment verification and financial actions (`VerifyPayment`, `RejectPayment`, `MarkCodCollected`) require `PaymentsManage` (permitted strictly to `Owner` and `Admin`).
   - `Viewer` and `PlatformSupport` (read-only support) have NO execution privileges.
8. **Mandatory Reasons:** Destructive actions (`Cancel`, `RejectPayment`, `MarkDeliveryFailed`) strictly require a non-empty reason (3–500 characters).
9. **Optimistic Concurrency:** All mutations require the expected row version (`xmin`). Any concurrent collision returns HTTP 409 Conflict.
10. **Strict Idempotency:** Repeated requests with the identical idempotency key and payload replay previous results safely. Replays with conflicting payloads return HTTP 409 Conflict.
11. **Comprehensive Audit:** Every transition records actor, tenant, timestamp, correlation ID, prior status, new status, and reason in append-only audit events.

---

## 3. State Machine & Transition Matrix

### A. OrderStatus
| Current State | Action | Next State | Preconditions |
|---|---|---|---|
| `PendingConfirmation` | `Confirm` | `Confirmed` | Role has `OrdersWrite` |
| `PendingConfirmation` | `Cancel` | `Cancelled` | Reason provided (3–500 chars) |
| `Confirmed` | `Prepare` | `Processing` | Transitions order to `Processing` when preparation starts |
| `Confirmed` | `Cancel` | `Cancelled` | Reason provided; releases reservations |
| `Processing` | `Deliver` | `Fulfilled` | When fulfilment reaches `Delivered` |
| `Processing` | `Cancel` | `Cancelled` | Allowed only if fulfilment is not `Dispatched` or `Delivered`; reason required |
| `Fulfilled` | *(none)* | *(terminal)* | Cancellation and further mutations strictly denied |
| `Cancelled` | *(none)* | *(terminal)* | All operations strictly denied |

### B. PaymentStatus
| Payment Method | Current State | Action | Next State | Preconditions |
|---|---|---|---|---|
| `MerchantQr` | `AwaitingVerification` | `VerifyPayment` | `Paid` | Role has `PaymentsManage` (Owner/Admin) |
| `MerchantQr` | `AwaitingVerification` | `RejectPayment` | `Failed` | Role has `PaymentsManage`; reason required |
| `MerchantQr` | `Paid` | *(none)* | *(terminal)* | Cannot revert to Pending or AwaitingVerification |
| `CashOnDelivery` | `Pending` | `MarkCodCollected` | `Paid` | Role has `PaymentsManage`; requires Fulfilment `Dispatched` or `Delivered` |
| `CashOnDelivery` | `Pending` | `MarkPaymentFailed` | `Failed` | Delivery failed / uncollectible; reason required |

### C. FulfilmentStatus
| Current State | Action | Next State | Preconditions |
|---|---|---|---|
| `Unfulfilled` | `Prepare` | `Ready` | Order must be `Confirmed` or `Processing` |
| `Unfulfilled` | `Cancel` | `Cancelled` | Triggered when Order is cancelled |
| `Ready` | `Dispatch` | `Dispatched` | Order in `Processing`; for QR: payment must be `Paid` |
| `Ready` | `Cancel` | `Cancelled` | Triggered when Order is cancelled |
| `Dispatched` | `Deliver` | `Delivered` | Order in `Processing` |
| `Dispatched` | `MarkDeliveryFailed` | `Failed` | Reason required |
| `Failed` | `Prepare` | `Ready` | Re-attempt preparation |
| `Failed` | `Cancel` | `Cancelled` | Triggered when Order is cancelled |
| `Delivered` | *(none)* | *(terminal)* | Cannot be cancelled or modified |
| `Cancelled` | *(none)* | *(terminal)* | Cannot be fulfilled |

---

## 4. Role Authorization Matrix

| Action | Required Permission | Owner | Admin | Operator | Viewer | Read-Only Support |
|---|---|:---:|:---:|:---:|:---:|:---:|
| `Confirm` | `OrdersWrite` | Allowed | Allowed | Allowed | Denied | Denied |
| `Cancel` | `OrdersWrite` | Allowed | Allowed | Allowed | Denied | Denied |
| `Prepare` | `OrdersWrite` | Allowed | Allowed | Allowed | Denied | Denied |
| `Dispatch` | `OrdersWrite` | Allowed | Allowed | Allowed | Denied | Denied |
| `Deliver` | `OrdersWrite` | Allowed | Allowed | Allowed | Denied | Denied |
| `MarkDeliveryFailed` | `OrdersWrite` | Allowed | Allowed | Allowed | Denied | Denied |
| `VerifyPayment` | `PaymentsManage` | Allowed | Allowed | Denied | Denied | Denied |
| `RejectPayment` | `PaymentsManage` | Allowed | Allowed | Denied | Denied | Denied |
| `MarkCodCollected` | `PaymentsManage` | Allowed | Allowed | Denied | Denied | Denied |

---

## 5. Denial Reasons Catalog

Standard, structured denial reasons returned when an action is evaluated or attempted:

| Code / Identifier | User-Facing Denial Reason |
|---|---|
| `ORDER_ALREADY_CANCELLED` | "Cancelled orders cannot be modified or fulfilled." |
| `ORDER_ALREADY_FULFILLED` | "Fulfilled orders cannot be cancelled or modified." |
| `ORDER_NOT_PENDING_CONFIRMATION` | "Only orders awaiting confirmation can be confirmed." |
| `ORDER_NOT_CONFIRMED` | "Order must be confirmed before fulfilment can begin." |
| `ORDER_NOT_READY` | "Order fulfilment must be ready before dispatch." |
| `ORDER_NOT_DISPATCHED` | "Order must be dispatched before delivery can be marked." |
| `MERCHANT_QR_PAYMENT_UNVERIFIED` | "Merchant QR orders must be verified and paid before dispatch." |
| `PAYMENT_NOT_AWAITING_VERIFICATION` | "Payment is not awaiting verification." |
| `PAYMENT_METHOD_NOT_MERCHANT_QR` | "Payment verification is only applicable to Merchant QR orders." |
| `PAYMENT_METHOD_NOT_COD` | "COD collection is only applicable to Cash on Delivery orders." |
| `COD_NOT_READY_FOR_COLLECTION` | "COD collection cannot be recorded before the order is dispatched or delivered." |
| `PAYMENT_ALREADY_PAID` | "Payment has already been marked as paid." |
| `DISPATCHED_ORDER_CANNOT_CANCEL_DIRECTLY` | "Dispatched orders cannot be cancelled directly; record delivery failure first." |
| `REASON_REQUIRED` | "A valid reason (between 3 and 500 characters) is required for this action." |
| `ROLE_NOT_AUTHORIZED` | "You do not have permission to perform this action." |

---

## 6. Architecture & Implementation Design

### A. Domain Layer (`Kreyora.Domain.Orders`)
1. **Enums & Action Definitions:**
   - Define `OrderAction` enum (`Confirm`, `Cancel`, `Prepare`, `Dispatch`, `Deliver`, `MarkDeliveryFailed`, `VerifyPayment`, `RejectPayment`, `MarkCodCollected`).
   - Define `OrderActionEvaluation` record: `(OrderAction Action, bool IsAllowed, string? DenialReason, bool RequiresReason, bool IsDestructive)`.
2. **Domain Policy Engine (`OrderTransitionPolicy`):**
   - Pure domain logic with zero external dependencies.
   - Evaluates whether an action is allowed based on `Order` state, payment method, and caller `TenantRole`.
   - Returns exhaustive list of action evaluations with precise denial reasons.
3. **Aggregate Root Methods on `Order`:**
   - Implement encapsulated transition methods on `Order`:
     - `Confirm(string actorId, DateTimeOffset now)`
     - `Cancel(string actorId, string reason, DateTimeOffset now)`
     - `Prepare(string actorId, DateTimeOffset now)`
     - `Dispatch(string actorId, DateTimeOffset now)`
     - `Deliver(string actorId, DateTimeOffset now)`
     - `MarkDeliveryFailed(string actorId, string reason, DateTimeOffset now)`
     - `VerifyPayment(string actorId, DateTimeOffset now)`
     - `RejectPayment(string actorId, string reason, DateTimeOffset now)`
     - `MarkCodCollected(string actorId, DateTimeOffset now)`
   - Each method validates `OrderTransitionPolicy` invariants and throws `InvalidOperationException` if violated.
   - Tracks cancellation reasons and operational audit metadata on the order entity.

### B. Application Layer (`Kreyora.Application.Orders`)
1. **Contracts & DTOs:**
   - `IOrderOperationService`:
     - `Task<Result<IReadOnlyList<OrderActionEvaluation>>> GetAllowedActionsAsync(string orderId, CancellationToken ct = default)`
     - `Task<Result<OrderOperationResult>> ExecuteActionAsync(ExecuteOrderActionRequest request, CancellationToken ct = default)`
   - `ExecuteOrderActionRequest(string OrderId, OrderAction Action, string? Reason, uint ExpectedVersion, string IdempotencyKey)`
   - `OrderOperationResult(string OrderId, string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, FulfilmentStatus FulfilmentStatus, uint RowVersion, bool WasReplayed)`
2. **Infrastructure Implementation (`Kreyora.Infrastructure.Orders.OrderOperationService`):**
   - Resolves current `TenantContext` and demands appropriate permission (`OrdersWrite` or `PaymentsManage`).
   - Applies idempotency via `OrderCommand` table:
     - Operation name: `$"order.{action.ToString().ToLowerInvariant()}"`
     - Computes SHA256 request fingerprint (`OrderId + Action + Reason`).
     - Returns replayed result if duplicate key with identical fingerprint; returns 409 Conflict if fingerprint differs.
   - Sets original `xmin` concurrency token:
     `dbContext.Entry(order).Property<uint>("xmin").OriginalValue = request.ExpectedVersion;`
   - Executes aggregate transition method.
   - Emits structured audit event via `IAuditEventService` (`order.confirmed`, `order.cancelled`, etc.) with prior status, new status, actor, reason, correlation ID.
   - Commits changes within serializable or resilient retry loop.

---

## 7. Exhaustive Verification Plan

### A. Domain Unit Tests (`Kreyora.UnitTests/Domain/OrderTransitionPolicyTests.cs` and `OrderStateTests.cs`)
- **Table-Driven Matrix Tests:**
  - Test every valid combination of (`OrderStatus`, `PaymentStatus`, `FulfilmentStatus`, `OrderPaymentMethod`, `TenantRole`) against all 9 actions.
  - Verify exact `IsAllowed` boolean and `DenialReason` for each action.
  - Verify role boundary enforcement (Owner, Admin, Operator, Viewer, PlatformSupport).
  - Verify mandatory reason validation (null, empty, whitespace, < 3 chars, > 500 chars).
  - Verify `Order` aggregate methods execute valid transitions and reject invalid transitions with identical error messages.

### B. Integration Tests (`Kreyora.IntegrationTests/Orders/OrderOperationServiceTests.cs`)
- Real PostgreSQL + Testcontainers integration tests:
  1. **Full Lifecycle Happy Paths:**
     - COD order: `PendingConfirmation` -> `Confirm` -> `Prepare` -> `Dispatch` -> `Deliver` -> `MarkCodCollected`.
     - QR order: `PendingConfirmation` -> `Confirm` -> `VerifyPayment` -> `Prepare` -> `Dispatch` -> `Deliver`.
  2. **Invariant Violations Prevented:**
     - Attempting `Dispatch` on QR order with `PaymentStatus.AwaitingVerification` returns 400 with denial reason.
     - Attempting `Prepare` on cancelled order returns 400.
     - Attempting `Cancel` on delivered order returns 400.
     - Attempting `VerifyPayment` as `Operator` returns 403 Forbidden.
     - Attempting `Confirm` as `Viewer` returns 403 Forbidden.
  3. **Concurrency & Idempotency:**
     - Stale `ExpectedVersion` (`xmin` mismatch) returns 409 Conflict.
     - Idempotent re-submission returns original result with `WasReplayed = true`.
     - Conflicting payload with reused idempotency key returns 409 Conflict.
  4. **Tenant Isolation:**
     - Requesting order operations across tenants returns 404 Not Found without leaking existence.
  5. **Audit Trail Verification:**
     - Audit events verified in PostgreSQL for each action, confirming actor ID, tenant, prior status, new status, and reason metadata.

---

## 8. Permitted vs Prohibited Scope for M06-S01

### Permitted
- Creation of domain transition policy, action definitions, and denial reasons in `Kreyora.Domain.Orders`.
- Aggregate methods on `Order` for state transitions and invariant validation.
- Application contracts and DTOs in `Kreyora.Application.Orders`.
- Implementation of `OrderOperationService` with idempotency, concurrency, and audit logging in `Kreyora.Infrastructure.Orders`.
- Comprehensive table-driven unit tests in `Kreyora.UnitTests`.
- Real PostgreSQL integration tests in `Kreyora.IntegrationTests`.

### Prohibited
- Modifying Next.js UI or frontend order pages (deferred to S05).
- Implementing merchant QR image proof upload or media asset linking (deferred to S02).
- Implementing Hangfire background stock allocation/reservation commit jobs (deferred to S03).
- Implementing customer notifications or notification outbox (deferred to S04).
- Adding third-party payment gateway integrations (eSewa, Khalti).
- Altering existing passing M05 or M04 test suites.

