# Integration Reliability Matrix

## 1. Overview and Purpose

The Kreyora Social Integration Runtime (Milestone 07) provides a provider-neutral, fault-tolerant, and strictly isolated foundation for connecting external social messaging channels (Meta/Instagram/Messenger, Telegram, Viber, WhatsApp) into Kreyora's multi-tenant social-commerce operating system.

Before coupling the platform to any third-party network adapter (Milestone 08), the runtime underwent a sustained reliability, isolation, and failure campaign (M07-S07). All scenarios were executed against real PostgreSQL 16 instances via Testcontainers in `Milestone07ReliabilityAndFailureTests.cs`.

This matrix documents the 13 verified failure modes, system behaviors, boundaries, recovery mechanisms, and automated test evidence.

---

## 2. Definitive Reliability Matrix

| # | Scenario / Failure Mode | Trigger & Condition | System Detection & Classification | Invariant & Bound | Recovery & Observability | Test Evidence |
|---|---|---|---|---|---|---|
| **1** | **Fast Decoupled Ingress** | Ingress valid webhook request from external provider. | Webhook signature verified; `WebhookEvent` persisted synchronously as `Received`. | **Sub-100ms fast path acknowledgement.** Downstream normalization (`InboundEvent`), AI tools, or outbox sends never block HTTP response. | Responds `202 Accepted` with correlation ID and event ID. Downstream processing scheduled asynchronously via Hangfire/worker. | `Scenario01_FastDecoupledIngress_AcknowledgesImmediatelyWithoutDownstreamBlock` |
| **2** | **Duplicate Delivery Storm** | High-concurrency burst of identical webhook payloads (`ProviderEventId`) on the same connection. | 1. Ingress deduplication query (`e.ConnectionId == conn.Id && e.ProviderEventId == id`).<br>2. Unique DB index `(ConnectionId, ProviderEventId)` with `DbUpdateException` race catch. | **Exactly 1 row persisted in database.** All duplicate requests receive fast 200/202 responses with `isDuplicate: true`. Zero duplicate side-effects. | No unhandled 500 collisions; duplicate events logged with latency telemetry; zero redundant background jobs triggered. | `Scenario02_DuplicateDeliveryStorm_HighConcurrency_ExactlyOneCreatedAndOthersDeduplicated` |
| **3** | **Out-of-Order Status Progression** | Inbound `Read` status receipt arrives before a delayed `Delivered` receipt for an outbound message. | `OutboundMessage.UpdateProviderStatus(status, timestamp)` inspects state machine rules. | **Monotonic status progression (`Sent -> Delivered -> Read`).** Status never regresses backwards from `Read` to `Delivered`. | Message retains `Read` status; stale `Delivered` receipt safely discarded without error; accurate event timeline preserved. | `Scenario03_OutofOrderDelivery_MonotonicStatusProgression_PreservesReadAndIgnoresStaleDelivered` |
| **4** | **Latency & Slow Provider** | Provider response delayed beyond threshold (`X-Simulate-Latency-Ms`). | `CancellationToken` cancellation triggered after timeout during `SendMessageAsync`. | **System stability under latency.** Thread pool protected; state transitions safely to `Failed` (`Transient`) or remains in-flight without record corruption. | Request cleans up cleanly; transient timeout recorded with retry backoff scheduled; no orphaned connection leaks. | `Scenario04_LatencyAndSlowProvider_RespectsCancellationAndDoesNotCorruptState` |
| **5** | **Worker Crash & Restart Recovery** | Worker process abruptly crashes or terminates while message is in `Sending` or event is in `Processing`. | Worker restart / poll cycle identifies interrupted tasks with expired lease or transient failure. | **Zero message loss; no duplicate logical sends.** Idempotency keys prevent duplicate physical transmissions upon restart. | Re-enqueued message successfully processed and sent to provider on subsequent worker loop; state reaches `Sent`. | `Scenario05_WorkerRestart_InterruptedSendingOrProcessing_RecoversCleanlyWithoutDuplication` |
| **6** | **Database Concurrency Collision (`xmin`)** | Simultaneous conflicting mutations on the same message or event by concurrent processes. | PostgreSQL system column `xmin` mapped as EF Core concurrency token. | **Optimistic concurrency enforcement.** First committer succeeds; concurrent stale commit aborted via `DbUpdateConcurrencyException`. | Database state protected from lost updates; caller receives standard conflict error with automatic rollback. | `Scenario06_DatabaseConcurrency_ConflictingMutations_TriggersXminConcurrencyException` |
| **7** | **Transient Timeout Backoff** | Intermittent socket timeout, connection reset, or 5xx error from provider network. | `WebhookFailureClassifier.Classify(ex)` detects transient network exception. | **Bounded exponential retries with full jitter.** Attempt count increments up to `MaxAttempts` (default: 5). | `NextRetryAt` scheduled with exponential interval `now + 2^(attempt-1) * base + jitter`. Background worker retries at scheduled time. | `Scenario07_TransientTimeout_ExponentialBackoffAndJitter_IncrementsAttemptCount` |
| **8** | **Rate Limit (429 Feedback)** | External channel provider returns HTTP 429 Too Many Requests or rate-limit payload. | Provider send result reports `429` error code; classified as `Transient`. | **Outbox retry scheduling.** Delivery attempt recorded with `429` error details; message rescheduled for next retry interval. | Logged with tenant context; backoff allows rate limit window to reset before next dispatch attempt. | `Scenario08_RateLimit429_OutboxSend_ClassifiedAsTransientAndRescheduled` |
| **9** | **Token Expiry & Reconnect Lifecycle** | Provider token expires (`simulate_expired` or invalid credentials). | 1. Health check transitions connection to `Expired`.<br>2. Front-door `QueueMessageAsync` rejects new sends.<br>3. Pre-queued sends fail safely. | **Zero unauthorized message delivery.** Messages cannot be queued or dispatched while credentials are in `Expired` status. | Connection health status reflects `Expired`; Reconnect action refreshes credentials and restores `Active` status; pending queue resumes delivery. | `Scenario09_TokenExpiryAndReconnect_HealthCheckTransitionsToExpired_ReconnectRestoresActive` |
| **10** | **Poison Payload Quarantine** | Ingress message contains malformed JSON, unsupported schema, or unparsable payload structure. | `WebhookProcessingService` catches schema validation or deserialization failure; classified as `Permanent`. | **Immediate DLQ quarantine.** Quarantined on Attempt 1 without wasting remaining 4 retry attempts. | `WebhookEvent` transitions to `DeadLetter` with `FailureClassification.Permanent` and `DeadLetteredAt` timestamp; visible in operator diagnostics. | `Scenario10_PoisonPayload_QuarantinesToDeadLetterImmediatelyWithoutBurningRetries` |
| **11** | **Idempotent Replay (Inbound & Outbound)** | Operator triggers manual replay of dead-lettered inbound event or outbound message. | Replay endpoint validates permission (`IntegrationsWrite`), verifies DLQ status, and requires `Idempotency-Key`. | **Audited, idempotent replay.** State resets (`Received` or `Queued`), attempt counters reset, and audit trail record emitted. | Replayed item picked up and processed successfully; duplicate replays with identical key return existing state without duplicate jobs. | `Scenario11_IdempotentReplay_InboundAndOutbound_ResetsStateAndAuditsReplay` |
| **12** | **Two-Tenant High Concurrency** | Tenant A and Tenant B concurrently perform webhook ingress, normalization, outbox delivery, and diagnostics. | Global query filters on `TenantId`; `TenantContextAccessor` ambient scoping; `ITenantJobRunner` context injection. | **Absolute tenant isolation.** Zero cross-tenant data leakage across tables, background jobs, queues, and API queries. | Tenant A operations only process Tenant A data; Tenant B queues and diagnostics remain completely untouched and isolated. | `Scenario12_TwoTenantConcurrency_StrictIsolationAcrossIngressJobsAndDiagnostics` |
| **13** | **Redacted Observability (ADR-012 / ADR-013)** | Inspection of connection credentials and webhook payloads across roles (`Owner`, `Operator`, `Viewer`). | `IntegrationDiagnosticsService` role evaluation; `AesGcmSecretEncryptionService` zero raw-secret policy. | **Zero plain-text secret exposure.** Connection credentials never returned in any API or log. Raw payloads redacted to `"[REDACTED]"` for non-Owner roles. | `Owner` and read-only `PlatformSupport` can inspect unredacted raw payload for debugging; all other roles receive `"[REDACTED]"`. Credentials remain encrypted at rest. | `Scenario13_RedactedObservability_Adr012AndAdr013_NoSecretsExposedAndPayloadRedactedForNonOwner` |

---

## 3. Milestone 07 Architectural Invariants Validated

1. **Decoupled Webhook Ingress (ADR-010 / M07-S03)**:
   - Webhook ingress validates cryptographic signatures, resolves connection identity, and persists immutable `WebhookEvent` rows synchronously.
   - Acknowledgement (`202 Accepted`) is returned immediately and never awaits conversational logic, database transactions in other domains, or third-party downstream APIs.

2. **Durable Idempotency & Duplicate Protection**:
   - Webhooks are deduplicated at both the application layer (fast cache/query) and the database layer (PostgreSQL unique composite index on `(ConnectionId, ProviderEventId)`).
   - Race conditions under concurrent storms are caught safely via `DbUpdateException` and return positive duplicate acknowledgements (`isDuplicate: true`).

3. **Multi-Tenant Scoping & Security (ADR-003 / ADR-010)**:
   - Every background job runs within an explicit `ITenantJobRunner` scope that enforces `TenantId` isolation across all queries and commands.
   - No job or diagnostic endpoint can view, modify, or leak data across tenant boundaries.

4. **Secret & Payload Confidentiality (ADR-012 / ADR-013)**:
   - Credentials (access tokens, app secrets) are encrypted using AES-256-GCM with versioned keys and are never logged or returned over public/seller APIs.
   - Raw webhook payloads containing PII (names, phone numbers) are restricted and redacted for non-Owner roles in diagnostics views.

5. **Bounded Failure Recovery & Dead-Letter Handling (M07-S04 / M07-S05)**:
   - Transient failures follow an exponential backoff formula with jitter: \(t_{retry} = 2^{\text{attempt} - 1} \times t_{\text{base}} + \text{jitter}\).
   - Permanent failures (poison payloads, unparseable schemas, inactive connections) bypass retries and are immediately quarantined into `DeadLetter`.
   - Both inbound and outbound dead-lettered items support safe, audited, idempotent replay.

