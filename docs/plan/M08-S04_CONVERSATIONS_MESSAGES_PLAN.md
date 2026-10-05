# M08-S04 — Customer Identities, Conversations, and Messages — Scoped Plan

## Identity

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 04 — Customer identities, conversations, and messages
- **Author:** Claude (planning)
- **Date:** 2026-10-05
- **Status:** `REVIEW` — implemented 2026-10-05; evidence in `artifacts/checkpoints/M08-S04.md` (plan, including ADR-016 rules D2–D3, approved by the owner on 2026-10-05: "implement")
- **Prerequisites:** M08-S03 `APPROVED` (2026-10-05); ADR-010, ADR-011, ADR-012, ADR-015 `Accepted`.
- **Manual work for the owner in this step:** none. Live Meta checks remain at M08-S07.

## Milestone prompt (verbatim)

> Implement CustomerChannelIdentity, Customer, Conversation, Message, external references, assignment metadata, labels, unread state, and automation ownership state. Define deterministic rules for conversation lookup/creation and identity linkage within a tenant/connection. Store only necessary provider/customer fields and apply retention/redaction policies. Consume normalized provider events idempotently and update delivery state without duplicating messages. Add migrations, service methods, indexes, isolation tests, and out-of-order event tests.

**Review checkpoint:** approve identity boundaries, data minimization, conversation rules, and message timeline evidence.

## What exists today (verified 2026-10-05)

- `Domain/Customers/Customer` is the M05 **checkout** customer. It requires a Nepal mobile number (unique per tenant), privacy acknowledgement, and retention-review fields. An Instagram sender has none of these.
- S03 produces `InboundEvent` rows with v1 payloads: text, media, read status, reaction. Each row carries a per-connection `DeduplicationKey` (ADR-015). Echo messages (business-sent, `is_echo`) are skipped. `SenderName` is null.
- `OutboundMessage` (M07-S05) has a free-text `ConversationId` (max 64 characters) and no foreign key. `IConversationGate` is an always-allow placeholder (S05 scope).
- Permissions `conversations.read` (Viewer and up) and `conversations.write` (Operator and up) already exist.
- Frontend types (`apps/web/src/lib/types/conversations.ts`) use the plan §10.4 state names, `unreadCount`, `labels`, `assignment`, `isAutomationActive`, and message `direction` / `senderType` / `deliveryState`.

## Objective

Turn normalized inbound events into tenant-isolated customer channel identities, one conversation per identity per connection, and an ordered message timeline. Do this idempotently and atomically with inbound processing, and expose read-only inbox queries. No replies, assignment actions, takeover, or UI (S05/S06).

## Design

### Data model (new Conversations module + Customers extension)

**`CustomerChannelIdentity`** (`Domain/Customers`)
- Fields: `TenantId`, `ConnectionId`, `Channel`, `ExternalUserId` (Instagram IGSID), `DisplayName?` (null until a profile source exists), `CustomerId?`, `FirstSeenAt`, `LastSeenAt`, `ErasedAt?`.
- Unique `(ConnectionId, ExternalUserId)`. IGSIDs are scoped to the business account, so identity is scoped to one connection, as the milestone requires.
- `CustomerId` links to an M05 checkout `Customer` only by an explicit later action. S04 never auto-links or merges. The milestone forbids automatic cross-channel merging without verified identifiers and policy, so the M05 `Customer` is reused unchanged rather than weakened (no nullable phone).

**`Conversation`** (`Domain/Conversations`)
- Fields: `TenantId`, `ConnectionId`, `StoreId?` (copied from the connection, ADR-010), `CustomerChannelIdentityId`, `Channel`.
- State: `Status` (plan §10.4: `New`, `BotActive`, `HumanAssigned`, `AwaitingCustomer`, `CheckoutInProgress`, `OrderCreated`, `Resolved`, `Closed`, `Spam`); `AutomationMode` (`Automated` | `HumanTakeover`); `AssignedUserId?`, `AssignedAt?`.
- Inbox counters and timestamps: `UnreadCount`, `LastMessageAt`, `LastCustomerMessageAt` (input for the S05 24-hour window), `CustomerLastReadAt?` (seen-receipt watermark).
- `xmin` concurrency token.
- **Unique `(ConnectionId, CustomerChannelIdentityId)`:** one continuous thread per customer per connection, matching how Instagram DMs work.
- Labels live in `ConversationLabel` (`TenantId`, `ConversationId`, `Label`; unique per conversation). S04 stores and returns them; S05 adds the edit operations.

**`Message`** (`Domain/Conversations`)
- Fields:
  - identity and links: `TenantId`, `ConversationId`, `ConnectionId`, `InboundEventId?` (external reference for troubleshooting);
  - classification: `Direction` (`Inbound` | `Outbound`), `Origin` (`Customer` | `Staff` | `Automation` | `ProviderNative`), `ProviderMessageId?` (512);
  - content: `Kind` (`Text` | `Media`), `Text?`, `MediaUrl?`, `MediaContentType?`;
  - timing and state: `OccurredAt` (provider time), `ReceivedAt`, `DeliveryStatus?` (outbound only; monotonic), `RedactedAt?`.
- Unique `(ConnectionId, ProviderMessageId)` where not null. S05's own sends and their echoes then cannot duplicate.
- The timeline is ordered by `(OccurredAt, CreatedAt, Id)`, never by arrival.

**`MessageReaction`**
- Fields: `TenantId`, `ConnectionId`, `ProviderMessageId` (the target), `ReactorChannelId`, `Emoji`, `IsRemoved`, `OccurredAt`.
- Unique `(ConnectionId, ProviderMessageId, ReactorChannelId)`; last-write-wins by `OccurredAt`.
- There is deliberately no foreign key to `Message`: reactions may arrive before, or without, the message they reference (out-of-order or business messages sent outside Kreyora).

### Deterministic rules (proposed ADR-016)

1. **Identity:** find-or-create by `(ConnectionId, sender ExternalUserId)`; update `LastSeenAt`. Identities never cross connections or tenants.
2. **Conversation:** find-or-create by `(ConnectionId, IdentityId)`. A new conversation starts as `New` with `AutomationMode = Automated` (plan §10.4 starts at `new → bot_active`; AI invocation remains gated by M09 entitlement/enablement). `StoreId` is copied from the connection.
3. **Inbound message on an existing conversation:**
   - `Resolved` or `Closed` → reopened to `New`;
   - `Spam` → stays `Spam`; the message is stored but `UnreadCount` is unchanged;
   - otherwise the status is unchanged and `UnreadCount += 1`.

   `LastMessageAt` and `LastCustomerMessageAt` become the max of their current value and the message's `OccurredAt`, so out-of-order arrival is safe.
4. **Read receipt (`MessageStatusUpdatedPayload(Read)`):**
   - `CustomerLastReadAt` becomes the max of its current value and `OccurredAt`;
   - if an outbound `Message` with that provider ID exists, its `DeliveryStatus` advances monotonically and is never downgraded;
   - receipts for unknown messages update only the watermark.
5. **Reaction:** upsert `MessageReaction`, last-write-wins on `OccurredAt`. An older event arriving late never overrides a newer one.
6. **Media:** store the provider URL and type only. Downloading to private R2 is out of scope (provider URLs expire; recorded as a limitation).

The plan defines the conversation state machine, but not reopening on inbound or spam handling. Under change control (state machine), rules 2–3 are recorded as **ADR-016 (Proposed)**, accepted together with this plan.

### Processing integration (atomic, idempotent)

- New Application contract **`IConversationIngestionService.IngestAsync(InboundEvent inbound, NormalizedInboundPayload payload, ChannelConnection connection, CancellationToken)`**, implemented in Infrastructure (Conversations).
- `WebhookProcessingService` calls it for each **newly created** `InboundEvent`, before the single `SaveChanges`. Inbound event, identity, conversation, and message are therefore committed atomically or not at all. This is an explicit application orchestrator, the module-boundary pattern `backend.md` allows.
- Idempotency layers:
  1. the ADR-015 `InboundEvent` dedup key (duplicates never reach ingestion);
  2. the unique message, identity, and conversation indexes;
  3. the S03 recovery path: a race on find-or-create (two events for a new customer processed concurrently) or an `xmin` conflict on counters fails the attempt Transient, and the retry finds the committed rows.
- `MessageStatusUpdatedPayload` keeps its existing call to `IOutboundMessageService.ProcessStatusReceiptAsync` (M07).

### Read API (new `ConversationsController`, thin, RFC 7807)

| Route | Permission | Purpose |
|---|---|---|
| `GET /v1/conversations?status=&connectionId=&unreadOnly=&assignedTo=&page=&pageSize=` | `conversations.read` | Paged inbox list. Each item has customer label (display name or masked ID, e.g. `Instagram user ·4821`), channel, status, last message preview (from the latest message, truncated server-side), `lastMessageAt`, `unreadCount`, labels, assignment, `isAutomationActive` |
| `GET /v1/conversations/{id}` | `conversations.read` | Detail, plus identity summary (no raw provider payloads) |
| `GET /v1/conversations/{id}/messages?before=&pageSize=` | `conversations.read` | Timeline page (keyset on `OccurredAt`/`Id`) with reactions aggregated per message |
| `POST /v1/conversations/{id}/read` | `conversations.write` | Resets `UnreadCount` to 0 (idempotent) |

- Every query is tenant-filtered and also checks tenant ownership of `{id}` explicitly; a cross-tenant ID returns `404`.
- The OpenAPI snapshot and TypeScript contract are regenerated with the existing live-local-API workflow (`pnpm generate:api`). No frontend wiring in S04 (S06).

### Data minimization, retention, redaction

- Stored: only the fields above. No profile pictures, usernames, phone numbers, or raw payload copies. Raw payloads stay in `WebhookEvent` under ADR-012's 30-day purge.
- **Erasure:** `IConversationPrivacyService.EraseIdentityAsync(identityId)`.
  - Owner only, audited (`conversations.identity.erased`).
  - Nulls message `Text`/`MediaUrl` (sets `RedactedAt`), clears `DisplayName`, removes reactions by that identity, and keeps non-PII structure (counts, timestamps) for integrity.
- Logs and audit metadata never include message content or external user IDs; external IDs are hashed, as in ADR-015.
- `[UNRESOLVED]` — **owner/legal decision before production (M11), not blocking S04:** a message retention period. Until it is decided, messages are retained until an erasure request. Recorded in the checkpoint and the risk register.

### Explicitly out of scope (later steps)

- Staff reply, assignment/label edits, takeover/release, 24-hour window enforcement (S05).
- **Echo messages** (seller replies sent from the Instagram app): S05. They belong with outbound sends because both must merge by provider message ID. S04's unique `(ConnectionId, ProviderMessageId)` index and `Origin = ProviderNative` already prepare for it.
- **Customer display names via the Instagram User Profile API:** needs a live token call; deferred to S05/S07. Until then the inbox shows a masked identifier.
- Inbox UI (S06); media download to R2; linking an identity to a checkout `Customer`.

## Affected files (expected)

| Area | Files |
|---|---|
| Domain | `Customers/CustomerChannelIdentity.cs`; `Conversations/{Conversation,Message,MessageReaction,ConversationLabel}.cs` + enums |
| Application | `Conversations/{IConversationIngestionService,IConversationQueryService,IConversationPrivacyService}.cs` + DTOs |
| Infrastructure | `Conversations/{ConversationIngestionService,ConversationQueryService,ConversationPrivacyService}.cs`; EF configurations; `AppDbContext` sets; DI; `WebhookProcessingService` (one call site) |
| WebApi | `Controllers/ConversationsController.cs` |
| Migration | `AddConversationsMessagesAndChannelIdentities`: 5 tables; unique and keyset indexes; tenant composite keys like existing modules |
| Contracts | `apps/web/src/lib/api/generated/openapi-v1.json` + generated TS (regenerated, not hand-edited) |
| Docs | ADR-016 (Proposed → accepted with plan approval); checkpoint `artifacts/checkpoints/M08-S04.md`; CURRENT_WORK; milestone file |

## Failure cases handled

- Duplicate or redelivered events (ADR-015 key).
- Concurrent first messages from one new customer (unique index plus retry).
- Concurrent counter updates (`xmin` plus retry).
- Out-of-order messages, receipts, and reactions.
- Receipts and reactions for unknown messages.
- Spam conversations.
- Replay of a dead-lettered event (atomic: no partial conversation rows).
- Cross-tenant IDs on every endpoint.
- Erasure idempotency.

## Tests (added with the feature)

- **Unit (domain):**
  - identity/conversation factory invariants;
  - status rules (reopen from `Resolved`/`Closed`, spam stays spam);
  - monotonic delivery status;
  - reaction last-write-wins;
  - max-timestamp counters;
  - masked customer label.
- **Integration (Testcontainers PostgreSQL, through the real ingress → processing path with signed fixtures):**
  1. First DM creates an identity, a conversation (`New`, `Automated`, store from connection), and a message.
  2. A second DM reuses both, and `UnreadCount` is 2.
  3. An exact and a re-batched duplicate produce one message.
  4. **Out-of-order:** B (later `timestamp`) is processed before A, the timeline orders A, B, and `LastMessageAt` is B's.
  5. **Out-of-order reaction:** unreact (newer) arrives before react (older), and the final state is removed.
  6. A seen receipt before any outbound message updates only the watermark; a seen receipt for an existing outbound message advances it to `Read` and never downgrades it.
  7. Concurrent first messages from the same new customer produce exactly one identity and one conversation after retry.
  8. Resolved reopens on inbound; spam stores the message without an unread increment.
  9. **Tenant isolation:** the same IGSID on two tenants' connections stays separate; cross-tenant `GET`/`POST` returns `404`; list never shows another tenant's rows.
  10. **Authorization:** unauthenticated `401`; Viewer can read but gets `403` on `POST /read`; Operator can mark read.
  11. **Atomicity:** an injected ingestion failure leaves no `InboundEvent`/`Message`/conversation rows and the webhook event `Failed` (retryable).
  12. **Erasure:** text and media nulled, reactions removed, audit row written, idempotent; Owner-only.
  13. **API:** paging, keyset timeline, filter by status/unread, preview truncation, no raw payload fields in responses.
- **Contract:** OpenAPI regenerated; generated TypeScript compiles in `pnpm ci:frontend`.

## Acceptance criteria

1. All listed tests pass. Full backend suite green; Release build 0 warnings / 0 errors.
2. EF has no pending model changes after the new migration; the migration applies cleanly to Testcontainers databases.
3. OpenAPI/TypeScript regenerated from a live local API, and the diff contains only the new conversation endpoints and schemas.
4. `pnpm ci:frontend` green; `git diff --check` clean; Testcontainers cleanup verified against the Docker baseline.
5. ADR-016 recorded; checkpoint `M08-S04.md` with actual commands and outputs; status `REVIEW`.

## Exact verification commands

```bash
dotnet restore services/api/Kreyora.slnx
dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1
dotnet test services/api/tests/Kreyora.UnitTests/Kreyora.UnitTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Conversation|FullyQualifiedName~ChannelIdentity"
dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Conversation|FullyQualifiedName~Instagram|FullyQualifiedName~WebhookProcessing"
dotnet test services/api/Kreyora.slnx --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --configuration Release --no-build
# Live local API (Testcontainers or Compose database, owner-approved) then:
pnpm generate:api
pnpm install --frozen-lockfile && pnpm ci:frontend
git diff --check
# Docker: compare resource lists against the pre-run baseline (order-insensitive)
```

## Decisions recommended in this plan

| # | Decision | Recommendation | Owner input needed? |
|---|---|---|---|
| D1 | Reuse the M05 `Customer` or make phone optional | Reuse unchanged; channel identities link to it only explicitly, later | No |
| D2 | One conversation per customer per connection vs new thread per session | One continuous thread, reopened on inbound (ADR-016) | No (approve with plan) |
| D3 | Spam handling | Stored, silent (no unread increment), never auto-reopened | No (approve with plan) |
| D4 | Echo messages | S05 | No |
| D5 | Customer display names | Masked identifier until the Profile API lookup (S05/S07) | Only if you want names visible earlier |
| D6 | Message retention period | Retain until erasure; decide the period before production | Yes, later (M11), legal/product |

## Build checklist (replaces root `task.md`)

- [x] Task 1 — ADR-016 draft; domain entities, enums, and invariants with unit tests
- [x] Task 2 — EF configurations, `AppDbContext`, migration
- [x] Task 3 — `ConversationIngestionService`; call it from `WebhookProcessingService`
- [x] Task 4 — `ConversationQueryService` + `ConversationsController` (read, mark-read)
- [x] Task 5 — `ConversationPrivacyService` (erasure) + audit
- [x] Task 6 — Integration test matrix (1–13)
- [x] Task 7 — OpenAPI/TypeScript regeneration (live local API)
- [x] Task 8 — Full gates, Docker cleanup, checkpoint `M08-S04.md` (`REVIEW`), CURRENT_WORK + milestone update

**Build notes (2026-10-05):** Task 7 regenerated the contract faithfully. The committed snapshot had drifted since M05-S05, so the diff also contains 35 previously shipped M06–M08 paths (additive only; see checkpoint). The erasure service is service-level only, with no HTTP endpoint in S04.
