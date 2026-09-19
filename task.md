# Handoff: Milestone 07 Step 04 — Normalization, Processing Jobs, Retry, DLQ, and Replay

## 1. Overview

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 04 — Normalization, processing jobs, retry, DLQ, and replay
- **Phase:** Phase 2 (Builder) Execution — Complete; Awaiting Human Approval
- **Governing Plan:** `docs/plan/M07-S04_NORMALIZATION_JOBS_PLAN.md`
- **Active Milestone File:** `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`
- **Current Checkpoint:** `artifacts/checkpoints/M07-S04.md` (REVIEW)
- **Status:** `REVIEW`

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Domain Models, Retry Policy & Invariants (`services/api/src/Kreyora.Domain/Integrations/`)**
  - Implement `WebhookFailureClassification` enum (`Transient = 1`, `Permanent = 2`, `Exhausted = 3`).
  - Implement `WebhookRetryPolicy` record with bounded exponential backoff (e.g. 15s, 60s, 300s, 900s, 3600s), jitter calculation, and `GetBackoffForAttempt(int attempt)`.
  - Extend `WebhookEvent` aggregate root:
    - Add fields: `AttemptCount`, `MaxAttempts`, `NextRetryAt`, `DeadLetteredAt`, `LastAttemptedAt`, `FailureClassification`.
    - Implement domain methods: `RecordFailure(string error, WebhookFailureClassification classification, DateTimeOffset now, WebhookRetryPolicy policy)`, `RecordSuccess(DateTimeOffset processedAt)`, `Replay(DateTimeOffset now)`, and `QuarantinePoison(string reason, DateTimeOffset now)`.
  - Implement `InboundEvent` aggregate root inheriting `BaseEntity, ITenantOwned`:
    - Fields: `TenantId`, `ConnectionId`, `WebhookEventId` (FK to raw WebhookEvent), `Channel`, `ProviderMessageId`, `SchemaVersion`, `EventType`, `PayloadJson`, `OccurredAt`, `CreatedAt`.
    - Invariants: required fields, bounds, and factory `Create(...)`.
  - Add domain unit tests in `Kreyora.UnitTests/Integrations/`.

- [x] **Task 2: Persistence & EF Core Migration (`services/api/src/Kreyora.Infrastructure/`)**
  - Update `WebhookEventConfiguration.cs` with column mappings for `attempt_count`, `max_attempts`, `next_retry_at`, `dead_lettered_at`, `last_attempted_at`, `failure_classification`.
  - Implement `InboundEventConfiguration.cs` in `Persistence/Configurations/`:
    - Map `inbound_events` table.
    - Foreign key `webhook_event_id` -> `WebhookEvent(id)` with `DeleteBehavior.Restrict`.
    - Partial unique index: `(connection_id, provider_message_id) WHERE provider_message_id IS NOT NULL`.
    - Query indexes: `(tenant_id, webhook_event_id)`, `(tenant_id, occurred_at)`.
    - Shadow concurrency token `xmin`.
  - Add `DbSet<InboundEvent> InboundEvents` to `AppDbContext.cs` with tenant query filter.
  - Generate migration `AddWebhookEventRetryAndInboundEvents`.
  - Verify zero pending model changes.

- [x] **Task 3: Application Contracts & DTOs (`services/api/src/Kreyora.Application/Integrations/`)**
  - Define `IWebhookProcessingService.cs`:
    - `Task<WebhookProcessingResult> ProcessWebhookEventAsync(string webhookEventId, CancellationToken cancellationToken = default)`
    - `Task<PagedResult<WebhookDeadLetterDto>> GetDeadLetterEventsAsync(WebhookDeadLetterQuery query, CancellationToken cancellationToken = default)`
    - `Task<WebhookReplayResult> ReplayWebhookEventAsync(string webhookEventId, string idempotencyKey, CancellationToken cancellationToken = default)`
  - Define `WebhookDeadLetterDto`, `WebhookDeadLetterQuery`, `WebhookReplayResult`, `WebhookProcessingResult`.

- [x] **Task 4: Infrastructure Processing Service, Job & Classifier (`services/api/src/Kreyora.Infrastructure/Integrations/`)**
  - Implement `WebhookFailureClassifier.cs`:
    - Classify exceptions into `Transient` (timeouts, network, rate limits, `DbUpdateConcurrencyException`) vs `Permanent` (format errors, poison payloads, unsupported schema version, invalid connection status).
  - Implement `WebhookProcessingService.cs`:
    - Fetch event (scoped by tenant).
    - Validate schema version (`schemaVersion == "v1"`).
    - Resolve provider from `IChannelProviderRegistry`.
    - Call `provider.NormalizeInboundAsync(...)`.
    - Check duplicate message IDs on `InboundEvents` before inserting.
    - Persist `InboundEvent` entities.
    - Handle exceptions with `WebhookFailureClassifier` and update `WebhookEvent` accordingly.
  - Implement `WebhookProcessingJob.cs` (Hangfire background job):
    - Multi-tenant execution with `ITenantJobRunner.RunAsync(...)`.
    - Selects `Received` and due `Failed` events in batch.
    - Enforces `[DisableConcurrentExecution(timeoutInSeconds: 55)]`.
  - Enhance `SimulatorChannelProvider.NormalizeInboundAsync` to support payload types and simulated poison/schema mismatch payloads.
  - Register services and jobs in `DependencyInjection.cs`.

- [x] **Task 5: Web API Controller for DLQ & Replay (`services/api/src/Kreyora.WebApi/Controllers/`)**
  - Implement `IntegrationDiagnosticsController.cs`:
    - `GET /v1/integrations/webhooks/dead-letter`: list dead-lettered events (Policy: `IntegrationsRead`).
    - `POST /v1/integrations/webhooks/{id}/replay`: authorized replay with `Idempotency-Key` header (Policy: `IntegrationsWrite`).
    - Anti-forgery validation and audit event logging.

- [x] **Task 6: Unit & Real Testcontainers Integration Tests**
  - Unit tests:
    - `WebhookRetryPolicyTests`: Exponential backoff and jitter bounds.
    - `WebhookFailureClassifierTests`: Transient vs permanent vs poison classifications.
    - `WebhookEventRetryTests`: State transitions, max attempts exhaustion, replay resets, and poison quarantine.
    - `InboundEventTests`: Creation, property bounds, payload serialization.
  - Integration tests in `WebhookProcessingIntegrationTests.cs` (PostgreSQL Testcontainers in Docker):
    - Normal normalization: raw webhook -> `InboundEvent` with FK.
    - Duplicate provider message ID rejection/skipping: no duplicate `InboundEvent` row.
    - Out-of-order events: proper timestamp handling.
    - Transient failure & exponential retry: increments attempt, computes `NextRetryAt`.
    - Retry exhaustion -> DLQ: status becomes `DeadLetter`.
    - Poison event quarantine: malformed payload immediately becomes `DeadLetter`.
    - Authorized idempotent replay: DLQ event reset to `Received` with `Idempotency-Key`.
    - Cross-tenant job isolation: Tenant A job never accesses Tenant B events.

- [x] **Task 7: Quality Gates & Verification**
  - Run backend build and all tests.
  - Verify EF migration model state.
  - Run frontend CI (`pnpm ci:frontend`).
  - Run git diff check (`git diff --check`).
  - Create checkpoint report `artifacts/checkpoints/M07-S04.md` (`REVIEW`).
  - Update `CURRENT_WORK.md` and `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`.
