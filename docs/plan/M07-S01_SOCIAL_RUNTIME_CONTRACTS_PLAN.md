# M07-S01 — Provider-Neutral Social Runtime Contracts and Integration ADRs Plan

## 1. Context & Objective

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 01 — Provider capability model and integration ADRs
- **Status:** `PLANNING`
- **Objective:** Define the provider-neutral channel boundary before implementing persistence or live webhooks. Produce a comprehensive capability matrix across 11 functional domains for candidate providers (WhatsApp Cloud API, Instagram Graph API, Messenger, Viber, Telegram, and Simulator). Write and accept 4 architectural decision records:
  - **ADR-010**: Social Connection Ownership (`TenantId` mandatory, `StoreId` optional).
  - **ADR-011**: Normalized Social Event Versioning (strongly typed envelope with polymorphic payload and explicit schema version `v1`).
  - **ADR-012**: Raw Provider Webhook Payload Retention and Privacy Policy (14–30 day retention for replay/debugging with automated purge/redaction while preserving audit metadata).
  - **ADR-013**: Secrets Encryption and Key Management (AES-256-GCM envelope encryption with key versioning and `ISecretEncryptionService`).
  Implement only the domain value objects, capability models, and `IChannelProvider` contracts approved by these ADRs, verified by contract tests using fakes.

---

## 2. Invariants & Guardrails

1. **Provider Neutrality Invariant:** Domain and application layers must never depend on provider-specific SDKs, wire formats, or error codes. Provider-specific quirks are confined to the adapter boundary.
2. **No Fabricated Capabilities:** Every cell in the capability matrix is strictly marked `verified`, `unsupported`, or `unknown` with concrete evidence requirements. No unverified provider behavior is claimed.
3. **Server Authority & Zero Leakage:** Social connections, inbound webhooks, and outbound messages are strictly tenant-isolated. Tenant context must be resolved from connection identity on webhooks and never from untrusted headers.
4. **Secret Confidentiality:** Credentials (access tokens, app secrets, webhook verification tokens) are encrypted at rest using AES-256-GCM envelope encryption and never logged, returned over public APIs, or committed in plain text.
5. **Durable Ingest & Quick Acknowledgment:** Webhooks validate signatures and persist immutable `WebhookEvent` records before acknowledging HTTP 200/202. Downstream normalization, AI processing, and outbound messaging happen asynchronously.
6. **Immutable Audit & Idempotency:** Duplicate webhook events (same provider event ID / message ID) are detected idempotently. Every state transition has actor/time/correlation evidence.

---

## 3. Decisions Agreed from `/grill-me`

1. **Connection Ownership (ADR-010):**
   - Social connections (`ChannelConnection`) belong to a `Tenant` (`TenantId` mandatory) with an optional `StoreId` binding.
   - Allows a tenant to share one brand WhatsApp/Instagram across multiple stores or bind a specific channel to a single dedicated storefront.
2. **Secrets Encryption & Key Management (ADR-013):**
   - AES-256-GCM envelope encryption with key versioning via `ISecretEncryptionService`.
   - Local/test environments use a deterministic development key; production uses managed master keys/KMS.
   - Ciphertext structure: `EncryptedSecret(string CiphertextBase64, string IvBase64, string AuthTagBase64, string KeyVersion)`.
3. **Raw Webhook Retention & Privacy (ADR-012):**
   - Short-term raw payload retention (14–30 days) in `WebhookEvent` for safe replay, debugging, and audit.
   - Background worker purges or redacts raw body after TTL while preserving event headers, timestamps, provider IDs, and correlation records.
4. **Normalized Event Versioning (ADR-011):**
   - Strongly-typed polymorphic envelope: `NormalizedInboundEnvelope<T>` containing `eventId`, `tenantId`, `connectionId`, `provider`, `occurredAt`, `schemaVersion: "v1"`.
   - Typed payloads: `TextMessageReceived`, `MediaMessageReceived`, `MessageStatusUpdated`, `CustomerProfileUpdated`, `ReactionReceived`.
5. **Provider Capability Model:**
   - Granular capability flags across 11 domains with explicit status (`verified`, `unsupported`, `unknown`).

---

## 4. Provider Capability Matrix

| Capability Domain | Description | WhatsApp Cloud API | Instagram Graph API | Messenger Platform | Viber Bot API | Telegram Bot API | Simulator |
|---|---|---|---|---|---|---|---|
| **Inbound Text** | UTF-8 text messages up to 4,096 chars | **Verified** (4,096 chars) | **Verified** (1,000 chars) | **Verified** (2,000 chars) | **Verified** (4,096 chars) | **Verified** (4,096 chars) | **Verified** (4,096 chars) |
| **Inbound Media** | Images, audio, video, documents with MIME type & download URL | **Verified** (JPEG, PNG, MP4, PDF, OGG) | **Verified** (Images, voice, video) | **Verified** (Images, audio, video, files) | **Verified** (Images, video, files) | **Verified** (Photos, voice, video, docs) | **Verified** (All standard media) |
| **Outbound Text** | Sending plain text responses to customer | **Verified** (Session/template gated) | **Verified** (Standard messaging) | **Verified** (Standard messaging) | **Verified** (Standard messaging) | **Verified** (Standard messaging) | **Verified** (Standard messaging) |
| **Outbound Media & Links** | Sending images, PDFs, structured link cards | **Verified** (Media IDs or public URLs) | **Verified** (Public URLs) | **Verified** (Public URLs or upload attachment) | **Verified** (Public URLs) | **Verified** (File uploads or URLs) | **Verified** (Configurable) |
| **Templates / HSM** | Pre-approved structured template messages | **Verified** (Meta Business Manager approval required) | **Unsupported** (Templates not required; 24h window applies) | **Unsupported** (Message tags used instead of templates) | **Unknown** (Requires Viber business partner verification) | **Unsupported** (No template pre-approval needed) | **Verified** (Simulated template catalog) |
| **Reactions** | Emoji reactions on messages | **Verified** (Single emoji reaction) | **Verified** (Emoji reaction events) | **Verified** (Emoji reactions) | **Unsupported** (1-to-1 bot reactions unsupported) | **Verified** (Emoji reactions) | **Verified** (Supported) |
| **Delivery Receipts** | Provider callback confirming message delivery to device | **Verified** (`delivered` status webhook) | **Unsupported** (No delivery receipt webhook) | **Verified** (`delivery` webhook) | **Verified** (`delivered` callback) | **Unsupported** (No delivery webhook) | **Verified** (Simulated delivery receipt) |
| **Read Receipts** | Provider callback confirming message read by customer | **Verified** (`read` status webhook) | **Verified** (`read` watermark webhook) | **Verified** (`read` watermark webhook) | **Verified** (`seen` callback) | **Unsupported** (No read receipt webhook) | **Verified** (Simulated read receipt) |
| **Customer Identity** | Channel-specific customer identifier & profile name | **Verified** (E.164 phone number + profile name) | **Verified** (IGSID + profile name if authorized) | **Verified** (PSID + first/last name if authorized) | **Verified** (Viber user ID + name) | **Verified** (Telegram user ID + username/name) | **Verified** (Synthetic channel ID & name) |
| **24h Messaging Window** | Standard messaging restricted to 24h since customer last message | **Verified** (Strict 24h customer care window; templates required outside) | **Verified** (Strict 24h window; human agent tag allows 7 days) | **Verified** (Strict 24h window; message tags allow specific use cases) | **Unsupported** (Session timeout varies; session messages free, broadcast paid) | **Unsupported** (No 24h window restriction for user-initiated bots) | **Verified** (Simulated 24h window check) |
| **Token Refresh & Lifecycle** | Token validation, expiry detection, and refresh mechanism | **Verified** (System User tokens don't expire; user tokens expire in 60 days) | **Verified** (Long-lived Page tokens require periodic refresh) | **Verified** (Page access tokens require refresh) | **Verified** (Static auth token; no refresh required) | **Verified** (Static bot token; no refresh required) | **Verified** (Simulated token expiry & refresh) |
| **Webhook Signature Verification** | Cryptographic payload verification at ingress | **Verified** (`X-Hub-Signature-256` HMAC-SHA256) | **Verified** (`X-Hub-Signature-256` HMAC-SHA256) | **Verified** (`X-Hub-Signature-256` HMAC-SHA256) | **Verified** (`X-Viber-Content-Signature` HMAC-SHA256) | **Verified** (`X-Telegram-Bot-Api-Secret-Token` header check) | **Verified** (`X-Kreyora-Signature-256` HMAC-SHA256) |

---

## 5. Architectural Decision Records (ADRs) to Produce

1. **ADR-010: Social Connection Ownership and Multi-Tenant Routing**
   - Context: Social media accounts (WhatsApp, Instagram, etc.) must be associated with the tenant hierarchy.
   - Decision: `ChannelConnection` is owned by `TenantId` with an optional `StoreId` binding. Inbound webhooks resolve `TenantId` and optional `StoreId` from the connection mapping.
2. **ADR-011: Normalized Social Event Versioning**
   - Context: Inbound events from disparate providers must be normalized for uniform inbox, conversation, and AI processing without losing fidelity.
   - Decision: Strongly typed polymorphic envelope (`schemaVersion: "v1"`) with discriminator mapping to concrete payload types (`TextMessageReceived`, `MediaMessageReceived`, `MessageStatusUpdated`, `CustomerProfileUpdated`, `ReactionReceived`).
3. **ADR-012: Raw Provider Webhook Payload Retention and Privacy Policy**
   - Context: Raw webhook payloads are required for cryptographic signature verification, replay, and incident debugging, but contain customer PII.
   - Decision: Retain raw payloads in `WebhookEvent` for 30 days. Background job purges/redacts raw body after 30 days while preserving event metadata and header hashes. Access is restricted to `Owner` and `PlatformSupport` roles with audit logging.
4. **ADR-013: Secrets Encryption and Key Management for Social Credentials**
   - Context: Third-party access tokens and webhook secrets must be stored securely at rest.
   - Decision: AES-256-GCM envelope encryption with key versioning. `ISecretEncryptionService` interface allows swappable key providers (local development key in dev/test, KMS/Vault in production). Ciphertext envelope stores `keyVersion`, `iv`, `ciphertext`, and `authTag`.

---

## 6. Contracts, Enums, and Value Objects to Implement

### A. Core Enums & Value Objects (`Kreyora.Domain.Integrations`)
- `ChannelType`: `WhatsApp`, `Instagram`, `Messenger`, `Viber`, `Telegram`, `Simulator`.
- `ChannelCapabilities`: Immutable value object with bool properties:
  - `CanReceiveText`, `CanReceiveMedia`, `CanSendText`, `CanSendMedia`, `CanSendLinkPreview`, `RequiresTemplatesOutsideWindow`, `SupportsReactions`, `SupportsDeliveryReceipts`, `SupportsReadReceipts`, `Enforces24HourWindow`, `SupportsTokenRefresh`, `RequiresSignatureVerification`.
- `ChannelConnectionStatus`: `Pending`, `Active`, `Degraded`, `Expired`, `Revoked`, `Disabled`.
- `EncryptedSecret`: Immutable value object `{ string CiphertextBase64, string IvBase64, string AuthTagBase64, string KeyVersion }`.

### B. Normalized Inbound Events (`Kreyora.Domain.Integrations`)
- `NormalizedInboundEnvelope`:
  - `string EventId`, `string TenantId`, `string ConnectionId`, `ChannelType Channel`, `DateTimeOffset OccurredAt`, `string SchemaVersion`, `NormalizedInboundPayload Payload`.
- Polymorphic Payloads:
  - `TextMessageReceivedPayload(string MessageId, string SenderChannelId, string? SenderName, string Text, DateTimeOffset Timestamp)`
  - `MediaMessageReceivedPayload(string MessageId, string SenderChannelId, string? SenderName, string MediaUrl, string ContentType, long? ByteSize, string? Caption, DateTimeOffset Timestamp)`
  - `MessageStatusUpdatedPayload(string MessageId, string RecipientChannelId, MessageDeliveryStatus Status, string? ProviderErrorCode, string? ProviderErrorMessage, DateTimeOffset Timestamp)`
  - `CustomerProfileUpdatedPayload(string SenderChannelId, string? DisplayName, string? ProfilePictureUrl, string? PhoneNumber)`
  - `ReactionReceivedPayload(string MessageId, string SenderChannelId, string Emoji, bool IsRemoved, DateTimeOffset Timestamp)`

### C. Application Contracts (`Kreyora.Application.Integrations`)
- `ISecretEncryptionService`:
  - `EncryptedSecret Encrypt(string plainText, string? keyVersion = null)`
  - `string Decrypt(EncryptedSecret encryptedSecret)`
- `IChannelProvider`:
  - `ChannelType Channel { get; }`
  - `ChannelCapabilities Capabilities { get; }`
  - `Task<WebhookValidationResult> ValidateWebhookAsync(WebhookValidationRequest request, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<NormalizedInboundEnvelope>> NormalizeInboundAsync(RawWebhookPayload rawPayload, CancellationToken cancellationToken = default)`
  - `Task<OutboundDeliveryResult> SendMessageAsync(ChannelConnectionSnapshot connection, OutboundMessageRequest message, CancellationToken cancellationToken = default)`
  - `Task<ConnectionHealthResult> ValidateOrRefreshConnectionAsync(ChannelConnectionSnapshot connection, CancellationToken cancellationToken = default)`
- `IChannelProviderRegistry`:
  - `IChannelProvider GetProvider(ChannelType channelType)`
  - `bool TryGetProvider(ChannelType channelType, out IChannelProvider? provider)`

---

## 7. Verification Plan

### Automated Tests
1. **Unit Tests (`Kreyora.UnitTests.Integrations`):**
   - `ChannelCapabilitiesTests`: Verify capability flags combination, equality, and validation.
   - `NormalizedInboundEventTests`: Verify envelope construction, polymorphic serialization/deserialization with `System.Text.Json`, schema version validation.
   - `AesGcmSecretEncryptionServiceTests`: Verify encryption/decryption round-trip, tampering detection (invalid auth tag), invalid key version rejection, and empty input handling.
2. **Contract Tests (`Kreyora.ContractTests.Integrations`):**
   - `ChannelProviderContractTests`: Table-driven tests against `FakeChannelProvider` proving compliance with `IChannelProvider` invariants:
     - Invalid webhook signatures return validation failure.
     - Inbound normalization produces valid `NormalizedInboundEnvelope` instances with correct timestamps and schema versions.
     - Outbound delivery respects capability flags (e.g. throwing/rejecting if attempting to send unsupported media).
     - Connection health check returns valid diagnostics.
3. **Quality Gates:**
   - `dotnet test services/api/Kreyora.slnx --configuration Release`
   - `pnpm ci:frontend`
   - `git diff --check`

---

## 8. Checkpoint & Handoff
- Produce `artifacts/checkpoints/M07-S01.md` with status `REVIEW`.
- Update `docs/context/CURRENT_WORK.md` and `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`.
- Wait for human review and approval before starting M07-S02.

