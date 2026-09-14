# Handoff: Milestone 06 Step 02 — COD and Merchant-QR Payment Domain

## 1. Overview

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** 02 — COD and merchant-QR payment domain
- **Phase:** Phase 2 (Builder) Implementation & Verification
- **Governing Plan:** `docs/plan/M06-S02_COD_MERCHANT_QR_PAYMENT_PLAN.md`
- **Milestone Reference:** `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`

---

## 2. Implementation Tasks

- [ ] **Task 1: Domain Entities & Enums**
  - Create `PaymentAttemptStatus` & `PaymentProofStatus` in `Kreyora.Domain.Payments`
  - Create `PaymentAttempt` aggregate root in `Kreyora.Domain.Payments`
  - Create `PaymentProof` entity in `Kreyora.Domain.Payments`
  - Create `StorePaymentConfiguration` entity in `Kreyora.Domain.Storefront`
- [ ] **Task 2: Application Contracts**
  - Create `PaymentContracts.cs` in `Kreyora.Application.Payments` with `IPaymentService` and `IStorePaymentConfigurationService`
  - Extend `ExecuteOrderActionRequest` in `Kreyora.Application.Orders.OrderContracts` with optional `PaymentAttemptId`
- [ ] **Task 3: EF Core Mapping & Database Migration**
  - Create `PaymentAttemptConfiguration.cs`
  - Create `PaymentProofConfiguration.cs`
  - Create `StorePaymentConfigurationConfiguration.cs`
  - Register DbSets and tenant query filters in `AppDbContext`
  - Add EF migration `AddPaymentDomain` and test for pending changes
- [ ] **Task 4: Infrastructure Services**
  - Implement `PaymentService.cs` in `Kreyora.Infrastructure.Payments` (handling attempts, proof upload via `IPrivateObjectStorage`, proof download)
  - Implement `StorePaymentConfigurationService.cs` in `Kreyora.Infrastructure.Payments` (get and update configuration with authorization and validation)
  - Update `OrderCreationService.cs` to create initial `PaymentAttempt` on order creation
  - Update `OrderOperationService.cs` to update `PaymentAttempt` status and record verification/rejection/collection details transactionally
  - Register new services in `DependencyInjection.cs`
- [ ] **Task 5: WebApi Controllers**
  - Create `PaymentController.cs` for payment attempts, proof submission, and proof retrieval
  - Create `StorePaymentConfigurationController.cs` for store payment configuration GET/PUT
- [ ] **Task 6: Unit & Integration Testing**
  - Unit tests: `PaymentAttemptTests`, `PaymentProofTests`, `StorePaymentConfigurationTests`
  - Integration tests: `PaymentServiceTests`, `StorePaymentConfigurationServiceTests`, update `OrderOperationServiceTests` and `OrderCreationServiceTests`
- [ ] **Task 7: Quality Gates & Checkpoint Report**
  - Run full solution build and tests
  - Verify `has-pending-model-changes`
  - Create checkpoint `artifacts/checkpoints/M06-S02.md` with status `REVIEW`
  - Update `CURRENT_WORK.md` and `06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`
