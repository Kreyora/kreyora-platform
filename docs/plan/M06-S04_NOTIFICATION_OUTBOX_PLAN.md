# M06-S04 — Notification Outbox and Development Provider Plan

## Scope

Implement the notification lifecycle: outbox processing → notification request → template rendering → provider-neutral delivery → bounded retry → dead-letter visibility → manual replay. Include a safe development sink, PII redaction, and comprehensive tests.

## Architecture Decisions

1. **Templates are code-defined** in a `NotificationTemplateRegistry`, not database-managed. Future milestones may add database-backed templates via ADR.
2. **Email-only for MVP.** `NotificationChannel` enum includes `Sms` and `InApp` for forward compatibility, but no provider or templates exist for those channels.
3. **Two-phase job design:** `OutboxNotificationProcessorJob` (outbox → notification requests) is separate from `NotificationDeliveryJob` (notification requests → delivery) for independent retry and clear failure boundaries.
4. **Development sink** records rendered output to `notification_delivery_logs` for developer inspection without contacting customers.
5. **PII redaction** in API responses and logs; plaintext in database (protected by tenant isolation + RBAC).

## Affected Files

### Domain (new namespace: `Kreyora.Domain.Notifications`)
- `NotificationChannel.cs` — enum
- `NotificationStatus.cs` — enum
- `NotificationRequest.cs` — aggregate root with state machine
- `NotificationDeliveryAttempt.cs` — child entity
- `NotificationRetryPolicy.cs` — value object

### Application (new namespace: `Kreyora.Application.Notifications`)
- `NotificationContracts.cs` — `INotificationService`, `INotificationDeliveryProvider`, `INotificationTemplateRegistry`, DTOs
- `NotificationOptions.cs` — typed configuration

### Infrastructure (new namespace: `Kreyora.Infrastructure.Notifications`)
- `NotificationService.cs` — implements `INotificationService`
- `OutboxNotificationProcessorJob.cs` — Hangfire recurring job
- `NotificationDeliveryJob.cs` — Hangfire recurring job
- `DevelopmentNotificationProvider.cs` — dev sink
- `NotificationTemplateRegistry.cs` — code-defined templates

### Infrastructure/Persistence
- `NotificationRequestConfiguration.cs`
- `NotificationDeliveryAttemptConfiguration.cs`
- `NotificationDeliveryLogConfiguration.cs`
- `AppDbContext.cs` — DbSets, query filters, append-only enforcement
- EF Migration — `AddNotificationTables`

### Infrastructure/Shared
- `PiiRedaction.cs` — static redaction helpers

### WebApi
- `NotificationsController.cs` — 4 endpoints

### DI and Startup
- `DependencyInjection.cs` — register notification services
- `Program.cs` — register Hangfire recurring jobs

## Invariants
- Outbox atomicity: outbox records are marked processed, never deleted
- Tenant isolation: all entities have `TenantId`, EF filters, ownership enforcement
- PII: never in logs or API responses, only in database for delivery
- Idempotency: unique `(tenant_id, idempotency_key)` prevents duplicate notifications
- Dead-letter: visible via admin endpoint with manual replay
- Dev safety: no external network calls in development provider

## Test Plan
- Unit: NotificationRequest state machine, retry policy, PII redaction, template registry
- Integration: outbox processing, delivery lifecycle, retry/dead-letter, idempotency, tenant isolation, replay authorization, PII in responses

## Prohibited
- No real SMTP/SMS delivery in this step
- No database-managed template editor
- No cross-tenant notification access
- No PII in logs or API responses

