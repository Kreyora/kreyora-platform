# Kreyora — Milestone 06 Step 05 Plan
# Seller Order Workspace Integration

## Goal

Replace seller order, payment, fulfilment, activity, and notification fixtures with generated real clients and robust API endpoints. Preserve the approved screens and connect allowed-action responses, concurrency conflict recovery, confirmation dialogs, reason capture, merchant QR proof review, COD collection, audit timeline, and notification delivery state. Enforce role-aware visibility in the UI while strictly relying on server authorization.

---

## Allowed Scope

1. **Backend Application & WebApi (`services/api`)**:
   - `Kreyora.Application.Orders`:
     - Define `IOrderQueryService` interface and query contracts (`OrderQuery`, `OrderSummaryItem`, `OrderDetailItem`, `OrderItemDetail`, `OrderActivityItem`, `OrderNotificationItem`).
   - `Kreyora.Infrastructure.Orders`:
     - Implement `OrderQueryService` with tenant scoping, pagination, filter criteria, payment attempt resolution, audit timeline resolution, and order notification resolution.
     - Register `OrderQueryService` in `DependencyInjection.cs`.
   - `Kreyora.WebApi.Controllers`:
     - Implement `OrdersController` with endpoints:
       - `GET /v1/orders`: Paginated orders with status, source, and search filters (`TenantPermissions.OrdersRead`).
       - `GET /v1/orders/{id}`: Order detail including items, addresses, financial totals, payment attempts, and row version (`TenantPermissions.OrdersRead`).
       - `GET /v1/orders/{id}/actions`: Allowed action evaluations with denial reasons (`TenantPermissions.OrdersRead`).
       - `POST /v1/orders/{id}/actions`: Execute order operations with concurrency check (`ExpectedVersion`), idempotency key, and optional reason/paymentAttemptId (`[ValidateAntiForgeryToken]`, authorization enforced via `IOrderOperationService`).
       - `GET /v1/orders/{id}/activity`: Chronological audit activity events (`TenantPermissions.OrdersRead`).
       - `GET /v1/orders/{id}/notifications`: Notification delivery states and attempt histories (`TenantPermissions.OrdersRead`).
   - Concurrency conflict handling: Return `409 Conflict` (RFC 7807 ProblemDetails) when `ExpectedVersion` does not match `order.RowVersion`.

2. **Frontend Typed Ports & Adapters (`apps/web`)**:
   - `OrderClient` port (`apps/web/src/lib/ports/order-client.ts`):
     - Expand methods: `listOrders`, `getOrder`, `getAllowedActions`, `executeAction`, `getOrderActivity`, `getOrderNotifications`.
   - `PaymentClient` port (`apps/web/src/lib/ports/payment-client.ts`):
     - Ensure support for `getPaymentAttempts(orderId)` and `getProofContentUrl(proofId)`.
   - Implement `apiOrderClient` (`apps/web/src/lib/adapters/api/order-client.ts`) and `apiPaymentClient` (`apps/web/src/lib/adapters/api/payment-client.ts`).
   - Update `client-provider.tsx` to bind `apiOrderClient` and `apiPaymentClient` when `USING_FIXTURE_ADAPTERS` is false.
   - Update `mockOrderClient` and `mockPaymentClient` to maintain parity for demo/test mode.

3. **Frontend Seller Order Workspace UI (`apps/web/src/app/(seller)/orders`)**:
   - `orders/page.tsx`:
     - Wire to `orderClient.listOrders` with query state, search, status filters, and pagination.
     - Accessible loading skeletons, empty state with filter clearing, and error recovery.
   - `orders/[id]/page.tsx`:
     - Wire to `orderClient.getOrder`, `getAllowedActions`, `getOrderActivity`, `getOrderNotifications`, and `paymentClient.getPaymentAttempts`.
     - Connect dynamic allowed action buttons with confirmation dialogs and reason capture.
     - Connect merchant QR payment proof review: thumbnail, modal preview, verify/reject actions.
     - Connect COD collection recording button when eligible.
     - Connect real notification cards with delivery status badges, channels, and attempt counts.
     - Connect chronological audit trail.
     - Concurrency conflict (409) handling: inline warning banner with one-click refresh CTA to reload latest state without losing user context.
     - Role-aware UI: hide or disable mutation controls for Viewer with informative helper text; show full controls for Owner and Operator.

4. **Testing**:
   - Backend unit tests (`Kreyora.UnitTests`) for `OrderQueryService` and `OrdersController`.
   - Real PostgreSQL Testcontainers integration tests (`Kreyora.IntegrationTests`) for `OrdersController`:
     - Listing and filtering across tenants.
     - Order detail retrieval with items and payment attempts.
     - Allowed actions evaluation.
     - Action execution (`Confirm`, `Prepare`, `Dispatch`, `Deliver`, `VerifyPayment`, `Cancel`).
     - Stale version concurrency rejection (`409 Conflict`).
     - Role RBAC enforcement (Viewer rejected with 403 on mutation endpoints).
   - Frontend tests (`apps/web/src/__tests__/orders.test.tsx`):
     - List page rendering, filtering, search.
     - Detail page rendering with real data, proof viewer, allowed actions, notifications.
     - Action modal, reason capture, and execution.
     - Stale version conflict alert and refresh.
     - Role-based control visibility (Viewer vs. Operator/Owner).

---

## Prohibited Scope

- Starting Milestone 06 Step 06 (end-to-end failure scenarios) before Step 05 approval.
- Introducing live payment gateways (eSewa, Khalti) or live SMS/WhatsApp providers.
- Modifying the PostgreSQL database schema (existing `orders`, `order_items`, `payment_attempts`, `payment_proofs`, `notification_requests`, and `audit_events` tables are already migrated and sufficient).
- Bypassing server-side authorization or business invariant checks from the client.
- Modifying accepted ADRs without authorization.

---

## Affected Files and Modules

| Component | File / Path | Action | Description |
|---|---|---|---|
| Backend Application | `services/api/src/Kreyora.Application/Orders/OrderContracts.cs` | **Modify** | Add `IOrderQueryService`, `OrderQuery`, `OrderSummaryItem`, `OrderDetailItem`, `OrderItemDetail`, `OrderActivityItem`, `OrderNotificationItem`. |
| Backend Infrastructure | `services/api/src/Kreyora.Infrastructure/Orders/OrderQueryService.cs` | **New** | Implement `IOrderQueryService` querying orders, items, payment attempts, proofs, audit timeline, and notifications. |
| Backend Infrastructure | `services/api/src/Kreyora.Infrastructure/DependencyInjection.cs` | **Modify** | Register `IOrderQueryService` in DI. |
| Backend WebApi | `services/api/src/Kreyora.WebApi/Controllers/OrdersController.cs` | **New** | Expose `/v1/orders` list, detail, actions, execute, activity, and notifications endpoints. |
| Backend Unit Tests | `services/api/tests/Kreyora.UnitTests/Orders/OrderQueryServiceTests.cs` | **New** | Unit tests for query filtering, sorting, and detail mapping. |
| Backend Integration Tests | `services/api/tests/Kreyora.IntegrationTests/Orders/SellerOrderWorkspaceIntegrationTests.cs` | **New** | Real PostgreSQL Testcontainers integration tests for seller order endpoints, actions, 409 conflict, and role RBAC. |
| Frontend Ports | `apps/web/src/lib/ports/order-client.ts` | **Modify** | Update `OrderClient` interface with action execution, activity, and notifications. |
| Frontend Ports | `apps/web/src/lib/ports/payment-client.ts` | **Modify** | Add `getProofContentUrl` to `PaymentClient`. |
| Frontend Types | `apps/web/src/lib/types/orders.ts` | **Modify** | Align `OrderActionEvaluation`, `OrderNotification` with backend models. |
| Frontend API Adapters | `apps/web/src/lib/adapters/api/order-client.ts` | **New** | Real API adapter implementing `OrderClient` via HTTP calls to `/v1/orders`. |
| Frontend API Adapters | `apps/web/src/lib/adapters/api/payment-client.ts` | **New** | Real API adapter implementing `PaymentClient` via HTTP calls to `/v1/orders/{id}/payments`. |
| Frontend API Adapters | `apps/web/src/lib/adapters/api/index.ts` | **Modify** | Export `apiOrderClient` and `apiPaymentClient`. |
| Frontend Providers | `apps/web/src/lib/providers/client-provider.tsx` | **Modify** | Switch `order` and `payment` to real API adapters when `USING_FIXTURE_ADAPTERS` is false. |
| Frontend Mock Adapters | `apps/web/src/lib/adapters/mock/mock-order-client.ts` | **Modify** | Implement new port methods in mock client for offline fidelity. |
| Frontend Mock Adapters | `apps/web/src/lib/adapters/mock/mock-payment-client.ts` | **Modify** | Implement `getProofContentUrl` in mock client. |
| Frontend Pages | `apps/web/src/app/(seller)/orders/page.tsx` | **Modify** | Wire to real `OrderClient.listOrders`, pagination, and filter state. |
| Frontend Pages | `apps/web/src/app/(seller)/orders/[id]/page.tsx` | **Modify** | Wire to real order data, action dialogs, QR proof preview, COD collection, conflict recovery, notifications, and role controls. |
| Frontend Tests | `apps/web/src/__tests__/orders.test.tsx` | **Modify** | Comprehensive tests for list, detail, action execution, conflict recovery, proof review, and role behavior. |

---

## Data & API Contracts

### Endpoints

1. **`GET /v1/orders`**
   - Query: `page` (int, default 1), `pageSize` (int, default 20), `status` (OrderStatus?), `paymentStatus` (PaymentStatus?), `fulfilmentStatus` (FulfilmentStatus?), `paymentMethod` (OrderPaymentMethod?), `source` (OrderSource?), `search` (string?)
   - Authorization: `TenantPermissions.OrdersRead` (Owner, Operator, Viewer)
   - Response: `200 OK` -> `PagedResult<OrderSummaryItem>`

2. **`GET /v1/orders/{id}`**
   - Authorization: `TenantPermissions.OrdersRead`
   - Response: `200 OK` -> `OrderDetailItem` | `404 Not Found`

3. **`GET /v1/orders/{id}/actions`**
   - Authorization: `TenantPermissions.OrdersRead`
   - Response: `200 OK` -> `IReadOnlyList<OrderActionEvaluation>` | `404 Not Found`

4. **`POST /v1/orders/{id}/actions`**
   - Header: `Idempotency-Key` (required)
   - Body: `ExecuteOrderActionBody(OrderAction Action, string? Reason, uint ExpectedVersion, string? PaymentAttemptId, string? ProviderReference)`
   - Authorization: Enforced via `IOrderOperationService` (`PaymentsManage` for payment actions, `OrdersWrite` for order/fulfilment actions). Rejects Viewer with `403 Forbidden`.
   - Concurrency: Rejects stale `ExpectedVersion` with `409 Conflict`.
   - Response: `200 OK` -> `OrderOperationResult` | `400 Bad Request` | `403 Forbidden` | `404 Not Found` | `409 Conflict`

5. **`GET /v1/orders/{id}/activity`**
   - Authorization: `TenantPermissions.OrdersRead`
   - Response: `200 OK` -> `IReadOnlyList<OrderActivityItem>` | `404 Not Found`

6. **`GET /v1/orders/{id}/notifications`**
   - Authorization: `TenantPermissions.OrdersRead`
   - Response: `200 OK` -> `IReadOnlyList<OrderNotificationItem>` | `404 Not Found`

---

## State Transitions & Invariants

1. **Server-Authoritative State**:
   - Order status, payment status, and fulfilment status are authoritative only on the server.
   - Action eligibility is determined by `OrderTransitionPolicy` and evaluated per tenant role.
2. **Optimistic Concurrency Control**:
   - Mutation actions must supply `ExpectedVersion` (`RowVersion`).
   - If another user or background job updated the order concurrently, the request is rejected with `409 Conflict`.
   - The UI presents an inline banner notifying the user of external changes and providing a one-click refresh button.
3. **Audit & Idempotency**:
   - Action execution requires an `Idempotency-Key` header and records an append-only `AuditEvent` with actor, tenant, operation, and reason.
   - Replaying the same request with the same idempotency key returns the cached result without repeating effects.
4. **Tenant Isolation**:
   - All order reads and mutations enforce `TenantId` filtering via `ITenantContextAccessor` and global EF filters.
   - Attempting to access another tenant's order yields `404 Not Found`.
5. **Role-Aware RBAC**:
   - **Viewer**: Read-only access to orders, proofs, audit trail, and notifications. UI hides or disables action buttons. Direct API mutation calls return `403 Forbidden`.
   - **Operator**: Can confirm, cancel (with reason), prepare, dispatch, deliver, verify/reject payment, and mark COD collected.
   - **Owner**: Full operational and management access.

---

## Verification Plan

### Automated Tests

1. **Backend Tests**:
   - Unit tests:
     ```bash
     dotnet test services/api/tests/Kreyora.UnitTests/Kreyora.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Order"
     ```
   - Integration tests (real PostgreSQL Testcontainers):
     ```bash
     dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~SellerOrderWorkspaceIntegrationTests"
     ```
   - Full backend solution suite:
     ```bash
     dotnet test services/api/Kreyora.slnx --configuration Release
     ```

2. **Frontend Tests**:
   - Component & port tests:
     ```bash
     pnpm test
     ```
   - Full frontend CI gate:
     ```bash
     pnpm ci:frontend
     ```

### Manual Verification Procedure

1. Launch local composition (`docker compose up -d` or Aspire AppHost).
2. Log in as an Operator / Owner in the seller workspace.
3. Navigate to `/orders` and verify real orders appear with correct pagination, search, and status filtering.
4. Open an order detail page (`/orders/{id}`):
   - Verify financial totals, items, and delivery address match backend facts.
   - Review merchant QR proof thumbnail and open full-size modal.
   - Execute an allowed action (e.g. `Confirm` or `Verify Payment`).
   - Verify confirmation dialog opens, reason capture works where required, and action completes.
   - Verify activity timeline reflects the newly executed action with actor and timestamp.
   - Verify notification delivery card reflects outbox delivery status.
5. Test concurrency conflict: simulate stale version and confirm friendly 409 recovery prompt appears with refresh button.
6. Log in as Viewer: verify all order details, proofs, and timeline are readable, but action buttons are disabled or hidden with explanatory badge.

---

## Handoff to Builder

Stop after approval of this plan. Builder (Phase 2) will implement the changes in strictly approved order:
1. Backend Application contracts & Infrastructure `OrderQueryService`.
2. Backend `OrdersController` and WebApi registration.
3. Backend unit and PostgreSQL Testcontainers integration tests.
4. Frontend ports, API adapters, and client-provider wiring.
5. Frontend seller order list and detail pages with action dialogs, QR proof review, COD collection, and 409 conflict recovery.
6. Frontend unit and component tests.
7. Full regression gates (`dotnet test services/api/Kreyora.slnx`, `pnpm ci:frontend`).

