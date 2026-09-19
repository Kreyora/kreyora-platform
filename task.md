# Handoff: Milestone 07 Step 03 — Fast, Idempotent Webhook Ingress

## 1. Overview

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 03 — Fast, idempotent webhook ingress
- **Phase:** Phase 2 (Builder) Execution — Complete; Awaiting Human Approval
- **Governing Plan:** `docs/plan/M07-S03_WEBHOOK_INGRESS_PLAN.md`
- **Active Milestone File:** `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`
- **Current Checkpoint:** `artifacts/checkpoints/M07-S03.md` (REVIEW)
- **Status:** `REVIEW`

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Domain Aggregate Root & Enum (`services/api/src/Kreyora.Domain/Integrations/`)**
  - Implement `WebhookProcessingStatus` enum (`Received`, `Processing`, `Processed`, `Failed`, `DeadLetter`).
  - Implement `WebhookEvent` aggregate root inheriting `BaseEntity, ITenantOwned`.
  - Implement factory `Create(...)`, `MarkProcessing()`, `MarkProcessed(...)`, `MarkFailed(...)`, and `PurgePayload(...)` (ADR-012).
  - Add domain unit tests in `Kreyora.UnitTests/Integrations/WebhookEventTests.cs`.

- [x] **Task 2: Persistence & EF Core Migration (`services/api/src/Kreyora.Infrastructure/`)**
  - Implement `WebhookEventConfiguration.cs` in `Persistence/Configurations/`.
  - Map `webhook_events` table, columns, foreign key to `ChannelConnection`, shadow concurrency token `xmin`.
  - Unique composite index: `(ConnectionId, ProviderEventId)`.
  - Query indexes: `(TenantId, ProcessingStatus, ReceivedAt)`, `(IsPurged, ReceivedAt)`.
  - Add `DbSet<WebhookEvent> WebhookEvents` and query filter to `AppDbContext.cs`.
  - Generate migration `AddWebhookEvents` via `dotnet ef migrations add AddWebhookEvents`.
  - Verify zero pending model changes.

- [x] **Task 3: Application Contracts & Ingress Interfaces (`services/api/src/Kreyora.Application/Integrations/`)**
  - Define `IWebhookIngressService.cs` with `HandleWebhookAsync` and `HandleChallengeAsync`.
  - Define `WebhookIngressCommand`, `WebhookIngressResult`, `WebhookChallengeCommand`, `WebhookChallengeResult`.
  - Update `WebhookValidationResult` in `IntegrationContracts.cs` to include optional `ProviderEventId` and `ExternalAccountId` (fully backward compatible).

- [x] **Task 4: Infrastructure Simulator Provider & Ingress Service (`services/api/src/Kreyora.Infrastructure/Integrations/`)**
  - Implement `SimulatorChannelProvider.cs` under `Integrations/Simulator/`.
  - Implement signature validation (`sha256=valid_test_signature` or HMAC-SHA256), replay window checking, and challenge response.
  - Register `SimulatorChannelProvider` as `IChannelProvider` in `DependencyInjection.cs`.
  - Implement `WebhookIngressService.cs` implementing `IWebhookIngressService`:
    - Enforce size (256 KB) and content-type (`application/json`) limits.
    - Resolve connection via `ConnectionId` or `(Channel, ExternalAccountId)` with `.IgnoreQueryFilters()`.
    - Decrypt secret via `ISecretEncryptionService` for provider signature validation.
    - Validate signature and replay window. Reject with 401/400 on failure without creating trusted events.
    - Idempotent deduplication on `(ConnectionId, ProviderEventId)`: return 200 OK without duplicate row.
    - Durable persistence: insert `WebhookEvent` with `Received` status and save before acknowledging.
    - Measure and enforce fast path (<200ms).

- [x] **Task 5: Web API Controller (`services/api/src/Kreyora.WebApi/Controllers/`)**
  - Implement `WebhooksController.cs` under `v1/webhooks`.
  - Routes:
    - `POST /v1/webhooks/{channel}`
    - `POST /v1/webhooks/{channel}/{connectionId}`
    - `GET /v1/webhooks/{channel}`
    - `GET /v1/webhooks/{channel}/{connectionId}`
  - Attributes: `[ApiController]`, `[AllowAnonymous]`, `[ApiVersion("1.0")]`, `[RequestSizeLimit(256 * 1024)]`.
  - Attach correlation ID and redacted diagnostic logging.

- [x] **Task 6: Unit & Real Testcontainers Integration Tests**
  - Unit tests:
    - `WebhookEventTests.cs` (factory invariants, status transitions, purge).
    - `SimulatorChannelProviderTests.cs` (signatures, replay window, challenge).
  - Integration tests in `WebhookIngressIntegrationTests.cs` (PostgreSQL Testcontainers in Docker):
    - Valid webhook: returns 200/202, event stored, duration measured (<300ms).
    - Invalid signature: returns 401, zero database rows created.
    - Duplicate delivery: returns 200/202 with `isDuplicate = true`, database has exactly 1 row.
    - Oversized payload: returns 413, zero database rows created.
    - Unknown connection: returns 404, zero database rows created.
    - Replay window expired: returns 400/401, zero database rows created.
    - Challenge verification: returns raw challenge string with 200 OK.
    - Multi-tenant isolation: event stored under connection's tenant, isolated from other tenants.

- [x] **Task 7: Quality Gates & Verification**
  - Run `dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1`.
  - Verify EF migration: `dotnet ef migrations has-pending-model-changes ...`.
  - Run full test suite: `dotnet test services/api/Kreyora.slnx --configuration Release`.
  - Run frontend CI: `pnpm ci:frontend`.
  - Run git diff check: `git diff --check`.
  - Create checkpoint report `artifacts/checkpoints/M07-S03.md` (`REVIEW`).
  - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`.
