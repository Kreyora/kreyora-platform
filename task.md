# Handoff: Milestone 06 Step 04 — Notification Outbox and Development Provider

## 1. Overview

- **Milestone:** 06 — Order Operations, Manual Payments, Fulfilment, and Notifications
- **Step:** 04 — Notification outbox and development provider
- **Phase:** Phase 2 (Builder) Execution
- **Governing Plan:** `docs/plan/M06-S04_NOTIFICATION_OUTBOX_PLAN.md`
- **Active Milestone File:** `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`
- **Prior Checkpoint:** `artifacts/checkpoints/M06-S03.md` (APPROVED)

---

## 2. Implementation Checklist (Phase 2 Builder)

- [ ] **Task 1: Domain Entities & Invariants (`Kreyora.Domain.Notifications`)**
  - `NotificationChannel.cs` (Email, Sms, InApp)
  - `NotificationStatus.cs` (Pending, Delivering, Delivered, Failed, DeadLettered)
  - `NotificationRetryPolicy.cs` (MaxAttempts, BackoffIntervals)
  - `NotificationRequest.cs` (Aggregate root with state transitions: MarkDelivering, RecordSuccess, RecordFailure, Replay)
  - `NotificationDeliveryAttempt.cs` (Child entity tracking attempts, duration, error, provider ref)
  - Unit tests in `Kreyora.UnitTests` for `NotificationRequest` state machine and `NotificationRetryPolicy`

- [ ] **Task 2: Application Contracts & DTOs (`Kreyora.Application.Notifications`)**
  - `NotificationContracts.cs`: `INotificationService`, `INotificationDeliveryProvider`, `INotificationTemplateRegistry`, query/result DTOs (`NotificationSummary`, `NotificationDetail`, `NotificationDeliveryRequest`, `NotificationDeliveryResult`, `RenderedNotification`, `NotificationTemplateMapping`)
  - `NotificationOptions.cs`: configuration for retry policy and execution settings
  - `PiiRedaction.cs`: static helpers for redacting email and phone in logs and DTOs
  - Unit tests in `Kreyora.UnitTests` for `PiiRedaction`

- [ ] **Task 3: Infrastructure Templates & Provider (`Kreyora.Infrastructure.Notifications`)**
  - `NotificationTemplateRegistry.cs`: registry mapping events (`order.created.v1`, `order.confirmed.v1`, `order.cancelled.v1`, `order.dispatched.v1`, `order.delivered.v1`, `order.delivery_failed.v1`, `payment.verified.v1`, `payment.rejected.v1`) to email templates
  - `DevelopmentNotificationProvider.cs`: dev sink writing to `NotificationDeliveryLog` table without network calls
  - `NotificationService.cs`: query, detail, and replay service methods with tenant isolation and PII redaction
  - Unit tests for template rendering

- [ ] **Task 4: Persistence, Entities & EF Core Migration**
  - `NotificationDeliveryLog.cs` entity in `Kreyora.Infrastructure.Persistence.Entities`
  - EF Core configurations: `NotificationRequestConfiguration`, `NotificationDeliveryAttemptConfiguration`, `NotificationDeliveryLogConfiguration`
  - Register `DbSet`s in `AppDbContext`, add global tenant query filters, configure tenant ownership and append-only rules
  - Generate and apply EF Core migration `AddNotificationTables`
  - Verify with `dotnet ef migrations has-pending-model-changes`

- [ ] **Task 5: Hangfire Background Jobs**
  - `OutboxNotificationProcessorJob`: minutely recurring job picking unprocessed `OutboxMessage`s, extracting customer contact from orders, generating `NotificationRequest`s idempotently, and setting `ProcessedAt`
  - `NotificationDeliveryJob`: minutely recurring job picking pending/retryable `NotificationRequest`s, rendering templates, delivering via `INotificationDeliveryProvider`, recording attempts, updating status/DLQ
  - Register services in `DependencyInjection.cs` and recurring jobs in `Program.cs`

- [ ] **Task 6: WebApi Controller (`NotificationsController.cs`)**
  - `GET /api/v1/notifications`: paginated list with redacted PII (Owner/Operator/Admin)
  - `GET /api/v1/notifications/{id}`: details with delivery attempt history and redacted PII
  - `GET /api/v1/notifications/dead-letter`: dead-lettered notifications view
  - `POST /api/v1/notifications/{id}/replay`: replay dead-lettered/failed notification (Owner/Admin only, audited)

- [ ] **Task 7: Comprehensive Testing**
  - Unit tests in `Kreyora.UnitTests`
  - Integration tests in `Kreyora.IntegrationTests.Notifications`:
    - Outbox to notification request generation & idempotency
    - Delivery lifecycle, success, dev sink logging
    - Bounded retries and transition to DeadLettered
    - Manual replay authorization and lifecycle reset
    - Cross-tenant isolation (cannot see or replay other tenant's notifications)
    - PII redaction validation in API responses and logs
  - Run full solution tests (`dotnet test services/api/Kreyora.slnx --configuration Release`)

- [ ] **Task 8: Checkpoint & Documentation**
  - Create `artifacts/checkpoints/M06-S04.md`
  - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md`
