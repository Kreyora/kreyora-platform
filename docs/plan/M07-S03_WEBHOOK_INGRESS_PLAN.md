# Milestone 07 Step 03: Fast, Idempotent Webhook Ingress Plan

## 1. Executive Summary & Objective

**Milestone:** 07 — Provider-Neutral Social Integration Runtime  
**Step:** 03 — Fast, idempotent webhook ingress  
**Objective:** Implement provider-routed webhook ingress endpoints supporting both connection-scoped and provider-wide routing. Enforce payload and content-type limits, timestamp/replay-window checks, cryptographic signature validation, connection/tenant resolution, and immutable `WebhookEvent` persistence with unique provider event identity. Deliver fast acknowledgment (<200ms) without waiting for downstream conversation or AI processing, while ensuring untrusted signatures never produce trusted events.

---

## 2. Authority & Governing ADRs

- **ADR-010 (Social Connection Ownership & Multi-Tenant Routing):** Connections are owned by `TenantId` with optional `StoreId` binding. Inbound webhooks resolve connections via URL route (`/v1/webhooks/{channel}/{connectionId}`) or external account identifier (`/v1/webhooks/{channel}`), establishing verified tenant and store context.
- **ADR-011 (Normalized Social Event Versioning):** Raw webhook payloads remain decoupled from application processing; raw ingress only captures and persists the event, delegating normalization to Step 04.
- **ADR-012 (Raw Webhook Payload Retention & Privacy Policy):** The raw body, headers, and metadata are stored immutably in `WebhookEvent` with 30-day retention and automated purge flags (`IsPurged`, `PurgedAt`). Raw payloads are restricted to administrative/replay access and never returned over public APIs.
- **ADR-013 (Secrets Encryption & Key Management):** Signing secrets and tokens are encrypted at rest; decrypted in-memory only for cryptographic signature verification.

---

## 3. Threat Model & Edge-Case Analysis

| Threat / Edge Case | Risk | Mitigation |
|---|---|---|
| **Forged Webhook / Invalid Signature** | Attacker injects fake customer messages or orders. | `IChannelProvider.ValidateWebhookAsync` verifies HMAC-SHA256 signature using the connection's decrypted secret before any trusted event is created. Invalid signatures return `401 Unauthorized` immediately. |
| **Webhook Replay Attack** | Attacker intercepts a valid signed webhook and replays it later. | Timestamp validation (`X-Hub-Timestamp` or `X-Timestamp`) enforces a strict replay window (default 300 seconds). Requests outside the window are rejected. |
| **Duplicate Delivery Storm** | Social providers (Meta, Telegram, Viber) retry unacknowledged webhooks rapidly, causing duplicate database entries and duplicate processing. | PostgreSQL unique composite index on `(ConnectionId, ProviderEventId)` prevents duplicate inserts. On duplicate detection, ingress returns `200 OK` (idempotent acknowledgment) with zero duplicate records created. |
| **Slow Downstream Processing / Webhook Timeout** | Providers timeout after 2–5 seconds, disconnecting the webhook if the server waits for AI or order processing. | Fast-path architecture: ingress only validates, persists raw event, and immediately returns HTTP 200/202. AI, conversation, and order effects are strictly asynchronous (Step 04). |
| **Payload Bomb / Oversized Body** | Attacker sends a multi-megabyte JSON payload causing high memory allocation / DoS. | Ingress enforces a strict payload size limit (default 256 KB) via ASP.NET Core `[RequestSizeLimit]` and stream length inspection. Oversized requests return `413 Payload Too Large`. |
| **Unsupported Media Type** | Unexpected MIME types causing parser crashes. | Ingress validates `Content-Type` header (must be `application/json` or provider-specific supported types). Invalid types return `415 Unsupported Media Type`. |
| **Unknown / Inactive Connection** | Webhook targets a deleted, disabled, or non-existent connection. | Ingress queries `ChannelConnections` (ignoring tenant query filters since request is unauthenticated) and verifies connection exists and is `Active`. Inactive or missing connections return `404 Not Found` or `403 Forbidden`. |
| **Database Outage / Transient Failure** | Database fails during event persistence. | Ingress logs error with correlation ID and returns `500 Internal Server Error` (or `503 Service Unavailable`), signaling the provider to retry later. No partial or corrupted state is saved. |

---

## 4. Domain Model Design

### 4.1 `WebhookEvent` Aggregate Root
Location: `services/api/src/Kreyora.Domain/Integrations/WebhookEvent.cs`

- Inherits from: `BaseEntity, ITenantOwned`
- Properties:
  - `TenantId` (`string`, mandatory, max 26)
  - `ConnectionId` (`string`, mandatory, max 26)
  - `Channel` (`ChannelType`, mandatory)
  - `ProviderEventId` (`string`, mandatory, max 128)
  - `EventType` (`string?`, optional, max 64)
  - `OccurredAt` (`DateTimeOffset`, mandatory)
  - `ReceivedAt` (`DateTimeOffset`, mandatory)
  - `ProcessedAt` (`DateTimeOffset?`, nullable)
  - `ProcessingStatus` (`WebhookProcessingStatus`, mandatory)
  - `CorrelationId` (`string`, mandatory, max 64)
  - `Headers` (`string`, JSON-serialized headers dictionary, mandatory)
  - `RawPayload` (`string`, text column, mandatory)
  - `IsPurged` (`bool`, mandatory, default `false` per ADR-012)
  - `PurgedAt` (`DateTimeOffset?`, nullable)
  - `ErrorMessage` (`string?`, nullable, max 1024)
- Domain Methods:
  - `Create(...)`: Factory method enforcing non-empty tenant, connection, provider event ID, and raw payload.
  - `MarkProcessing()`: Transitions status from `Received` to `Processing`.
  - `MarkProcessed(DateTimeOffset processedAt)`: Transitions to `Processed`.
  - `MarkFailed(string errorMessage)`: Records failure and transitions to `Failed`.
  - `PurgePayload(DateTimeOffset purgedAt)`: Sets `RawPayload = "[PURGED]"`, `IsPurged = true`, `PurgedAt = purgedAt` (ADR-012).

### 4.2 `WebhookProcessingStatus` Enum
Location: `services/api/src/Kreyora.Domain/Integrations/WebhookProcessingStatus.cs`

- Values:
  - `Received = 0` (Durable raw event persisted; awaiting asynchronous normalization)
  - `Processing = 1` (Picked up by background worker)
  - `Processed = 2` (Normalized and downstream effects emitted)
  - `Failed = 3` (Processing failed, eligible for retry)
  - `DeadLetter = 4` (Retries exhausted, queued for operator review)

---

## 5. Persistence & Migration Strategy

### 5.1 EF Core Configuration
Location: `services/api/src/Kreyora.Infrastructure/Persistence/Configurations/WebhookEventConfiguration.cs`

- Table: `webhook_events`
- Primary Key: `Id` (ULID, length 26)
- Alternate Key: `(TenantId, Id)`
- Column specifications:
  - `tenant_id`: `varchar(26)`, required
  - `connection_id`: `varchar(26)`, required
  - `channel`: `varchar(32)`, required, enum string conversion
  - `provider_event_id`: `varchar(128)`, required
  - `event_type`: `varchar(64)`, optional
  - `occurred_at`: `timestamp with time zone`, required
  - `received_at`: `timestamp with time zone`, required
  - `processed_at`: `timestamp with time zone`, optional
  - `processing_status`: `varchar(32)`, required, enum string conversion
  - `correlation_id`: `varchar(64)`, required
  - `headers`: `text`, required
  - `raw_payload`: `text`, required
  - `is_purged`: `boolean`, required, default `false`
  - `purged_at`: `timestamp with time zone`, optional
  - `error_message`: `varchar(1024)`, optional
  - `xmin`: shadow concurrency token
- Indexes:
  - **Unique composite index:** `(ConnectionId, ProviderEventId)` — ensures strict deduplication per connection.
  - **Tenant processing index:** `(TenantId, ProcessingStatus, ReceivedAt)` — optimizes background worker pickup.
  - **Retention purge index:** `(IsPurged, ReceivedAt)` — optimizes ADR-012 30-day purge scans.
- Foreign Key:
  - `(TenantId, ConnectionId)` -> `ChannelConnection(TenantId, Id)` with `DeleteBehavior.Restrict`.

### 5.2 DbContext Updates
- Add `DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();` to `AppDbContext.cs`.
- Add global query filter: `builder.Entity<WebhookEvent>().HasQueryFilter(e => e.TenantId == CurrentTenantId);`.
- Note: Webhook ingress queries use `.IgnoreQueryFilters()` because incoming HTTP webhooks are unauthenticated at the transport layer and establish tenant context only after resolving the connection.

### 5.3 PostgreSQL Migration
- Generate migration `AddWebhookEvents` via `dotnet ef migrations add AddWebhookEvents`.
- Verify zero pending model changes.

---

## 6. Application Layer Design

### 6.1 `IWebhookIngressService`
Location: `services/api/src/Kreyora.Application/Integrations/IWebhookIngressService.cs`

```csharp
public interface IWebhookIngressService
{
    Task<WebhookIngressResult> HandleWebhookAsync(
        WebhookIngressCommand command,
        CancellationToken cancellationToken = default);

    Task<WebhookChallengeResult> HandleChallengeAsync(
        WebhookChallengeCommand command,
        CancellationToken cancellationToken = default);
}
```

### 6.2 Commands & Results
Location: `services/api/src/Kreyora.Application/Integrations/WebhookIngressContracts.cs`

- `WebhookIngressCommand`:
  - `ChannelType Channel`
  - `string? ConnectionId`
  - `string Method`
  - `string Path`
  - `IReadOnlyDictionary<string, string> Headers`
  - `IReadOnlyDictionary<string, string> QueryParameters`
  - `byte[] RawBody`
  - `string? ContentType`
  - `string? CorrelationId`
  - `DateTimeOffset ReceivedAt`
- `WebhookIngressResult`:
  - `bool IsSuccess`
  - `int StatusCode`
  - `string? EventId`
  - `bool IsDuplicate`
  - `string? ErrorReason`
  - Factory methods: `Success(string eventId, bool isDuplicate)`, `InvalidSignature(string reason)`, `PayloadTooLarge()`, `UnsupportedMediaType()`, `NotFound(string reason)`, `ReplayWindowExpired(string reason)`, `InternalError(string message)`.
- `WebhookChallengeCommand`:
  - `ChannelType Channel`
  - `string? ConnectionId`
  - `IReadOnlyDictionary<string, string> QueryParameters`
  - `IReadOnlyDictionary<string, string> Headers`
- `WebhookChallengeResult`:
  - `bool IsValid`
  - `string? ChallengeResponse`
  - `string? ErrorReason`

---

## 7. Infrastructure Layer Design

### 7.1 `WebhookIngressService`
Location: `services/api/src/Kreyora.Infrastructure/Integrations/WebhookIngressService.cs`

- Coordinates:
  1. **Enforce Size & Content-Type Limits:** Return 413 or 415 if violated.
  2. **Connection Resolution:** Query `ChannelConnections.IgnoreQueryFilters()` by `ConnectionId` or by `(Channel, ExternalAccountId)`. If not found or not `Active`, return 404 / 403.
  3. **Secret Decryption:** If connection has encrypted credentials or verification token, decrypt via `ISecretEncryptionService` to obtain signing secret.
  4. **Signature & Replay Validation:** Invoke `IChannelProvider.ValidateWebhookAsync(...)`. If invalid or expired, log diagnostic and return 401 / 400 immediately without persisting any event.
  5. **Deduplication & Storage:**
     - Extract `ProviderEventId` (from validation result or headers or JSON body).
     - Check `WebhookEvents.IgnoreQueryFilters().AnyAsync(e => e.ConnectionId == connection.Id && e.ProviderEventId == providerEventId)`.
     - If duplicate: return `WebhookIngressResult.Success(existingEventId, isDuplicate: true)` immediately.
     - If new: create `WebhookEvent`, insert to `_dbContext.WebhookEvents`, call `SaveChangesAsync`.
     - Return `WebhookIngressResult.Success(event.Id, isDuplicate: false)`.

### 7.2 `SimulatorChannelProvider`
Location: `services/api/src/Kreyora.Infrastructure/Integrations/Simulator/SimulatorChannelProvider.cs`

- Full implementation of `IChannelProvider` for `ChannelType.Simulator`:
  - Validates `X-Hub-Signature-256` header (supports `"sha256=valid_test_signature"` or HMAC-SHA256 of body with signing secret).
  - Validates replay window if `X-Hub-Timestamp` or `X-Timestamp` header is present.
  - Handles challenge verification for `hub.mode == "subscribe"` and `hub.challenge`.
  - Extracts `ProviderEventId` from `X-Provider-Event-Id` header or JSON payload `id`.
  - Extracts `ExternalAccountId` from `X-External-Account-Id` header or JSON payload `account_id`.
  - Provides mock normalization and outbound delivery.

---

## 8. Web API Layer Design

### 8.1 `WebhooksController`
Location: `services/api/src/Kreyora.WebApi/Controllers/WebhooksController.cs`

- Attributes: `[ApiController]`, `[AllowAnonymous]`, `[ApiVersion("1.0")]`
- Route templates:
  - `POST /v1/webhooks/{channel}` (provider-wide webhook)
  - `POST /v1/webhooks/{channel}/{connectionId}` (connection-specific webhook)
  - `GET /v1/webhooks/{channel}` (challenge verification)
  - `GET /v1/webhooks/{channel}/{connectionId}` (challenge verification)
- Security controls:
  - `[RequestSizeLimit(256 * 1024)]` (256 KB limit)
  - Content-Type verification (`application/json`)
  - Redacted logging (no raw secrets, masked signatures, correlation IDs attached)

---

## 9. Verification & Latency Test Plan

### 9.1 Unit Tests (`Kreyora.UnitTests/Integrations/`)
- `WebhookEventTests.cs`:
  - Factory creation invariants (validates tenant, connection, event ID, raw payload).
  - Status transitions (`MarkProcessing`, `MarkProcessed`, `MarkFailed`).
  - ADR-012 30-day purge method (`PurgePayload`).
- `SimulatorChannelProviderTests.cs`:
  - Signature validation (valid, invalid, missing header).
  - Replay window validation (fresh timestamp vs expired timestamp).
  - Challenge verification (matching token vs mismatched token).

### 9.2 Integration Tests (`Kreyora.IntegrationTests/Integrations/`)
- `WebhookIngressIntegrationTests.cs` (PostgreSQL Testcontainers in Docker):
  1. **Valid Webhook:** Signed webhook succeeds, persists `WebhookEvent` with `Received` status, returns 200/202, elapsed latency < 300ms.
  2. **Invalid Signature:** Tampered or wrong signature returns 401, zero database rows created.
  3. **Duplicate Webhook:** Consecutive duplicate deliveries return 200/202 with `isDuplicate = true`, database contains exactly 1 row.
  4. **Oversized Payload:** Payload > 256 KB returns 413 Payload Too Large, zero database rows created.
  5. **Unknown Connection:** Non-existent connection ID returns 404 Not Found, zero database rows created.
  6. **Replay Window Expired:** Request with timestamp > 300s in the past returns 400/401, zero database rows created.
  7. **Challenge Verification:** GET request with valid verify token returns raw challenge string with 200 OK.
  8. **Multi-Tenant Isolation:** Ingress creates event under the connection's tenant; query with another tenant's query filter cannot see it.

---

## 10. Quality Gates & Acceptance Criteria

1. `dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1` succeeds with 0 errors, 0 warnings.
2. `dotnet ef migrations has-pending-model-changes` returns 0 pending model changes.
3. Unit tests pass (100% of domain and simulator tests).
4. Real PostgreSQL Testcontainers integration tests pass (100% of ingress scenarios).
5. Frontend CI (`pnpm ci:frontend`) passes.
6. `git diff --check` passes cleanly.
7. Checkpoint `artifacts/checkpoints/M07-S03.md` created in `REVIEW` status.

