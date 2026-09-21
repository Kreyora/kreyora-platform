# M07-S05 — Outbound Outbox and Delivery Attempts — Scoped Plan

## Identity

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 05 — Outbound outbox and delivery attempts
- **Author:** Antigravity Phase 1 (Architect)
- **Date:** 2026-09-21
- **Status:** `PLANNING` — Awaiting human approval before implementation

## Objective

Build the outbound counterpart to the inbound processing pipeline (Steps 03–04). Implement a provider-neutral `OutboundMessage` aggregate with delivery lifecycle tracking, an `OutboundDeliveryAttempt` append-only record, a multi-tenant Hangfire delivery worker, failure classification and bounded retry reusing the existing `WebhookFailureClassifier` and `WebhookRetryPolicy`, dead-letter queue, cancellation and replay, status receipt processing from inbound webhooks, connection capability enforcement, a conversation gate placeholder, and idempotent send semantics. Use the simulator transport only.

## Scope

### Allowed

- New domain entities: `OutboundMessage`, `OutboundDeliveryAttempt`, `OutboundMessageStatus`, `OutboundMessageType`
- New application contracts: `IOutboundMessageService`, `IConversationGate`, outbound DTOs
- New persistence: `outbound_messages` and `outbound_delivery_attempts` tables with EF Core migration
- New infrastructure: `OutboundMessageService`, `OutboundDeliveryJob`, `AlwaysAllowConversationGate`
- Modify `WebhookProcessingService` to hook status receipts to outbound messages
- Enhance `SimulatorChannelProvider.SendMessageAsync` with simulated failure modes
- New controller: `OutboundMessagesController` (6 endpoints)
- Unit and integration tests for all new functionality
- Register new services in DI

### Prohibited

- Real provider adapters (WhatsApp, Instagram, etc.)
- Conversation/thread entity or AI orchestration (M08/M09)
- Connection-level rate-limit tracking (deferred to M07-S06/S07 if needed)
- Modifications to the existing `OutboxMessage` generic infrastructure
- Frontend changes
- Commits, pushes, or deployments without explicit authorization

## Design decisions

1. **Dedicated outbound entity**: `OutboundMessage` is a rich domain aggregate — not routed through the generic `OutboxMessage`. Social messages need delivery status, provider references, receipt correlation, and conversation gating that exceed the generic outbox pattern.

2. **Reuse retry infrastructure**: Reuse `WebhookRetryPolicy` (exponential backoff with jitter) and `WebhookFailureClassifier` (transient vs permanent classification) from Step 04. Both are provider-agnostic.

3. **Status receipt hook**: `WebhookProcessingService` calls `IOutboundMessageService.ProcessStatusReceiptAsync` for `MessageStatusUpdatedPayload` envelopes. Failure to update outbound status does not fail inbound processing.

4. **Conversation gate placeholder**: `IConversationGate` with `AlwaysAllowConversationGate` default. Real logic in M08.

5. **Rate-limit handling**: Per-message retry backoff when 429 is received (classified as Transient by `WebhookFailureClassifier`). Connection-level rate-limit tracking deferred.

## Data model

### `outbound_messages` table

| Column | Type | Constraints |
|---|---|---|
| id | varchar(26) | PK |
| tenant_id | varchar(26) | NOT NULL, FK |
| connection_id | varchar(26) | NOT NULL, FK |
| channel | integer | NOT NULL |
| conversation_id | varchar(26) | nullable |
| recipient_channel_id | varchar(256) | NOT NULL |
| idempotency_key | varchar(128) | NOT NULL |
| message_type | integer | NOT NULL |
| text_content | varchar(4096) | nullable |
| media_url | varchar(2048) | nullable |
| media_content_type | varchar(128) | nullable |
| caption | varchar(1024) | nullable |
| template_code | varchar(256) | nullable |
| template_parameters_json | jsonb | nullable |
| metadata_json | jsonb | nullable |
| status | integer | NOT NULL, default 0 |
| provider_message_id | varchar(256) | nullable |
| attempt_count | integer | NOT NULL, default 0 |
| max_attempts | integer | NOT NULL, default 5 |
| next_retry_at | timestamptz | nullable |
| failure_classification | integer | nullable |
| last_error_message | varchar(2048) | nullable |
| queued_at | timestamptz | NOT NULL |
| sent_at | timestamptz | nullable |
| delivered_at | timestamptz | nullable |
| read_at | timestamptz | nullable |
| failed_at | timestamptz | nullable |
| dead_lettered_at | timestamptz | nullable |
| cancelled_at | timestamptz | nullable |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | nullable |
| xmin | xid | concurrency token |

**Indexes:**
- UNIQUE `(tenant_id, connection_id, idempotency_key)`
- `(tenant_id, status, next_retry_at)` filtered WHERE status IN (0, 5)
- `(tenant_id, status, queued_at)`
- `(connection_id, provider_message_id)` filtered WHERE provider_message_id IS NOT NULL

### `outbound_delivery_attempts` table

| Column | Type | Constraints |
|---|---|---|
| id | varchar(26) | PK |
| tenant_id | varchar(26) | NOT NULL |
| outbound_message_id | varchar(26) | NOT NULL, FK |
| attempt_number | integer | NOT NULL |
| channel | integer | NOT NULL |
| connection_id | varchar(26) | NOT NULL |
| started_at | timestamptz | NOT NULL |
| completed_at | timestamptz | nullable |
| succeeded | boolean | NOT NULL, default false |
| provider_message_id | varchar(256) | nullable |
| provider_error_code | varchar(512) | nullable |
| provider_error_message | varchar(2048) | nullable |
| created_at | timestamptz | NOT NULL |

**Indexes:**
- `(outbound_message_id, attempt_number)`

## API contract

### `POST /v1/integrations/messages`
Queue an outbound message. Policy: `IntegrationsWrite`.

### `GET /v1/integrations/messages/{id}`
Get message status. Policy: `IntegrationsRead`.

### `GET /v1/integrations/messages`
List messages with pagination and filters. Policy: `IntegrationsRead`.

### `POST /v1/integrations/messages/{id}/cancel`
Cancel a queued message. Policy: `IntegrationsWrite`.

### `POST /v1/integrations/messages/{id}/replay`
Replay a failed/dead-lettered message. Requires `Idempotency-Key` header. Policy: `IntegrationsWrite`.

### `GET /v1/integrations/messages/dead-letter`
List dead-lettered messages. Policy: `IntegrationsRead`.

## Test plan

### Unit tests (~20)
- OutboundMessage factory validation and invariants
- Status transition rules (valid and invalid)
- Provider status monotonicity
- Cancellation and replay rules
- Content length validation
- OutboundDeliveryAttempt lifecycle

### Integration tests (~12, Testcontainers PostgreSQL)
- End-to-end queue → deliver → Sent
- Idempotency key deduplication
- Transient failure → retry → success
- Permanent failure → immediate DLQ
- Retry exhaustion → DLQ
- Rate-limit (429) → Transient retry
- Cancel queued message
- Cannot cancel sent message
- Replay dead-lettered message
- Tenant isolation
- Connection capability enforcement
- Status receipt → outbound message update

## Files affected

| File | Change | Purpose |
|---|---|---|
| `Kreyora.Domain/Integrations/OutboundMessageStatus.cs` | NEW | Status enum |
| `Kreyora.Domain/Integrations/OutboundMessageType.cs` | NEW | Message type enum |
| `Kreyora.Domain/Integrations/OutboundMessage.cs` | NEW | Aggregate root |
| `Kreyora.Domain/Integrations/OutboundDeliveryAttempt.cs` | NEW | Append-only entity |
| `Kreyora.Application/Integrations/IOutboundMessageService.cs` | NEW | Service contract |
| `Kreyora.Application/Integrations/OutboundMessageContracts.cs` | NEW | DTOs |
| `Kreyora.Application/Integrations/IConversationGate.cs` | NEW | Placeholder gate |
| `Kreyora.Infrastructure/Persistence/Configurations/OutboundMessageConfiguration.cs` | NEW | EF config |
| `Kreyora.Infrastructure/Persistence/Configurations/OutboundDeliveryAttemptConfiguration.cs` | NEW | EF config |
| `Kreyora.Infrastructure/Persistence/AppDbContext.cs` | MODIFY | DbSets, filters, append-only |
| `Kreyora.Infrastructure/Persistence/Migrations/...` | NEW | Migration |
| `Kreyora.Infrastructure/Integrations/OutboundMessageService.cs` | NEW | Service impl |
| `Kreyora.Infrastructure/Integrations/OutboundDeliveryJob.cs` | NEW | Hangfire job |
| `Kreyora.Infrastructure/Integrations/AlwaysAllowConversationGate.cs` | NEW | Default gate |
| `Kreyora.Infrastructure/Integrations/WebhookProcessingService.cs` | MODIFY | Status receipt hook |
| `Kreyora.Infrastructure/Integrations/Simulator/SimulatorChannelProvider.cs` | MODIFY | Send simulation |
| `Kreyora.Infrastructure/DependencyInjection.cs` | MODIFY | Register services |
| `Kreyora.WebApi/Controllers/OutboundMessagesController.cs` | NEW | API controller |
| `Kreyora.UnitTests/Integrations/OutboundMessageTests.cs` | NEW | Unit tests |
| `Kreyora.UnitTests/Integrations/OutboundDeliveryAttemptTests.cs` | NEW | Unit tests |
| `Kreyora.IntegrationTests/Integrations/OutboundMessageIntegrationTests.cs` | NEW | Integration tests |

## Rollback

All changes are additive (new tables, new files). Rollback is: revert the migration and remove the new files. No existing table or behavior is modified destructively.

## Approval gate

This plan must be approved by the project owner before Phase 2 (Builder) implementation begins.

