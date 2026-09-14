# Handoff: Milestone 06 Step 01 — State-Transition Policies and Action Authorization

## 1. Handoff Overview

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** 01 — State-transition policies and action authorization
- **Phase:** Phase 1 (Architect) Complete -> Awaiting User Approval -> Phase 2 (Builder)
- **Governing Plan:** `docs/plan/M06-S01_ORDER_STATE_TRANSITIONS_PLAN.md`
- **Milestone Reference:** `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`

---

## 2. Allowed vs Prohibited Scope

### Allowed Scope
- Implement explicit, table-driven transition policy engine for `OrderStatus`, `PaymentStatus`, and `FulfilmentStatus` in `Kreyora.Domain.Orders`.
- Expose all 9 domain actions (`Confirm`, `Cancel`, `Prepare`, `Dispatch`, `Deliver`, `MarkDeliveryFailed`, `VerifyPayment`, `RejectPayment`, `MarkCodCollected`) with machine-readable & human-readable denial reasons.
- Add transition methods directly to the `Order` aggregate with strict invariant validation.
- Implement `IOrderOperationService` application contracts and DTOs in `Kreyora.Application.Orders`.
- Implement `OrderOperationService` in `Kreyora.Infrastructure.Orders` enforcing:
  - Role permissions via `ITenantPermissionAuthorizer` (`OrdersWrite` vs `PaymentsManage`).
  - Optimistic concurrency control using PostgreSQL `xmin`.
  - Idempotency using `OrderCommand` entity.
  - Audit logging using `IAuditEventService`.
- Add exhaustive table-driven unit tests in `Kreyora.UnitTests` covering all state and role permutations.
- Add real PostgreSQL integration tests in `Kreyora.IntegrationTests` verifying concurrency, idempotency, isolation, and audit events.

### Prohibited Scope
- Do NOT modify frontend UI or replace frontend mock order fixtures (deferred to S05).
- Do NOT implement merchant QR image/proof upload or media asset handling (deferred to S02).
- Do NOT implement stock allocation/reservation commit background jobs or outbox compensation (deferred to S03).
- Do NOT implement notification outbox or email sending (deferred to S04).
- Do NOT add external live payment gateways (eSewa, Khalti).
- Do NOT alter existing approved M01–M05 behavior or database schema.

---

## 3. Affected Files and Modules

| Layer / Module | File Path | Action | Description |
|---|---|---|---|
| **Domain** | `services/api/src/Kreyora.Domain/Orders/OrderActions.cs` | **NEW** | Enum of 9 order actions, evaluation record, denial reasons |
| **Domain** | `services/api/src/Kreyora.Domain/Orders/OrderTransitionPolicy.cs` | **NEW** | Pure domain transition matrix, precondition checks, denial reason resolution |
| **Domain** | `services/api/src/Kreyora.Domain/Orders/Order.cs` | **MODIFY** | Encapsulated transition methods (`Confirm`, `Cancel`, `Prepare`, `Dispatch`, etc.) |
| **Application** | `services/api/src/Kreyora.Application/Orders/OrderContracts.cs` | **MODIFY** | `IOrderOperationService`, `ExecuteOrderActionRequest`, `OrderOperationResult`, `OrderActionEvaluationResult` |
| **Infrastructure** | `services/api/src/Kreyora.Infrastructure/Orders/OrderOperationService.cs` | **NEW** | Application service implementing permissions, `xmin`, `OrderCommand` idempotency, audit trail |
| **Infrastructure** | `services/api/src/Kreyora.Infrastructure/DependencyInjection.cs` | **MODIFY** | Register `IOrderOperationService` in DI |
| **Unit Tests** | `services/api/tests/Kreyora.UnitTests/Domain/OrderTransitionPolicyTests.cs` | **NEW** | Exhaustive table-driven tests for all 300+ state permutations and denial reasons |
| **Unit Tests** | `services/api/tests/Kreyora.UnitTests/Domain/OrderAggregateTests.cs` | **NEW** | Unit tests for `Order` aggregate mutation methods and mandatory reason validation |
| **Integration Tests** | `services/api/tests/Kreyora.IntegrationTests/Orders/OrderOperationServiceTests.cs` | **NEW** | Real PostgreSQL tests for concurrency, idempotency, roles, cross-tenant isolation, audit |
| **Milestone / Context** | `docs/context/CURRENT_WORK.md` | **MODIFY** | Track M06-S01 progress |
| **Milestone / Context** | `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md` | **MODIFY** | Update step status |

---

## 4. Invariants and Architectural Guarantees

1. **State Invariants:**
   - Cancelled orders can NEVER be prepared, dispatched, or delivered.
   - Fulfilled / Delivered orders can NEVER be cancelled.
   - Dispatched orders cannot be cancelled directly without recording delivery failure first.
   - Merchant QR orders CANNOT be dispatched until payment is `Paid`.
   - COD orders remain payment `Pending` throughout fulfilment; payment is collected (`MarkCodCollected`) upon or after delivery.
   - Manual payment CAN NEVER become `Paid` without authorized `VerifyPayment` action.
2. **Authorization Invariants:**
   - Order & Fulfilment actions (`Confirm`, `Cancel`, `Prepare`, `Dispatch`, `Deliver`, `MarkDeliveryFailed`) require `OrdersWrite` (Owner, Admin, Operator).
   - Financial payment actions (`VerifyPayment`, `RejectPayment`, `MarkCodCollected`) require `PaymentsManage` (Owner and Admin ONLY; Operator is forbidden).
   - Viewers have ZERO write privileges.
3. **Audit & Idempotency Invariants:**
   - Every transition emits an audit event with actor ID, prior state, new state, reason, and correlation ID.
   - Every operation command verifies SHA256 request fingerprint and prevents duplicate side-effects.

---

## 5. Verification Commands and Quality Gates

```bash
# Focused Domain Unit Tests
dotnet test services/api/tests/Kreyora.UnitTests/Kreyora.UnitTests.csproj --filter "FullyQualifiedName~Order"

# Focused Integration Tests (PostgreSQL Testcontainers)
dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --filter "FullyQualifiedName~OrderOperationServiceTests"

# Full Solution Build and Test Suite
dotnet build services/api/Kreyora.slnx --configuration Release --disable-build-servers /m:1
dotnet test services/api/Kreyora.slnx --configuration Release --no-build

# Frontend Safety Check (ensure zero regressions)
pnpm typecheck
pnpm lint
```

---

## 6. Approval Gate

**STOP HERE.** 
Wait for explicit project-owner approval of this plan before beginning Phase 2 (Builder) implementation.

