# M07-S07 — Reliability, Isolation, and Failure Campaign — Scoped Plan

## Identity

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 07 — Reliability, isolation, and failure campaign
- **Author:** Antigravity Phase 1 (Architect)
- **Date:** 2026-09-21
- **Status:** `PLANNING` — Awaiting human approval before implementation

## Objective

Execute a comprehensive, sustained reliability and failure campaign across the entire provider-neutral social integration runtime (Milestone 07). Verify fast decoupled webhook acknowledgement, duplicate storm deduplication, out-of-order monotonic status receipts, configurable latency tolerance, worker crash and restart recovery, database concurrency conflict protection (`xmin`), bounded exponential backoff with jitter, rate limit (429 feedback) handling, token expiry and reconnect lifecycles, poison payload dead-letter quarantine, idempotent replay of inbound and outbound queues, strict two-tenant concurrency isolation, and zero-leakage secret/PII protection. Fix any milestone-scoped defects discovered and produce the definitive **Integration Reliability Matrix** required for the Milestone 07 Exit Gate.

---

## Gate, Boundaries, and Scope

### Allowed

1. Create a dedicated real PostgreSQL Testcontainers integration test suite in:
   `services/api/tests/Kreyora.IntegrationTests/Integrations/Milestone07ReliabilityAndFailureTests.cs`
2. Implement 13 comprehensive reliability and failure scenarios:
   - **Scenario 1: Fast Webhook Ingress Decoupled from Downstream Processing**:
     Measure and prove that webhook acknowledgement occurs immediately upon raw event persistence (sub-100ms fast path) and never blocks on normalization, message dispatch, or background workers.
   - **Scenario 2: Duplicate Delivery Storm**:
     High-concurrency burst of identical webhook payloads (`ProviderEventId`) against the same connection. Verify exactly 1 unique `WebhookEvent` row is inserted, and all duplicate requests return fast 200/202 with `isDuplicate = true` and zero duplicate side-effects.
   - **Scenario 3: Out-of-Order Delivery & Monotonic Status Progression**:
     Inbound status receipts arriving out of sequence (`Read` before `Delivered`) and events with older timestamps. Verify monotonic status progression (`Sent -> Delivered -> Read`), no status regression, and preservation of historical `OccurredAt` timestamps.
   - **Scenario 4: High Latency & Slow Provider Simulation**:
     Execute webhook validation and outbound message dispatch under configured high latency (`X-Simulate-Latency-Ms`). Verify system stability and ensure timeouts do not corrupt message states.
   - **Scenario 5: Worker Restart / Interrupted Processing Recovery**:
     Simulate background worker crash/restart while an event is in `Processing` or message is in `Sending`. Verify that stale/interrupted tasks are recovered cleanly without duplicating logical outbound messages.
   - **Scenario 6: Database Interruption & Concurrency Collision (`xmin`)**:
     Simultaneous conflicting mutations or replays on the same message/event. Verify EF Core `DbUpdateConcurrencyException` triggers safe rollback without data corruption.
   - **Scenario 7: Provider Timeout & Exponential Retry Backoff**:
     Simulate intermittent network timeouts. Verify `WebhookFailureClassifier` marks as `Transient`, attempt count increments, and `NextRetryAt` is calculated with exponential backoff and jitter.
   - **Scenario 8: Provider Rate Limit (429 Feedback) & Outbox Retries**:
     Simulate provider HTTP 429 Too Many Requests. Verify classification as `Transient` and outbox rescheduling.
   - **Scenario 9: Token Expiry & Reconnect Lifecycle**:
     Simulate credential expiration. Verify health check transitions connection to `Expired`, outbound sends are blocked/failed safely, and reconnect restores `Active` status allowing pending queue processing to resume.
   - **Scenario 10: Poison Payload Immediate DLQ Quarantine**:
     Malformed syntax or poison payloads are immediately quarantined to `DeadLetter` with `FailureClassification.Permanent` without wasting remaining retry attempts.
   - **Scenario 11: Idempotent Replay Campaign (Inbound & Outbound)**:
     Verify replay of dead-lettered inbound webhooks and outbound messages with `Idempotency-Key` headers. Confirm state resets, redelivery occurs, and duplicate replays return existing results.
   - **Scenario 12: Two-Tenant High-Concurrency Isolation**:
     Two separate tenants (Tenant A and Tenant B) process concurrent webhook ingress, normalization jobs, outbox deliveries, and diagnostic queries simultaneously. Verify zero cross-tenant leakage across all tables, jobs, and endpoints.
   - **Scenario 13: Redacted Observability & Zero Secret Leakage (ADR-012 / ADR-013)**:
     Verify that connection secrets are never returned in plain text in any API response or log, and raw webhook payloads are strictly redacted to `"[REDACTED]"` for non-Owner roles.
3. Fix any milestone-scoped defects discovered during test execution.
4. Produce the definitive **Integration Reliability Matrix** documenting every failure mode, trigger, detection, recovery, bounds, and evidence.
5. Create checkpoint report `artifacts/checkpoints/M07-S07.md` with status `REVIEW`.

### Prohibited

- Writing real third-party channel adapters (Meta, Telegram, Viber live networks) — Milestone 08 scope.
- Introducing live external OAuth flows or live payment gateways.
- Relaxing tenant isolation, authorization policies, or database constraints.
- Modifying accepted ADRs (ADR-010 to ADR-013) without explicit architectural decision.
- Starting Milestone 08 before Milestone 07 Exit Gate approval.

---

## Scenarios & Invariant Verification Matrix

| # | Scenario | Trigger & Flow | Invariant & Evidence Required |
|---|---|---|---|
| **1** | **Fast Decoupled Ingress** | Ingress valid webhook payload | Webhook acknowledges with 200/202 immediately upon raw storage; does not block on normalization or downstream tasks. |
| **2** | **Duplicate Storm** | 10 concurrent requests with identical `ProviderEventId` | Exactly 1 `WebhookEvent` row inserted; 9 return duplicate acknowledgement (`isDuplicate = true`). Zero duplicate side effects. |
| **3** | **Monotonic Status Progression** | Receive `Read` status receipt, then delayed `Delivered` receipt | Outbound message advances `Sent -> Read`. Stale `Delivered` receipt is safely ignored; state remains `Read`. |
| **4** | **Latency & Slow Provider** | Webhook / send with `simulate_latency_ms = 100` | System respects cancellation tokens and does not lock shared threads or corrupt records. |
| **5** | **Worker Crash Recovery** | Message stuck in `Sending` or `Processing` | Interrupted task recovers safely on next batch poll without duplicating logical outbound messages. |
| **6** | **Concurrency Collision (`xmin`)** | Simultaneous replay/mutation on same entity | PostgreSQL `xmin` concurrency token detects collision and throws `DbUpdateConcurrencyException`. |
| **7** | **Transient Timeout Backoff** | Simulated network timeout during processing | Attempt count increments; `FailureClassification.Transient`; `NextRetryAt` scheduled with exponential interval + jitter. |
| **8** | **Rate Limit (429 Feedback)** | Outbox send receives 429 Too Many Requests | Classified as `Transient`; delivery attempt logged; message rescheduled for next retry interval. |
| **9** | **Token Expiry & Reconnect** | Expired credentials detected during health check | Status transitions to `Expired`; send attempts fail; reconnect/re-enable restores `Active` and resumes delivery. |
| **10** | **Poison Payload Quarantine** | Ingress payload with malformed syntax / poison flag | Immediately transitions to `DeadLetter` (`Permanent`) on first attempt without burning 5 retry attempts. |
| **11** | **Idempotent Replay** | Replay dead-lettered webhook & outbound message | Status resets to `Received`/`Queued`; attempts reset; redelivery succeeds; replay action audited with `Idempotency-Key`. |
| **12** | **Two-Tenant Concurrency** | Concurrent ingress and jobs across Tenant A & Tenant B | Zero cross-tenant data leakage; tenant query filters strictly enforced across all operations. |
| **13** | **Redacted Observability** | Query connection details & webhook details as Operator vs Owner | Credentials never returned (ADR-013); `RawPayload` redacted to `"[REDACTED]"` for Operator, visible for Owner (ADR-012). |

---

## Implementation Tasks

- [ ] **Task 1: Create Sustained Reliability & Failure Test Suite (`services/api/tests/Kreyora.IntegrationTests/Integrations/`)**
  - Implement `Milestone07ReliabilityAndFailureTests.cs` using `PostgresFixture`.
  - Add test methods for all 13 scenarios against real Testcontainers PostgreSQL.
- [ ] **Task 2: Fix Any Milestone-Scoped Defects Discovered**
  - Address any concurrency, timing, state machine, or tenant boundary defects exposed during sustained testing.
- [ ] **Task 3: Produce Definitive Integration Reliability Matrix**
  - Document all 13 failure modes in `docs/architecture/INTEGRATION_RELIABILITY_MATRIX.md` and in the checkpoint report.
- [ ] **Task 4: Run Full Quality Gates**
  - Backend solution build Release (`0 Warning(s), 0 Error(s)`).
  - All backend tests passing (`dotnet test services/api/Kreyora.slnx`).
  - Zero pending EF Core model changes (`dotnet ef migrations has-pending-model-changes`).
  - Frontend CI passes (`pnpm ci:frontend`).
  - Git diff check clean (`git diff --check`).
- [ ] **Task 5: Checkpoint & Exit Gate Review**
  - Create checkpoint report `artifacts/checkpoints/M07-S07.md` with status `REVIEW`.
  - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`.

---

## Verification Plan

### Automated Tests
- Sustained Campaign Test Suite:
  ```bash
  dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Milestone07ReliabilityAndFailureTests"
  ```
- Full Backend Solution:
  ```bash
  dotnet test services/api/Kreyora.slnx --configuration Release --no-build
  ```
- Frontend CI:
  ```bash
  pnpm ci:frontend
  ```
- EF Migration Validation:
  ```bash
  dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --no-build
  ```

