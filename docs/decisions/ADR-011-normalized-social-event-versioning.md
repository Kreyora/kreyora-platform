# ADR-011 — Normalized Social Event Versioning

- **Status:** `Accepted`
- **Date:** 2026-09-20
- **Owner:** Platform Architect
- **Reviewers:** Project Owner, Engineering Team
- **Affected milestones:** Milestone 07, Milestone 08, Milestone 09

## Context

Inbound webhooks from diverse social messaging platforms (Meta WhatsApp, Instagram Graph API, Messenger, Viber, Telegram) arrive with radically different JSON payload structures, timestamp conventions, message type definitions, and identity representations.

To enable unified inbox workflows, AI conversation processing, and order drafting without tight coupling to provider schemas, the incoming raw webhooks must be normalized into a canonical event model. We must decide how to structure and version these normalized events to allow schema evolution without breaking historical events or running background consumers.

## Decision

We adopt a strongly-typed polymorphic envelope model with an explicit schema version string (`schemaVersion: "v1"`).

1. **Envelope Structure (`NormalizedInboundEnvelope`):**
   - `EventId`: Canonical ULID identifying the normalized event.
   - `TenantId`: Mandatory tenant identifier.
   - `ConnectionId`: The originating `ChannelConnection` ID.
   - `Channel`: `ChannelType` enum (e.g. `WhatsApp`, `Instagram`, `Messenger`, `Viber`, `Telegram`, `Simulator`).
   - `OccurredAt`: Canonical UTC timestamp when the provider recorded the event.
   - `SchemaVersion`: Explicit version string, initialized to `"v1"`.
   - `Payload`: Polymorphic payload instance derived from `NormalizedInboundPayload`.
2. **Polymorphic Payloads (`v1`):**
   - `TextMessageReceivedPayload`: Standard incoming text messages with length and content.
   - `MediaMessageReceivedPayload`: Incoming media attachments (image, audio, video, document) with URL, MIME type, size, and caption.
   - `MessageStatusUpdatedPayload`: Delivery and read receipts (`sent`, `delivered`, `read`, `failed`) with provider references and error details.
   - `CustomerProfileUpdatedPayload`: External customer profile name, phone number, and avatar updates.
   - `ReactionReceivedPayload`: Emoji reactions and reaction removal events.
3. **Serialization & Evolution:**
   - Serialized using `System.Text.Json` with a type discriminator property (`$type` or `type`).
   - Adding non-breaking optional fields preserves `schemaVersion: "v1"`.
   - Breaking changes (e.g., restructuring core message identity) require introducing `schemaVersion: "v2"` alongside backward-compatible deserializer handlers in the background processing pipeline.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Dynamic JSON Document (`JsonElement` / `JsonNode`) | Schema-less; any provider field can be passed without C# contract changes. | No compile-time type safety; prone to runtime deserialization bugs in inbox and AI pipelines; contract drift cannot be caught by tests. | Rejected. Fails Kreyora's strict typing and correctness invariants. |
| Provider-Specific Events | Direct 1:1 mapping of each provider's exact event. | Leaks provider quirks into core domain; every consumer (inbox, AI, audit) must write switch-cases for each provider. | Rejected. Directly violates the provider-neutral architecture invariant. |
| **Strongly Typed Polymorphic Envelope with Versioning (Chosen)** | Full compile-time type safety; explicit schema versioning; clean separation of provider quirks from application logic. | Requires defining polymorphic C# records and maintaining deserialization compatibility for new versions. | Accepted. Delivers maximum reliability, testability, and long-term maintainability. |

## Consequences

- **Product impact:** Enables a uniform conversation inbox and AI assistant that operate identically across all supported social channels.
- **Architecture impact:** Application layer defines `NormalizedInboundEnvelope` and typed payloads; `IChannelProvider.NormalizeInboundAsync` maps raw payloads to this contract.
- **Security/privacy impact:** Customer identity and message content are normalized into consistent fields, making PII auditing and redaction predictable.
- **Cost/operations impact:** High performance with .NET 10 source-generated or strongly-typed `System.Text.Json` serialization.
- **Migration or rollback impact:** Historical normalized events retain their schema version; consumers can inspect `SchemaVersion` to handle legacy formats.

## Validation evidence

- Unit tests in `NormalizedInboundEventTests` verify polymorphic round-trip serialization and deserialization across all payload types.
- Contract tests verify that `IChannelProvider` implementations produce valid `v1` envelopes.

## Supersession conditions

- Introduction of streaming or real-time binary protocols (e.g., WebRTC audio) requiring fundamentally non-JSON envelope representations.

