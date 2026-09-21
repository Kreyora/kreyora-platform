# Handoff: Milestone 07 Step 06 — Diagnostics API/UI and Provider Simulator

## 1. Overview

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 06 — Diagnostics API/UI and provider simulator
- **Phase:** Phase 2 (Builder) Execution Complete — Awaiting Review
- **Governing Plan:** `docs/plan/M07-S06_DIAGNOSTICS_SIMULATOR_PLAN.md`
- **Active Milestone File:** `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`
- **Previous Checkpoint:** `artifacts/checkpoints/M07-S05.md` (APPROVED)
- **Current Checkpoint:** `artifacts/checkpoints/M07-S06.md` (REVIEW)
- **Status:** `REVIEW`

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Simulator Channel Provider Enhancements (`services/api/src/Kreyora.Infrastructure/Integrations/Simulator/`)**
  - Add signature generation helper `GenerateValidSignature(byte[] body, string? secret)` using HMAC-SHA256.
  - Support configurable latency delay via metadata/header `X-Simulate-Latency-Ms`.
  - Update `ValidateOrRefreshConnectionAsync` to evaluate simulated token expiry:
    - If connection status is `Expired` or account ID has `simulate_expired`, return `ConnectionHealthResult.Failed(ChannelConnectionStatus.Expired, "Token has expired.")`.
    - If connection has `simulate_degraded`, return `ConnectionHealthResult.Degraded("Intermittent provider errors detected.")`.
    - Otherwise return `ConnectionHealthResult.Healthy()`.
  - Add helper to create synthetic webhook events for scenarios (signed inbound, duplicate provider ID, out-of-order timestamps, poison).

- [x] **Task 2: Application Contracts & Service Interface (`services/api/src/Kreyora.Application/Integrations/`)**
  - Add `IIntegrationDiagnosticsService.cs`.
  - Add DTOs in `IntegrationDiagnosticsContracts.cs`:
    - `IntegrationOverviewDto`
    - `ConnectionDiagnosticsDto`
    - `WebhookEventDto`
    - `WebhookEventDetailDto`
    - `SimulatorScenarioRequest`
    - `SimulatorScenarioResult`
    - `SimulatorScenarioType` enum

- [x] **Task 3: Infrastructure Diagnostics Service (`services/api/src/Kreyora.Infrastructure/Integrations/`)**
  - Implement `IntegrationDiagnosticsService.cs`:
    - `GetOverviewAsync`: Count connections, 24h event stats, DLQ counts.
    - `GetConnectionDiagnosticsAsync`: Compute connection-specific metrics.
    - `GetConnectionWebhooksAsync`: Paginated query on `WebhookEvents` filtered by `connectionId`.
    - `GetWebhookDetailAsync`: Fetch single event; inspect caller role via `ITenantContextAccessor` and redact `RawPayload` to `"[REDACTED]"` if not `Owner` or `PlatformSupport`.
    - `ExecuteSimulatorScenarioAsync`: Dispatch scenario via `IWebhookIngressService`, `IWebhookProcessingService`, or `IChannelConnectionService`.
  - Register `IIntegrationDiagnosticsService` in `DependencyInjection.cs`.

- [x] **Task 4: Web API Controller (`services/api/src/Kreyora.WebApi/Controllers/`)**
  - Expand `IntegrationDiagnosticsController.cs` with the new diagnostic and scenario endpoints:
    - `GET /v1/integrations/diagnostics/overview` (`IntegrationsRead`)
    - `GET /v1/integrations/connections/{id}/diagnostics` (`IntegrationsRead`)
    - `GET /v1/integrations/connections/{id}/webhooks` (`IntegrationsRead`)
    - `GET /v1/integrations/webhooks/{id}` (`IntegrationsRead`)
    - `POST /v1/integrations/simulator/scenarios` (`IntegrationsWrite`, anti-forgery, requires `Idempotency-Key`)
    - Preserve existing `dead-letter` and `replay` endpoints.
  - Enforce `[RequireTenantContext]`, RFC 7807 problem details, audit logging.

- [x] **Task 5: Frontend API Adapter & UI Integration (`apps/web/`)**
  - Update `apps/web/src/lib/ports/integration-client.ts` with `replayWebhook` and `reconnect`.
  - Update `apps/web/src/lib/adapters/mock/mock-integration-client.ts` to implement new methods.
  - Create `apps/web/src/lib/adapters/api/integration-client.ts` calling real backend endpoints via `fetchWithAuth`.
  - Export `apiIntegrationClient` from `apps/web/src/lib/adapters/api/index.ts`.
  - Update `apps/web/src/lib/providers/client-provider.tsx` to toggle `integration: USING_FIXTURE_ADAPTERS ? mockIntegrationClient : apiIntegrationClient`.
  - Update `apps/web/src/app/(seller)/integrations/[id]/page.tsx` to handle real replay, reconnect, loading states, and Viewer role guards.

- [x] **Task 6: Unit & Real PostgreSQL Integration Tests**
  - Unit tests in `Kreyora.UnitTests/Integrations/`:
    - Simulator signature generation, latency, scenarios (10 tests).
  - Real Testcontainers integration tests in `IntegrationDiagnosticsIntegrationTests.cs`:
    - Scenario 1: Healthy connection & webhook processing updates metrics.
    - Scenario 2: Degraded connection simulation returns degraded diagnostic status.
    - Scenario 3: Token expiry simulation returns `Expired`; reconnect restores `Active`.
    - Scenario 4: Rate-limited simulation triggers 429 backoff.
    - Scenario 5: Poison payload simulation immediately enters DLQ.
    - Scenario 6: Replay of dead-lettered event succeeds with audit trail.
    - Scenario 7: Non-Owner role receives redacted payload.
    - Scenario 8: Cross-tenant isolation verification.
    - Scenario 9: Duplicate delivery deduplication verification.
  - Frontend Vitest tests in `api-integration-client.test.ts` (4 tests).

- [x] **Task 7: Quality Gates & Verification**
  - Solution build Release clean (`0 Warning(s), 0 Error(s)`).
  - All backend tests passing (`dotnet test`: 523 passed).
  - Zero pending EF migrations (`has-pending-model-changes`).
  - Frontend CI passes (`pnpm ci:frontend`: 458 passed, build succeeded).
  - Git diff clean (`git diff --check`).
  - Create checkpoint `artifacts/checkpoints/M07-S06.md`.
