# Handoff: Milestone 06 Step 05 — Seller Order Workspace Integration

## 1. Overview

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** 05 — Seller order workspace integration
- **Phase:** Phase 2 (Builder) Implementation — Completed (`REVIEW`)
- **Governing Plan:** `docs/plan/M06-S05_SELLER_ORDER_WORKSPACE_PLAN.md`
- **Active Milestone File:** `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`
- **Current Checkpoint:** `artifacts/checkpoints/M06-S05.md` (REVIEW)
- **Prior Checkpoint:** `artifacts/checkpoints/M06-S04.md` (APPROVED)

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Backend Application Contracts (`Kreyora.Application.Orders`)**
  - Add `IOrderQueryService` interface
  - Add query models: `OrderQuery`, `OrderSummaryItem`, `OrderDetailItem`, `OrderItemDetail`, `OrderActivityItem`, `OrderNotificationItem`

- [x] **Task 2: Backend Infrastructure Query Service (`Kreyora.Infrastructure.Orders`)**
  - Implement `OrderQueryService` implementing `IOrderQueryService`
  - Tenant-scoped order queries with filters (`status`, `paymentStatus`, `fulfilmentStatus`, `source`, `search`, pagination)
  - Detail mapping including items, delivery addresses, financial breakdown, payment attempts, and row version
  - Audit event mapping for order activity timeline
  - Notification mapping for order outbox events
  - Register `IOrderQueryService` in `DependencyInjection.cs`

- [x] **Task 3: Backend WebApi Controller (`Kreyora.WebApi.Controllers.OrdersController`)**
  - Implement `OrdersController` with `[RequireTenantContext]`, `[ApiVersion("1.0")]`
  - `GET /v1/orders`: List orders with pagination & filters (`TenantPermissions.OrdersRead`)
  - `GET /v1/orders/{id}`: Order details with items and payment attempts (`TenantPermissions.OrdersRead`)
  - `GET /v1/orders/{id}/actions`: Allowed action evaluations (`TenantPermissions.OrdersRead`)
  - `POST /v1/orders/{id}/actions`: Execute order action with concurrency version check (`ExpectedVersion`), idempotency key, and optional reason (`[ValidateAntiForgeryToken]`, authorization via `IOrderOperationService`)
  - `GET /v1/orders/{id}/activity`: Order activity timeline (`TenantPermissions.OrdersRead`)
  - `GET /v1/orders/{id}/notifications`: Order notifications status and attempts (`TenantPermissions.OrdersRead`)
  - Stale version concurrency rejection: Return `409 Conflict` (ProblemDetails) when version mismatch occurs

- [x] **Task 4: Backend Tests (`Kreyora.UnitTests` & `Kreyora.IntegrationTests`)**
  - Real PostgreSQL Testcontainers integration tests in `Kreyora.IntegrationTests/Orders/SellerOrderWorkspaceIntegrationTests.cs`:
    - List and filter orders across tenants (verify tenant isolation)
    - Retrieve order details with items and payment attempts
    - Retrieve allowed actions per order state
    - Execute actions through the full order lifecycle (`Confirm`, `Prepare`, `Dispatch`, `Deliver`, `Cancel`)
    - Execute payment verification (`VerifyPayment`, `RejectPayment`) on merchant QR with proof
    - Execute COD collection recording (`MarkCodCollected`)
    - Verify stale version concurrency rejection (`409 Conflict`)
    - Verify role authorization (Viewer rejected with 403 on mutation endpoints)
  - Full backend test suite passing (342/342 tests)

- [x] **Task 5: Frontend Ports & API Adapters (`apps/web`)**
  - Update `OrderClient` port in `apps/web/src/lib/ports/order-client.ts`
  - Update `PaymentClient` port in `apps/web/src/lib/ports/payment-client.ts` (add `getProofContentUrl`)
  - Implement `apiOrderClient` in `apps/web/src/lib/adapters/api/order-client.ts`
  - Implement `apiPaymentClient` in `apps/web/src/lib/adapters/api/payment-client.ts`
  - Export adapters in `apps/web/src/lib/adapters/api/index.ts`
  - Bind `apiOrderClient` and `apiPaymentClient` in `client-provider.tsx` when `USING_FIXTURE_ADAPTERS` is false
  - Update `mockOrderClient` and `mockPaymentClient` for offline demo fidelity

- [x] **Task 6: Frontend Seller Order Workspace UI (`apps/web/src/app/(seller)/orders`)**
  - Update `orders/page.tsx`:
    - Connect to `orderClient.listOrders` with server-side pagination, status filters, and search
    - Loading skeletons, empty states with filter reset, and error retry states
  - Update `orders/[id]/page.tsx`:
    - Connect real order details, items, financial totals, customer and address data
    - Dynamic allowed action buttons with confirmation dialogs and reason capture (min 3 chars)
    - Merchant QR proof review: thumbnail preview, full-size modal dialog, quick verify/reject controls
    - COD collection recording button when eligible
    - Real notification delivery cards with status badges and attempt counts
    - Chronological audit timeline from `orderClient.getOrderActivity`
    - Concurrency conflict (409) recovery banner with one-click refresh button
    - Role-aware UI: hide or disable mutation controls for Viewer with explanatory tooltip/badge; enable for Owner and Operator

- [x] **Task 7: Frontend Tests & Verification**
  - Component and port tests in `apps/web/src/__tests__/orders.test.tsx`
  - Role tests for Owner, Operator, Viewer
  - Concurrency conflict (409) recovery tests
  - Run full frontend CI gate (`pnpm ci:frontend`) — passed with 0 errors, 451 tests passing

- [x] **Task 8: Checkpoint & Documentation**
  - Create `artifacts/checkpoints/M06-S05.md` with status `REVIEW`
  - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`
