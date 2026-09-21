# M07-S06 — Diagnostics API/UI and Provider Simulator — Scoped Plan

## Identity

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 06 — Diagnostics API/UI and provider simulator
- **Author:** Antigravity Phase 1 (Architect)
- **Date:** 2026-09-21
- **Status:** `PLANNING` — Awaiting human approval before implementation

## Objective

Deliver a deterministic provider simulator and comprehensive integration diagnostics runtime. The simulator supports signed inbound events, duplicate/out-of-order delivery, configurable latency, rate limits (429 feedback), transient/permanent errors, token expiry, delivery receipts, and reconnect behaviors without external dependencies. Expose authorized connection health, webhook events, delivery attempts, DLQ, and replay APIs with strict role-based payload redaction (ADR-012/013). Connect the Milestone 01 frontend integration diagnostics UI to live backend runtime data while preserving demo mode via fixture adapters.

## Scope

### Allowed

- **Domain / Simulator Extensions**:
  - Enhance `SimulatorChannelProvider` with deterministic scenario triggers: configurable latency, duplicate/out-of-order delivery simulation, rate limit (429) simulation, token expiry and reconnect behavior simulation.
  - Helper methods for generating signed inbound webhook payloads (HMAC-SHA256) and simulation payloads.
- **Application Contracts & DTOs**:
  - `IIntegrationDiagnosticsService` defining diagnostic aggregation, connection health metrics, webhook event querying with role-based redaction, and simulator scenario triggers.
  - DTOs: `IntegrationOverviewDto`, `ConnectionDiagnosticsDto`, `WebhookEventDto`, `WebhookEventDetailDto`, `SimulatorScenarioRequest`, `SimulatorScenarioResult`.
- **Infrastructure Services**:
  - Implement `IntegrationDiagnosticsService` implementing `IIntegrationDiagnosticsService`.
  - Enforce role-based payload visibility: `Owner` and `PlatformSupport` can inspect `RawPayload`; `Admin`, `Operator`, `Viewer` receive `"[REDACTED]"` per ADR-012.
  - Compute 24-hour event/failure counts, DLQ counts, and health status dynamically from database records.
- **Web API Endpoints (`IntegrationDiagnosticsController`)**:
  - `GET /v1/integrations/diagnostics/overview`: Aggregated integration status across connections and queues (`IntegrationsRead`).
  - `GET /v1/integrations/connections/{id}/diagnostics`: Connection health metrics, event counts (24h), token expiry (`IntegrationsRead`).
  - `GET /v1/integrations/connections/{id}/webhooks`: Paginated webhook event history for a connection (`IntegrationsRead`).
  - `GET /v1/integrations/webhooks/{id}`: Single webhook event detail with role-based redaction (`IntegrationsRead`).
  - `POST /v1/integrations/simulator/scenarios`: Deterministic test scenario runner for live verification (`IntegrationsWrite`, anti-forgery, requires `Idempotency-Key`).
  - Retain existing endpoints: `GET /v1/integrations/webhooks/dead-letter` and `POST /v1/integrations/webhooks/{id}/replay`.
- **Frontend Ports & Adapters (`apps/web`)**:
  - Expand `apps/web/src/lib/ports/integration-client.ts` with `replayWebhook` and `reconnect`.
  - Implement `apiIntegrationClient` in `apps/web/src/lib/adapters/api/integration-client.ts` calling real backend endpoints.
  - Update `apps/web/src/lib/providers/client-provider.tsx` to conditionally bind `apiIntegrationClient` when `USING_FIXTURE_ADAPTERS` is false.
  - Update `apps/web/src/app/(seller)/integrations/page.tsx` and `[id]/page.tsx` to use live data, real replay with auto-generated idempotency keys, real reconnect, and role-based action guards (`ViewerBadge`).
- **Tests**:
  - Unit tests for simulator scenarios, payload redaction by role, and diagnostics calculation.
  - Real PostgreSQL Testcontainers integration tests in `IntegrationDiagnosticsIntegrationTests.cs` proving:
    1. Healthy scenario (connection valid, events processed, stats increment).
    2. Degraded scenario (intermittent failure simulation updates status).
    3. Expired scenario (token expired health check returns `Expired`, reconnect restores `Active`).
    4. Rate-limited scenario (429 feedback simulation schedules backoff).
    5. Failed & DLQ scenario (poison or exhausted retry moves to DLQ).
    6. Replay scenario (authorized replay with idempotency key resets and reprocesses event).
    7. Role-based payload redaction (Owner sees payload, Operator/Viewer receives `[REDACTED]`).
    8. Multi-tenant isolation (Tenant B cannot query Tenant A diagnostics, webhooks, or DLQ).
  - Frontend Vitest tests verifying `apiIntegrationClient` and updated page interactions.

### Prohibited

- Real third-party social API adapters (Meta, Telegram, Viber live networks).
- Conversation aggregate or AI orchestration models (deferred to M08 and M09).
- Exposing unencrypted secrets or credentials in any API response or log (ADR-013 invariant).
- Removing demo mode / fixture adapter fallback in frontend.
- Starting Step 07 before Step 06 approval.

---

## Architectural Decisions

1. **Role-Based Webhook Payload Redaction (ADR-012 Enforced)**:
   - Raw webhook payloads may contain customer PII.
   - When retrieving webhook event details via diagnostics APIs:
     - `TenantRole.Owner` or `TenantRole.PlatformSupport`: returns original `RawPayload`.
     - All other roles (`Admin`, `Operator`, `Viewer`): returns `"[REDACTED]"`.
   - Raw secrets are never stored in `RawPayload` or returned in any DTO (ADR-013).

2. **Deterministic Simulator Scenario Trigger**:
   - To prove the required review checkpoint (*demonstrate healthy, degraded, expired, rate-limited, failed, and replayed scenarios without a live provider*), the system exposes `POST /v1/integrations/simulator/scenarios`.
   - This endpoint dispatches deterministic synthetic events through the real ingress pipeline (`IWebhookIngressService`) and health validation pipeline (`IChannelConnectionService`), generating real database records and state transitions.

3. **Frontend Hybrid Architecture**:
   - `apps/web` retains `mockIntegrationClient` when `NEXT_PUBLIC_API_URL` is omitted (`USING_FIXTURE_ADAPTERS = true`).
   - When `NEXT_PUBLIC_API_URL` is present, `apiIntegrationClient` connects seamlessly to the ASP.NET Core API via `fetchWithAuth`.

---

## Contract & API Specifications

### New Application Contracts (`Kreyora.Application/Integrations/`)

```csharp
public interface IIntegrationDiagnosticsService
{
    Task<Result<IntegrationOverviewDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ConnectionDiagnosticsDto>> GetConnectionDiagnosticsAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<WebhookEventDto>>> GetConnectionWebhooksAsync(
        string connectionId,
        int page,
        int pageSize,
        WebhookProcessingStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<Result<WebhookEventDetailDto>> GetWebhookDetailAsync(
        string webhookEventId,
        CancellationToken cancellationToken = default);

    Task<Result<SimulatorScenarioResult>> ExecuteSimulatorScenarioAsync(
        SimulatorScenarioRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record IntegrationOverviewDto(
    int TotalConnections,
    int ActiveConnections,
    int DegradedConnections,
    int ExpiredConnections,
    int EventsProcessed24h,
    int EventsFailed24h,
    int InboundDeadLetterCount,
    int OutboundDeadLetterCount,
    DateTimeOffset ComputedAt);

public sealed record ConnectionDiagnosticsDto(
    string ConnectionId,
    ChannelType Channel,
    string DisplayName,
    ChannelConnectionStatus Status,
    bool IsHealthy,
    string? HealthSummary,
    string? LastErrorMessage,
    DateTimeOffset? LastHealthCheckAt,
    DateTimeOffset? TokenExpiresAt,
    string WebhookUrl,
    int EventsProcessed24h,
    int EventsFailed24h,
    int DeadLetterCount,
    DateTimeOffset? LastEventAt);

public sealed record WebhookEventDto(
    string Id,
    string ConnectionId,
    ChannelType Channel,
    string ProviderEventId,
    string? EventType,
    WebhookProcessingStatus Status,
    int AttemptCount,
    int MaxAttempts,
    WebhookFailureClassification? FailureClassification,
    string? ErrorMessage,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? DeadLetteredAt);

public sealed record WebhookEventDetailDto(
    string Id,
    string ConnectionId,
    ChannelType Channel,
    string ProviderEventId,
    string? EventType,
    WebhookProcessingStatus Status,
    int AttemptCount,
    int MaxAttempts,
    WebhookFailureClassification? FailureClassification,
    string? ErrorMessage,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? DeadLetteredAt,
    string RawPayload,
    bool IsPayloadRedacted,
    string CorrelationId);

public enum SimulatorScenarioType
{
    HealthyInbound = 1,
    DuplicateDelivery = 2,
    OutOfOrderDelivery = 3,
    RateLimit429 = 4,
    TransientFailure = 5,
    PermanentPoison = 6,
    TokenExpiry = 7,
    Reconnect = 8,
    DeliveryReceipt = 9
}

public sealed record SimulatorScenarioRequest(
    string ConnectionId,
    SimulatorScenarioType Scenario,
    int? LatencyMs = null,
    string? CustomPayload = null);

public sealed record SimulatorScenarioResult(
    SimulatorScenarioType Scenario,
    bool Succeeded,
    string Summary,
    string? GeneratedEventId,
    string? CorrelationId,
    DateTimeOffset ExecutedAt);
```

### Web API Endpoints

| Method | Path | Policy | CSRF | Idempotency | Description |
|---|---|---|:---:|:---:|---|
| `GET` | `/v1/integrations/diagnostics/overview` | `IntegrationsRead` | No | No | Tenant-level diagnostics summary |
| `GET` | `/v1/integrations/connections/{id}/diagnostics` | `IntegrationsRead` | No | No | Detailed health and 24h metrics for connection |
| `GET` | `/v1/integrations/connections/{id}/webhooks` | `IntegrationsRead` | No | No | Paginated webhook events for connection |
| `GET` | `/v1/integrations/webhooks/{id}` | `IntegrationsRead` | No | No | Single webhook event detail with role redaction |
| `POST` | `/v1/integrations/simulator/scenarios` | `IntegrationsWrite` | Yes | Header | Trigger deterministic simulator scenario |
| `GET` | `/v1/integrations/webhooks/dead-letter` | `IntegrationsRead` | No | No | Retained from S04 |
| `POST` | `/v1/integrations/webhooks/{id}/replay` | `IntegrationsWrite` | Yes | Header | Retained from S04 |

---

## Implementation Tasks

- [ ] **Task 1: Simulator Channel Provider Enhancements (`services/api/src/Kreyora.Infrastructure/Integrations/Simulator/`)**
  - Add signature generation helper `GenerateValidSignature(byte[] body, string? secret)` using HMAC-SHA256.
  - Support configurable latency delay via metadata/header `X-Simulate-Latency-Ms`.
  - Update `ValidateOrRefreshConnectionAsync` to evaluate simulated token expiry:
    - If connection status is `Expired` or account ID has `simulate_expired`, return `ConnectionHealthResult.Failed(ChannelConnectionStatus.Expired, "Token has expired.")`.
    - If connection has `simulate_degraded`, return `ConnectionHealthResult.Degraded("Intermittent provider errors detected.")`.
    - Otherwise return `ConnectionHealthResult.Healthy()`.
  - Add helper to create synthetic webhook events for scenarios (signed inbound, duplicate provider ID, out-of-order timestamps, poison).

- [ ] **Task 2: Application Contracts & Service Interface (`services/api/src/Kreyora.Application/Integrations/`)**
  - Add `IIntegrationDiagnosticsService.cs`.
  - Add DTOs in `IntegrationDiagnosticsContracts.cs`.

- [ ] **Task 3: Infrastructure Diagnostics Service (`services/api/src/Kreyora.Infrastructure/Integrations/`)**
  - Implement `IntegrationDiagnosticsService.cs`:
    - `GetOverviewAsync`: Count connections, 24h event stats, DLQ counts.
    - `GetConnectionDiagnosticsAsync`: Compute connection-specific metrics.
    - `GetConnectionWebhooksAsync`: Paginated query on `WebhookEvents` filtered by `connectionId`.
    - `GetWebhookDetailAsync`: Fetch single event; inspect caller role via `ITenantContextAccessor` and redact `RawPayload` to `"[REDACTED]"` if not `Owner` or `PlatformSupport`.
    - `ExecuteSimulatorScenarioAsync`: Dispatch scenario via `IWebhookIngressService`, `IWebhookProcessingService`, or `IChannelConnectionService`.
  - Register `IIntegrationDiagnosticsService` in `DependencyInjection.cs`.

- [ ] **Task 4: Web API Controller (`services/api/src/Kreyora.WebApi/Controllers/`)**
  - Expand `IntegrationDiagnosticsController.cs` with the new diagnostic and scenario endpoints.
  - Enforce `[RequireTenantContext]`, `[Authorize(Policy = TenantPermissions.IntegrationsRead / IntegrationsWrite)]`, `[ValidateAntiForgeryToken]`, RFC 7807 problem details.

- [ ] **Task 5: Frontend API Adapter & UI Integration (`apps/web/`)**
  - Update `apps/web/src/lib/ports/integration-client.ts` with `replayWebhook` and `reconnect`.
  - Update `apps/web/src/lib/adapters/mock/mock-integration-client.ts` to implement new methods.
  - Create `apps/web/src/lib/adapters/api/integration-client.ts` calling real backend endpoints via `fetchWithAuth`.
  - Export `apiIntegrationClient` from `apps/web/src/lib/adapters/api/index.ts`.
  - Update `apps/web/src/lib/providers/client-provider.tsx` to toggle `integration: USING_FIXTURE_ADAPTERS ? mockIntegrationClient : apiIntegrationClient`.
  - Update `apps/web/src/app/(seller)/integrations/[id]/page.tsx` to handle real replay, reconnect, loading states, and Viewer role guards.

- [ ] **Task 6: Unit & Real PostgreSQL Integration Tests**
  - Unit tests:
    - Simulator signature generation and latency handling.
    - Role-based redaction logic.
    - Scenario request validation.
  - Real Testcontainers integration tests in `IntegrationDiagnosticsIntegrationTests.cs`:
    - Scenario 1: Healthy connection & webhook processing updates metrics.
    - Scenario 2: Degraded connection simulation returns degraded diagnostic status.
    - Scenario 3: Token expiry simulation returns `Expired`; reconnect restores `Active`.
    - Scenario 4: Rate-limited simulation triggers 429 backoff.
    - Scenario 5: Poison payload simulation immediately enters DLQ.
    - Scenario 6: Replay of dead-lettered event succeeds with audit trail.
    - Scenario 7: Non-Owner role receives redacted payload.
    - Scenario 8: Cross-tenant isolation verification.
  - Frontend unit tests for `apiIntegrationClient`.

- [ ] **Task 7: Quality Gates & Verification**
  - Full backend build Release (`0 Warning(s), 0 Error(s)`).
  - All backend tests passing (`dotnet test`).
  - EF Core migration check (`has-pending-model-changes` clean).
  - Frontend CI passes (`pnpm ci:frontend`).
  - Git diff check clean (`git diff --check`).
  - Create checkpoint report `artifacts/checkpoints/M07-S06.md`.

---

## Verification Plan

### Automated Tests

1. **Backend Integration Tests**:
   ```bash
   dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --filter "FullyQualifiedName~IntegrationDiagnosticsIntegrationTests" --configuration Release --no-build
   ```
2. **Full Backend Solution**:
   ```bash
   dotnet test services/api/Kreyora.slnx --configuration Release --no-build
   ```
3. **Frontend CI**:
   ```bash
   pnpm ci:frontend
   ```

### Manual Verification Procedure

1. Verify `GET /v1/integrations/diagnostics/overview` returns live counts of connections, 24h events, and dead-letter queues.
2. Execute `POST /v1/integrations/simulator/scenarios` with `HealthyInbound` and confirm new `WebhookEvent` and `InboundEvent` are created.
3. Execute `TokenExpiry` scenario and verify connection status becomes `Expired`.
4. Execute `Reconnect` scenario and verify connection status restores to `Active`.
5. Authenticate as `Operator` and call `GET /v1/integrations/webhooks/{id}`; verify `rawPayload` is `"[REDACTED]"`.
6. Authenticate as `Owner` and verify `rawPayload` contains full body.
7. Open `/integrations` in browser and confirm live connections, health badges, event tables, and replay work properly.

