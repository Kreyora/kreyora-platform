# Handoff: Milestone 07 Step 05 — Outbound Outbox and Delivery Attempts

## 1. Overview

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 05 — Outbound outbox and delivery attempts
- **Phase:** Phase 2 (Builder) Execution — Complete; Awaiting Human Approval
- **Governing Plan:** `docs/plan/M07-S05_OUTBOUND_OUTBOX_PLAN.md`
- **Active Milestone File:** `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`
- **Previous Checkpoint:** `artifacts/checkpoints/M07-S04.md` (APPROVED)
- **Current Checkpoint:** `artifacts/checkpoints/M07-S05.md` (REVIEW)
- **Status:** `REVIEW`

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Domain Models, Lifecycle & Invariants (`services/api/src/Kreyora.Domain/Integrations/`)**
  - Implement `OutboundMessageStatus` enum (`Queued = 0`, `Sending = 1`, `Sent = 2`, `Delivered = 3`, `Read = 4`, `Failed = 5`, `DeadLetter = 6`, `Cancelled = 7`).
  - Implement `OutboundMessageType` enum (`Text = 1`, `Media = 2`, `LinkPreview = 3`, `Template = 4`).
  - Implement `OutboundMessage` aggregate root (`BaseEntity, ITenantOwned`):
    - Identity: `TenantId`, `ConnectionId`, `Channel`, `IdempotencyKey`.
    - Routing: `ConversationId` (nullable placeholder), `RecipientChannelId`.
    - Content: `MessageType`, `TextContent`, `MediaUrl`, `MediaContentType`, `Caption`, `TemplateCode`, `TemplateParametersJson`, `MetadataJson`.
    - Status & Provider: `Status`, `ProviderMessageId`.
    - Retry & Error: `AttemptCount`, `MaxAttempts`, `NextRetryAt`, `FailureClassification`, `LastErrorMessage`.
    - Timestamps: `QueuedAt`, `SentAt`, `DeliveredAt`, `ReadAt`, `FailedAt`, `DeadLetteredAt`, `CancelledAt`.
    - Methods: `Create(...)`, `MarkSending()`, `RecordDeliverySuccess(...)`, `RecordDeliveryFailure(...)`, `UpdateProviderStatus(...)`, `Cancel()`, `Replay(...)`.
    - Invariants: status transition checks, monotonic status progression (Sent -> Delivered -> Read), content length bounds.
  - Implement `OutboundDeliveryAttempt` append-only entity (`BaseEntity, ITenantOwned`):
    - Properties: `TenantId`, `OutboundMessageId`, `AttemptNumber`, `Channel`, `ConnectionId`, `StartedAt`, `CompletedAt`, `Succeeded`, `ProviderMessageId`, `ProviderErrorCode`, `ProviderErrorMessage`.
    - Methods: `Create(...)`, `CompleteSuccess(...)`, `CompleteFailure(...)`.
  - Add domain unit tests in `Kreyora.UnitTests/Integrations/`.

- [x] **Task 2: Persistence & EF Core Migration (`services/api/src/Kreyora.Infrastructure/`)**
  - Implement `OutboundMessageConfiguration.cs`:
    - Table `outbound_messages`.
    - Primary key `Id`, alternate key `(TenantId, Id)`.
    - Unique index on `(TenantId, ConnectionId, IdempotencyKey)`.
    - Filtered index on `(TenantId, Status, NextRetryAt)` for active/due queue items (`status IN (0, 5)`).
    - Query index on `(TenantId, Status, QueuedAt)`.
    - Filtered lookup index on `(ConnectionId, ProviderMessageId) WHERE provider_message_id IS NOT NULL` for receipt matching.
    - Concurrency token `xmin`.
  - Implement `OutboundDeliveryAttemptConfiguration.cs`:
    - Table `outbound_delivery_attempts`.
    - FK to `OutboundMessage` (`Restrict`).
    - Index on `(OutboundMessageId, AttemptNumber)`.
  - Update `AppDbContext.cs`:
    - Add `DbSet<OutboundMessage> OutboundMessages` and `DbSet<OutboundDeliveryAttempt> OutboundDeliveryAttempts`.
    - Add global tenant query filters.
    - Add append-only enforcement for `OutboundDeliveryAttempt` in `EnforceTenantOwnership()`.
  - Generate EF Core migration `AddOutboundMessagesAndDeliveryAttempts`.
  - Verify zero pending model changes.

- [x] **Task 3: Application Contracts & DTOs (`services/api/src/Kreyora.Application/Integrations/`)**
  - Define `IOutboundMessageService.cs`.
  - Define `IConversationGate.cs` and `ConversationGateResult` (placeholder).
  - Define `OutboundMessageContracts.cs` (DTOs, commands, queries, results).

- [x] **Task 4: Infrastructure Services, Background Job & Simulator (`services/api/src/Kreyora.Infrastructure/Integrations/`)**
  - Implement `AlwaysAllowConversationGate.cs` (placeholder).
  - Implement `OutboundMessageService.cs`:
    - `QueueMessageAsync`: validate connection (Active), capabilities, gate, idempotency, create & persist message.
    - `ProcessDeliveryAsync`: load message, transition to `Sending`, create attempt, invoke `IChannelProvider.SendMessageAsync`, handle success (`Sent`) or failure (`Failed`/`DeadLetter` via `WebhookFailureClassifier` & `WebhookRetryPolicy`).
    - `ProcessStatusReceiptAsync`: look up message by `(ConnectionId, ProviderMessageId)` and update status monotonically.
    - `CancelMessageAsync` and `ReplayMessageAsync`.
    - Query methods: `GetMessageAsync`, `GetMessagesAsync`, `GetDeadLetterMessagesAsync`.
  - Implement `OutboundDeliveryJob.cs`:
    - Hangfire recurring job running multi-tenant via `ITenantJobRunner`.
    - Dispatches due messages via `ProcessDeliveryAsync`.
    - `[DisableConcurrentExecution(timeoutInSeconds: 55)]`.
  - Enhance `SimulatorChannelProvider.SendMessageAsync`:
    - Support simulation flags via metadata/text (`throw_transient`, `throw_rate_limit`, `throw_permanent`).
  - Hook status receipts in `WebhookProcessingService.cs`:
    - On `MessageStatusUpdatedPayload`, notify `IOutboundMessageService.ProcessStatusReceiptAsync`.
  - Register services and jobs in `DependencyInjection.cs`.

- [x] **Task 5: Web API Controller (`services/api/src/Kreyora.WebApi/Controllers/`)**
  - Implement `OutboundMessagesController.cs`:
    - `POST /v1/integrations/messages` (`IntegrationsWrite`).
    - `GET /v1/integrations/messages/{id}` (`IntegrationsRead`).
    - `GET /v1/integrations/messages` (`IntegrationsRead`).
    - `POST /v1/integrations/messages/{id}/cancel` (`IntegrationsWrite`).
    - `POST /v1/integrations/messages/{id}/replay` (`IntegrationsWrite`, requires `Idempotency-Key`).
    - `GET /v1/integrations/messages/dead-letter` (`IntegrationsRead`).

- [x] **Task 6: Unit & Real Testcontainers Integration Tests**
  - Unit tests:
    - `OutboundMessageTests.cs`: state machine, invariants, monotonicity, bounds, replay, cancellation.
    - `OutboundDeliveryAttemptTests.cs`: attempt creation, completion, immutability.
  - Integration tests in `OutboundMessageIntegrationTests.cs` (PostgreSQL Testcontainers):
    - Queue and deliver successfully.
    - Idempotency key deduplication.
    - Capability check (reject media when not supported).
    - Transient error -> retry backoff -> success.
    - Rate limit (429) -> transient retry.
    - Permanent error -> immediate DLQ.
    - Retry exhaustion -> DLQ.
    - Cancel queued message vs cannot cancel sent message.
    - Replay dead-lettered message.
    - Status receipt hook updates outbound message.
    - Cross-tenant isolation.

- [x] **Task 7: Quality Gates & Verification**
  - Solution build and all backend tests pass.
  - Verify EF migration model state (`has-pending-model-changes`).
  - Frontend CI passes.
  - Git diff check clean.
  - Create checkpoint `artifacts/checkpoints/M07-S05.md`.
  - Update `CURRENT_WORK.md` and `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`.
