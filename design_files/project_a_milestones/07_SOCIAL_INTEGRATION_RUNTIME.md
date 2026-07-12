# Milestone 07 — Provider-Neutral Social Integration Runtime

## Objective

Build the reliable backend foundation for connecting social media providers without yet coupling the product to a particular API. Implement encrypted connections, capability discovery, durable webhook ingestion, event normalization, outbound outbox, retries, dead-letter handling, replay, health diagnostics, and a realistic provider simulator.

## Dependencies

- Milestone 06 exit gate approved.
- A secrets-encryption/key-management approach is accepted by ADR.
- Provider selection may still be pending; no provider-specific behavior is fabricated.

## Implementation design

Webhook processing follows: validate request → durably store immutable raw event/idempotency identity → acknowledge quickly → normalize/process asynchronously → create conversation effects → enqueue outbound response separately.

`IChannelProvider` exposes only capabilities evidenced across providers: connection validation/refresh, webhook validation, inbound normalization, outbound messaging, capability discovery, and health. Provider-specific IDs and payloads stay at the integration boundary.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Provider capability model and integration ADRs | `NOT STARTED` |
| 02 | Connection model and encrypted-secret lifecycle | `NOT STARTED` |
| 03 | Fast, idempotent webhook ingress | `NOT STARTED` |
| 04 | Normalization, processing jobs, retry, DLQ, and replay | `NOT STARTED` |
| 05 | Outbound outbox and delivery attempts | `NOT STARTED` |
| 06 | Diagnostics API/UI and provider simulator | `NOT STARTED` |
| 07 | Reliability, isolation, and failure campaign | `NOT STARTED` |

## Prompt 01 — Provider capability model and integration ADRs

> Define the provider-neutral channel boundary before implementing code. Produce a capability matrix for inbound text/media, outbound text/media/link, templates, reactions, delivery/read receipts, identity fields, conversation windows, token refresh, webhook verification, rate limits, and sandbox/production approval. Mark every cell `verified`, `unsupported`, or `unknown` with evidence requirements; do not guess. Write ADRs for connection ownership, normalized-event versioning, provider payload retention, and secrets encryption. Then implement only the capability/value objects and `IChannelProvider` contracts approved by those ADRs, with contract tests using fakes.

**Review checkpoint:** approve capability vocabulary and ADRs before persistence or webhooks.

## Prompt 02 — Connection model and encrypted-secret lifecycle

> Implement ChannelConnection with tenant/store ownership as approved, provider/account identity, encrypted credential envelope, granted scopes/capabilities, connection status, expiry/refresh metadata, last validation, and health summary. Use an encryption abstraction with development keys separated from production key configuration; never return secrets through APIs or logs. Implement authorized create/update/disable/delete metadata flows without a live OAuth exchange. Add audit, rotation hooks, migration, unique mappings, tenant isolation, redaction, and failure tests.

**Review checkpoint:** approve connection schema, key lifecycle, redaction, and ownership constraints.

## Prompt 03 — Fast, idempotent webhook ingress

> Implement provider-routed webhook endpoints using a fake/simulator validator. Enforce payload and content-type limits, timestamp/replay-window checks when supported, signature-validation contract, connection resolution, and immutable WebhookEvent storage with unique provider event identity. A valid request must acknowledge only after durable storage and must not wait for conversation or AI processing. Invalid signatures must not create trusted events. Record redacted diagnostics and correlation IDs. Add latency-aware integration tests for valid, invalid, duplicate, oversized, unknown-connection, and database-failure cases.

**Review checkpoint:** approve durable acknowledgement semantics, uniqueness, security checks, and measured fast path.

## Prompt 04 — Normalization, processing jobs, retry, DLQ, and replay

> Implement versioned normalized inbound events and asynchronous processing jobs. Preserve the immutable raw event reference, reject duplicate normalized provider messages, classify transient/permanent failures, use bounded exponential retries with jitter where appropriate, move exhausted work to a visible DLQ state, and expose authorized replay that remains idempotent. Explicitly establish tenant context in every job. Add poison-event quarantine and schema-version handling. Test crash/restart points, duplicate delivery, out-of-order events, retry exhaustion, replay, and cross-tenant job isolation.

**Review checkpoint:** approve normalized schema, failure taxonomy, retry policy, DLQ, and replay evidence.

## Prompt 05 — Outbound outbox and delivery attempts

> Implement provider-neutral OutboundMessage, outbox scheduling, DeliveryAttempt, provider reference, delivery/read-status updates, bounded retry, permanent failure, cancellation, and replay rules. Enforce connection capability, conversation automation/ownership gate placeholder, tenant, rate-limit feedback, and idempotency before calling a provider. Use the simulator transport only. Ensure user-visible message state can distinguish queued, sent, delivered, read, failed, and unsupported. Add transaction/outbox, duplicate-send, crash, ordering, rate-limit, and tenant-isolation tests.

**Review checkpoint:** approve delivery lifecycle and evidence that retries cannot duplicate logical messages.

## Prompt 06 — Diagnostics API/UI and provider simulator

> Create a deterministic provider simulator capable of signed inbound events, duplicate/out-of-order delivery, configurable latency, rate limits, transient/permanent errors, token expiry, delivery receipts, and reconnect behavior. Expose authorized connection health, webhook events, delivery attempts, DLQ, and replay APIs. Connect the Milestone 01 integration diagnostics UI to real runtime data while retaining demo mode. Hide raw secrets and restrict payload visibility/redaction by role. Add end-to-end diagnostic and replay tests.

**Review checkpoint:** demonstrate healthy, degraded, expired, rate-limited, failed, and replayed scenarios without a live provider.

## Prompt 07 — Reliability, isolation, and failure campaign

> Run sustained simulator tests covering duplicate storms, out-of-order events, slow processing, database interruption, worker restart, provider timeout, rate limit, token expiry, poison payload, replay, and two-tenant concurrency. Measure webhook acknowledgement separately from downstream processing. Verify no silent event/message loss, no duplicate logical messages, no cross-tenant effect, bounded queues/retries, redacted observability, and recoverable DLQ items. Fix milestone-scoped defects and produce an integration reliability matrix.

**Review checkpoint:** approve the reliability matrix before any real provider adapter is written.

## Milestone exit gate

- The provider-neutral runtime passes simulator contract and failure tests.
- Webhooks validate, persist idempotently, and acknowledge independently of downstream work.
- Every failure is observable, bounded, and replayable where safe.
- Secrets are encrypted and never exposed in API/log output.
- Tenant isolation holds across connections, events, jobs, messages, and replay.
- No unverified provider-specific functionality is present.

