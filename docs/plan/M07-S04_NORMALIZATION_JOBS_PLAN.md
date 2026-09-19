# M07-S04: Normalization, Processing Jobs, Retry, DLQ, and Replay — Scoped Plan

## 1. Overview and Authority

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 04 — Normalization, processing jobs, retry, DLQ, and replay
- **Status:** `PLANNING`
- **Governing ADRs:**
  - `ADR-001`: Service-layer pattern with traditional `[ApiController]` controllers (no MediatR/CQRS).
  - `ADR-003`: Verified tenant selection context.
  - `ADR-010`: Social connection ownership and multi-tenant routing.
  - `ADR-011`: Normalized social event versioning (`schemaVersion: "v1"`, polymorphic payloads).
  - `ADR-012`: Raw provider webhook payload retention and privacy policy.
  - `ADR-013`: Secrets encryption and key management.
- **Reference Plan:** `docs/plan/plan.md` §10.6 (Channel integration and event reliability design), §10.13.
- **Active Milestone File:** `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md` (Prompt 04).

---

## 2. Permitted and Prohibited Scope

### Permitted Scope
1. **Domain Models & State Transitions (`Kreyora.Domain/Integrations/`)**:
   - Extend `WebhookEvent` with retry & DLQ fields: `AttemptCount`, `MaxAttempts`, `NextRetryAt`, `DeadLetteredAt`, `LastAttemptedAt`, and domain methods `RecordFailure(...)`, `RecordSuccess(...)`, `Replay(...)`, `QuarantinePoison(...)`.
   - Define `WebhookFailureClassification` enum (`Transient`, `Permanent`, `Exhausted`).
   - Define `WebhookRetryPolicy` with bounded exponential backoff and jitter.
   - Define `InboundEvent` aggregate root linking back to `WebhookEvent.Id` (FK) with `ProviderMessageId`, `SchemaVersion`, `EventType`, `PayloadJson`, `OccurredAt`.
2. **Persistence & EF Core Migration (`Kreyora.Infrastructure/`)**:
   - Update `WebhookEventConfiguration` with new retry/DLQ fields.
   - Implement `InboundEventConfiguration` with unique partial index `(ConnectionId, ProviderMessageId) WHERE ProviderMessageId IS NOT NULL`.
   - Add `DbSet<InboundEvent> InboundEvents` to `AppDbContext` with tenant query filter.
   - Generate migration `AddWebhookEventRetryAndInboundEvents`.
3. **Application Contracts & Services (`Kreyora.Application/Integrations/`)**:
   - Define `IWebhookProcessingService` with `ProcessWebhookEventAsync`, `GetDeadLetterEventsAsync`, `ReplayWebhookEventAsync`.
   - Define `WebhookDeadLetterDto`, `WebhookDeadLetterQuery`, `WebhookReplayResult`, `WebhookProcessingResult`.
4. **Infrastructure Services & Jobs (`Kreyora.Infrastructure/Integrations/`)**:
   - Implement `WebhookFailureClassifier` to classify exceptions into Transient vs Permanent.
   - Implement `WebhookProcessingService` with schema validation, normalization via `IChannelProvider`, deduplication check, and retry/DLQ recording.
   - Implement Hangfire background job `WebhookProcessingJob` establishing explicit tenant context per active tenant using `ITenantJobRunner`.
   - Register services in `DependencyInjection.cs`.
5. **Web API Controller (`Kreyora.WebApi/Controllers/`)**:
   - Implement DLQ query and authorized replay endpoints in `IntegrationDiagnosticsController` (or `WebhooksAdminController`):
     - `GET /v1/integrations/webhooks/dead-letter` (`IntegrationsRead`)
     - `POST /v1/integrations/webhooks/{id}/replay` (`IntegrationsWrite` + `Idempotency-Key`)
6. **Simulator Provider Updates**:
   - Enhance `SimulatorChannelProvider.NormalizeInboundAsync` to support multiple payload types, extract `ProviderMessageId`, and simulate poison/schema-mismatch payloads for testing.
7. **Comprehensive Tests**:
   - Unit tests for retry policy, failure classifier, state transitions, and inbound event creation.
   - Real PostgreSQL Testcontainers integration tests verifying normal processing, duplicate message rejection, out-of-order event handling, transient retry with backoff, DLQ exhaustion, poison quarantine, authorized idempotent replay, and cross-tenant isolation.

### Prohibited Scope
- Milestone 07 Step 05 (`Outbound outbox and delivery attempts`).
- Milestone 07 Step 06 (`Diagnostics UI and full simulator`).
- Milestone 08 (`Conversation`, `Message`, `Customer` inbox entities and UI).
- Real social provider adapters (WhatsApp Cloud API, Instagram Graph API, Viber, etc.).
- Bypassing tenant context in background jobs.

---

## 3. Data Model and Schema Changes

### A. Extended `WebhookEvent` (`Kreyora.Domain.Integrations`)
- New fields:
  - `int AttemptCount`: Number of times processing has been attempted.
  - `int MaxAttempts`: Maximum retry attempts before moving to `DeadLetter` (default 5).
  - `DateTimeOffset? NextRetryAt`: Scheduled time for next retry (if `Failed`).
  - `DateTimeOffset? DeadLetteredAt`: Timestamp when moved to DLQ.
  - `DateTimeOffset? LastAttemptedAt`: Timestamp of the most recent attempt.
  - `WebhookFailureClassification? FailureClassification`: `Transient`, `Permanent`, or `Exhausted`.
- State transitions:
  - `Received` -> `Processing` -> `Processed` (on success)
  - `Processing` -> `Failed` (transient failure, `AttemptCount < MaxAttempts`, `NextRetryAt` computed with backoff + jitter)
  - `Processing` -> `DeadLetter` (permanent failure, poison payload, or `AttemptCount >= MaxAttempts`)
  - `DeadLetter` | `Failed` -> `Received` (via `Replay`, resetting `AttemptCount = 0`, `NextRetryAt = null`, `DeadLetteredAt = null`)

### B. New `InboundEvent` Entity (`Kreyora.Domain.Integrations`)
- Aggregate root inheriting `BaseEntity, ITenantOwned`.
- Properties:
  - `string Id`: ULID (26 chars).
  - `string TenantId`: ULID (26 chars).
  - `string ConnectionId`: ULID (26 chars).
  - `string WebhookEventId`: ULID (26 chars) — Foreign key to `WebhookEvent.Id` (preserves immutable raw event reference).
  - `ChannelType Channel`: Enum.
  - `string? ProviderMessageId`: Max 128 chars.
  - `string SchemaVersion`: Max 16 chars (e.g., `"v1"`).
  - `string EventType`: Max 64 chars (`"text"`, `"media"`, `"status"`, `"profile"`, `"reaction"`).
  - `string PayloadJson`: JSON serialized `NormalizedInboundPayload`.
  - `DateTimeOffset OccurredAt`: Timestamp from provider.
  - `DateTimeOffset CreatedAt`: Timestamp when normalized.
- Constraints & Indexes:
  - Primary key: `Id`.
  - Alternate key: `(TenantId, Id)`.
  - Foreign key: `WebhookEventId` -> `WebhookEvent(Id)` with `DeleteBehavior.Restrict`.
  - Unique composite partial index: `(ConnectionId, ProviderMessageId) WHERE provider_message_id IS NOT NULL` (guarantees deduplication across normalized provider messages).
  - Query index: `(TenantId, WebhookEventId)`.
  - Query index: `(TenantId, OccurredAt)`.
  - PostgreSQL shadow concurrency token `xmin`.

---

## 4. Failure Taxonomy & Retry Policy

### A. Failure Classification
```csharp
public enum WebhookFailureClassification
{
    Transient = 1,   // Network timeout, rate limit (429), DB concurrency conflict -> Retryable
    Permanent = 2,   // Poison payload, malformed JSON, unsupported schema, unknown channel -> Non-retryable
    Exhausted = 3    // Transient failure that reached MaxAttempts -> Moved to DLQ
}
```

### B. Retry Policy with Jitter (`WebhookRetryPolicy`)
- Bounded exponential backoff:
  - Attempt 1: 15s ± jitter
  - Attempt 2: 60s ± jitter
  - Attempt 3: 300s (5m) ± jitter
  - Attempt 4: 900s (15m) ± jitter
  - Attempt 5: 3600s (1h) ± jitter
- Jitter calculation: ±20% randomized spread around the nominal backoff to prevent thundering herd.

---

## 5. Background Processing Job Architecture

- **Class:** `WebhookProcessingJob`
- **Schedule:** Recurring Hangfire job running every 1 minute (or triggered on new event receipt).
- **Execution:**
  1. Iterates over active tenants.
  2. Uses `ITenantJobRunner.RunAsync(new TenantJobEnvelope(tenantId, "webhook-event-processing", ...))` to establish tenant context.
  3. Queries `WebhookEvents` for `tenantId` where:
     `Status == WebhookProcessingStatus.Received || (Status == WebhookProcessingStatus.Failed && NextRetryAt <= now)`
  4. Takes a batch (e.g. 50 events) ordered by `ReceivedAt` ascending.
  5. For each event:
     - Marks `Processing`.
     - Checks schema version.
     - Resolves provider and normalizes into `IReadOnlyList<NormalizedInboundEnvelope>`.
     - For each envelope: checks `ProviderMessageId` deduplication; inserts `InboundEvent`.
     - On success: marks `Processed`.
     - On transient exception: records failure, increments `AttemptCount`, calculates `NextRetryAt`. If exhausted, marks `DeadLetter`.
     - On permanent exception: immediately marks `DeadLetter` (`QuarantinePoison`).

---

## 6. Verification Plan

### Automated Tests
1. **Unit Tests**:
   - `WebhookRetryPolicyTests`: Verify backoff times, jitter bounds, and max attempts cutoff.
   - `WebhookFailureClassifierTests`: Verify exception classification into Transient, Permanent, and Poison.
   - `WebhookEventRetryTests`: Verify state transitions, replay resets, and poison quarantine.
   - `InboundEventTests`: Verify creation, invariant validation, and JSON payload serialization.
2. **Integration Tests (PostgreSQL Testcontainers in Docker)**:
   - `WebhookProcessingIntegrationTests`:
     - Normal processing creates `InboundEvent` with FK to `WebhookEvent`.
     - Duplicate provider message ID is rejected / skipped.
     - Out-of-order events retain proper timestamps.
     - Transient failure retries with backoff and preserves `Failed` state.
     - Retry exhaustion transitions to `DeadLetter`.
     - Poison event immediately transitions to `DeadLetter`.
     - Authorized replay with `Idempotency-Key` resets status and successfully processes.
     - Cross-tenant isolation ensures jobs only process current tenant's events.
3. **Quality Gates**:
   - Solution build: `dotnet build services/api/Kreyora.slnx --configuration Release ...`
   - Unit, contract, and architecture tests pass.
   - Integration tests pass against real PostgreSQL Testcontainers.
   - EF migration model check: zero pending model changes.
   - Frontend CI: `pnpm ci:frontend` passes.
   - Git hygiene: `git diff --check` clean.

