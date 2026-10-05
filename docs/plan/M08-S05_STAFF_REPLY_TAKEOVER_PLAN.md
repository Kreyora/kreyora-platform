# M08-S05 — Staff Reply, Assignment, and Human Takeover — Scoped Plan

## Identity

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 05 — Staff reply, assignment, and human takeover
- **Author:** Claude (planning)
- **Date:** 2026-10-05
- **Status:** `APPROVED` (2026-10-05) — implemented 2026-10-05 (evidence: `artifacts/checkpoints/M08-S05.md`); approved by the owner on 2026-10-05 ("ok implement"): decisions Q1–Q5 as recommended, ADR-017, read-only Meta documentation access
- **Prerequisites:** M08-S04 `APPROVED` (2026-10-05); ADR-010–016 `Accepted`.
- **Manual work for the owner in this step:** none. Real sends to Instagram happen only at M08-S07 (sandbox).

## Milestone prompt (verbatim)

> Implement authorized staff reply through the durable outbound pipeline, conversation assignment/unassignment, labels, internal notes if approved, human takeover, and controlled release back to automation. The takeover transition must immediately block new automated outbound messages and cancel or suppress queued-but-not-sent AI replies safely. Preserve actor/origin and audit metadata. Enforce provider capability/window rules with clear denial reasons. Add race tests for inbound message, staff reply, AI enqueue placeholder, takeover, provider retry, and reassignment.

**Review checkpoint:** approve takeover invariant, staff reply, provider-window behavior, and audit trail.

## What exists today (verified 2026-10-05)

- **M07 outbox** (`OutboundMessage`, `OutboundMessageService`, `OutboundDeliveryJob`):
  - idempotent enqueue per `(tenant, connection, idempotency key)`;
  - `ProcessDeliveryAsync` commits `Sending` (with an `xmin` token) **before** calling the provider;
  - `Cancel` is allowed only from `Queued`/`Failed`;
  - retries, DLQ, replay, delivery attempts.
- `OutboundMessage` has **no origin or actor**, and its `ConversationId` is free text.
- `IConversationGate` is checked only at **enqueue**, and the registered implementation is `AlwaysAllowConversationGate`.
- `InstagramChannelProvider.SendMessageAsync` throws `NotSupportedException` (S03).
- S02 validates `PageId` at connect time but **does not persist it**. The S01 evidence (Send API, source S6) uses `POST /{PAGE-ID}/messages` with the customer's IGSID as recipient and the Page access token.
- S03 skips `is_echo` messages, so seller replies typed in the Instagram app never reach the timeline.
- S04 `Conversation` already stores the fields this step operates on: `AutomationMode`, `AssignedUserId`, labels, and `LastCustomerMessageAt` (window input). `Message` has `Origin` and `DeliveryStatus` (`null` = pending). `Message.CreateOutboundText` exists.
- `InstagramWindowEvaluator` (S01) maps the last customer action to Open / HumanAgentEligible (24h–7d) / Exhausted.
- Permissions `conversations.read` / `conversations.write` exist; `Membership` (tenant, user, role, status) exists for validating assignees.

## Objective

Let authorized staff reply from Kreyora through the durable outbox; assign, unassign and label conversations; take over from and release back to automation; and change conversation status. All of this must enforce a takeover invariant that no automated message can start sending after a takeover commits, keep actor/origin on every outbound message, and deny out-of-window or unsupported sends with clear reasons. Also record seller replies made directly in Instagram (echoes) in the timeline, without duplicating Kreyora-sent messages.

## Design

### 1. Origin, actor, and timeline linkage (migration)

- `OutboundMessage` adds:
  - `Origin` (`Staff` | `Automation` | `System`), required; existing rows backfill as `System`;
  - `ActorUserId?`.
- `ConversationId` must reference a conversation of the same tenant whenever it is set (checked at enqueue).
- `Message` adds `OutboundMessageId?` (unique where not null) and `ActorUserId?`.
- `ChannelConnection` adds `ProviderPageId?`, captured from validated Instagram options on create/reauthorize.
  - Existing Instagram connections have none, so sending is denied until they reauthorize (clear reason).
- **Timeline policy:**
  - A **staff** reply creates its timeline `Message` immediately as pending (`DeliveryStatus = null`, no provider ID), in the same transaction as the `OutboundMessage`. This enables S06's optimistic UI with durable reconciliation.
  - **Automation** messages appear in the timeline only when the provider accepts them, so takeover never leaves cancelled AI rows in the conversation.

### 2. Staff reply

- `POST /v1/conversations/{id}/replies` with body `{ text }` and a required `Idempotency-Key`.
  - Requires `conversations.write` and antiforgery.
  - The same key returns the same result (M07 idempotency); no second message.
- **Checks, in order, each with a stable machine-readable reason in an RFC 7807 problem:**
  1. Conversation exists in the tenant → otherwise `404`.
  2. Status is not `Spam` → `conversation_is_spam`.
  3. Connection accepts sending (`Active`) → `connection_inactive`.
  4. Connection has a page ID (Instagram) → `connection_reauthorization_required`.
  5. Capability `CanSendText` → `capability_unsupported`.
  6. Text is non-empty and within the provider limit → `text_too_long`.
     - `[UNRESOLVED]` exact Instagram limit; the plan uses a conservative 1,000 characters, verified against current docs during the build.
  7. **Window:** `InstagramWindowEvaluator` on `LastCustomerMessageAt`.
     - `Open` → allowed.
     - `HumanAgentEligible` → denied with `window_closed_human_agent_unavailable` while `Instagram:HumanAgentTagApproved = false` (default; needs Meta App Review). When approved, sent with the `HUMAN_AGENT` tag.
     - `Exhausted` or `Unknown` → `window_closed`.
- **Ownership:** a staff reply on an `Automated` conversation performs an implicit takeover in the same transaction (ADR-017; decision Q1). Automation therefore never talks over a human.
- **Actor:** `ActorUserId` is set from the verified tenant context on both `OutboundMessage` and `Message`.

### 3. Delivery (send path)

- `IInstagramGraphClient.SendTextAsync(pageAccessToken, pageId, recipientIgsid, text, tag?)`.
  - Bearer header; versioned path from `InstagramGraphOptions`; no token in URLs or logs (S02 conventions).
  - Returns the provider `message_id` or a typed error.
- `InstagramChannelProvider.SendMessageAsync` decrypts the Page token (ADR-013) and calls the client. Outbound media sending remains unsupported (text only in S05; denied with `capability_unsupported` for media).
- **Delivery-time gate (new):** `ProcessDeliveryAsync` re-checks the gate **before** `MarkSending`, in the same save:
  - an automation message on a `HumanTakeover` conversation is cancelled, never sent;
  - any message whose window closed while queued is failed permanently with `window_closed`.
- **Error mapping:**
  - Meta throttling (codes 4/17/32/613, HTTP 429) or `is_transient: true` → Transient (retry with M07 backoff).
  - 190 (token) → connection `Expired` health + Permanent.
  - 10/200 (permission) → Permanent.
  - **Ambiguous outcomes** (timeout or connection reset after the request was written) → Permanent with code `delivery_unconfirmed`, never auto-retried. The Send API has no idempotency key, so an automatic retry could double-message a customer; staff can resend deliberately (ADR-017).
  - `[UNRESOLVED]` exact window and recipient error subcodes, to be closed from docs during the build and from sandbox traffic at S07.
- **Reconciliation on success:**
  - the staff `Message` gets the provider ID and `DeliveryStatus = Sent`;
  - for automation, a new outbound `Message` is created with the provider ID;
  - on permanent failure the staff `Message` becomes `Failed` (the M07 retry/replay controls still apply).

### 4. Echo messages (seller replies sent from the Instagram app)

- S03 normalization is extended. An `is_echo` message becomes a text/media payload with optional v1 fields `IsEcho = true` and `RecipientChannelId` (the customer). These are non-breaking per ADR-011 §3.
- **Ingestion:**
  - if a `Message` with that provider ID exists → no-op (it was sent from Kreyora);
  - otherwise → find the identity by recipient and append an outbound `Message` with `Origin = ProviderNative` and `DeliveryStatus = Sent`. This updates `LastMessageAt` only (no unread count, no `LastCustomerMessageAt`).
- **Echo arrives before the send completes:** on success, if an echo row already holds the provider ID, the echo row is removed and the staff/automation row takes the ID in the same transaction. The result is one timeline entry; reactions follow by provider ID (no foreign key).
- An echo is **not** treated as a takeover (decision Q5).

### 5. Takeover, release, assignment, labels, status

| Route (all `conversations.write` + antiforgery) | Effect | Audit |
|---|---|---|
| `POST /{id}/takeover` | `AutomationMode = HumanTakeover`; status → `HumanAssigned` (unless `Spam`/`Resolved`/`Closed`). **In the same transaction**, every `Queued`/`Failed` automation `OutboundMessage` of the conversation becomes `Cancelled`. Idempotent | `conversations.takeover` (actor, cancelled count) |
| `POST /{id}/release` | `AutomationMode = Automated`; status → `BotActive` (unless `Spam`/`Resolved`/`Closed`). Explicit only | `conversations.released` |
| `POST /{id}/assign` `{ userId }` | Assignee must be an **active member of this tenant**; sets `AssignedUserId`/`AssignedAt`. Does not change automation (Q1 covers replies, not assignment) | `conversations.assigned` (actor, previous and new assignee IDs) |
| `POST /{id}/unassign` | Clears the assignee | `conversations.unassigned` |
| `PUT /{id}/labels` `{ labels[] }` | Replaces the label set (≤ 20 labels, ≤ 48 characters, trimmed, case-insensitive dedup) | none (low sensitivity) |
| `POST /{id}/status` `{ action }` | `resolve` / `reopen` / `close` / `mark_spam` / `unmark_spam` per ADR-017; invalid transitions → `409` with reason | `conversations.status_changed` |

- Concurrent edits use the conversation's `xmin` token. A lost race returns `409 conversation_changed` (refresh and retry) and never applies silently.
- **Internal notes:** not included (decision Q2: defer).

### 6. Takeover invariant (ADR-017)

1. After a takeover commits, **no automation message can reach `Sending`**:
   - **enqueue:** the gate reads the committed `AutomationMode` and denies;
   - **a racing enqueue that slipped in before the commit:** the delivery-time gate cancels it before `MarkSending`;
   - **a delivery job that read "Automated" just before takeover:** its `MarkSending` save conflicts on `xmin` with the takeover's cancellation, so the job does not send.
2. **The only permitted exception:** a message whose `Sending` state committed *before* the takeover (an in-flight HTTP call cannot be recalled). It is reported in the takeover result and audit, never hidden.
3. Release resumes automation only through the explicit, audited action.

### 7. AI enqueue placeholder

- `IConversationReplyService.EnqueueAutomationReplyAsync(conversationId, text, idempotencyKey)` has no endpoint. It exists for M09 and the race tests, and uses the same checks as staff (plus the requirement `AutomationMode = Automated`), with `Origin = Automation`.

## Contracts

- **New routes:** `replies`, `takeover`, `release`, `assign`, `unassign`, `labels`, `status` under `/v1/conversations/{id}`.
  - Responses: `ConversationDetailItem` (and `MessageItem` for replies).
  - Problem `type` values carry the denial reasons above.
- OpenAPI and TypeScript regenerated with the live-local-API workflow (throwaway database, as in S04).
- **Migration** `AddConversationOwnershipAndOutboundOrigin` (expand-only):
  - `outbound_messages.origin` (default `System` for existing rows) and `actor_user_id`;
  - `messages.outbound_message_id` (unique filtered) and `actor_user_id`;
  - `channel_connections.provider_page_id`.
- **ADR-017 (Proposed, accepted with this plan):**
  - takeover/release/status state machine;
  - staff-reply implicit takeover;
  - enqueue- and delivery-time automation gate with the documented in-flight exception;
  - at-most-once delivery for ambiguous provider outcomes;
  - window policy with `HUMAN_AGENT` gated by approval.

## Security, tenancy, audit

- Every route uses the verified tenant context and explicit tenant predicates. Assignees are validated as active members of the same tenant. Other tenants' IDs return `404`.
- **Audit:** takeover, release, assignment, unassignment, status changes. Metadata holds IDs and counts only, no message content.
- **Page token:** decrypted only inside the send path; Bearer header only; never logged.
- **Replies are not audited individually.** They carry `ActorUserId` and `Origin` on both records instead (the volume would swamp the audit log; actor and origin preserved as the prompt requires).

## Tests

- **Unit:**
  - ADR-017 state machine (every allowed and denied transition);
  - window denial reasons for each `InstagramWindowState`;
  - Meta error classification (throttle, transient, token, permission, ambiguous);
  - label normalization;
  - echo normalization;
  - `SendTextAsync` request shape (Bearer header, versioned path, no token in URL, `HUMAN_AGENT` tag only when enabled), using the S02 fake HTTP handler.
- **Integration (Testcontainers PostgreSQL; signed fixtures; stubbed Graph HTTP):**
  1. Staff reply happy path: pending timeline row → delivery → provider ID + `Sent`; actor on both records; implicit takeover.
  2. Idempotent reply: same key yields one outbound message and one timeline row.
  3. Denials: spam, inactive connection, missing page ID, media, too long, window closed (24h+), `HUMAN_AGENT` unavailable. Each writes nothing.
  4. A window that closes while queued fails permanently at delivery with `window_closed`.
  5. **Race: takeover vs queued automation** → cancelled, provider never called.
  6. **Race: automation enqueue concurrent with takeover** → never sent (delivery-time gate).
  7. **Race: delivery `MarkSending` vs takeover cancel** (forced interleaving) → exactly one wins; never `Cancelled` then `Sent`; any in-flight exception is reported in the audit.
  8. **Race: inbound message during a staff reply** → both persist; unread, `LastCustomerMessageAt` and the reply row are all correct (`xmin` retry).
  9. **Provider retry:** throttle then success → one provider call succeeds, one timeline row, attempts recorded. Ambiguous timeout → `delivery_unconfirmed`, no automatic retry, staff row `Failed`.
  10. **Echo:** an Instagram-app reply appears as `ProviderNative`; the echo of a Kreyora send is ignored; echo-before-send-completion merges into one row.
  11. **Race: concurrent reassignment** → one succeeds, the other gets `409`; the final assignee matches the last audit event.
  12. Assignment to a non-member, suspended member, or another tenant's user → `422`/`404`; nothing written.
  13. Release resumes automation; automation enqueue succeeds afterwards. Status machine over HTTP; invalid transition → `409`.
  14. Authorization: Viewer gets `403` on every write; unauthenticated `401`; missing CSRF `400`; cross-tenant `404`.
- **Contract:** regenerated OpenAPI; `pnpm ci:frontend` compiles.

## Acceptance criteria

1. All tests above pass; full backend suite green; Release build 0/0; EF has no pending changes after the migration.
2. The takeover invariant is demonstrated by races 5–7, with no flaky retries needed. Each race runs with forced interleaving, not sleeps.
3. Every denial has a stable reason code, and the checkpoint documents them.
4. OpenAPI/TypeScript regenerated (additive); `pnpm ci:frontend` green; `git diff --check` clean; Docker back to baseline.
5. ADR-017 recorded; checkpoint `M08-S05.md` with actual outputs; status `REVIEW`.

## Exact verification commands

```bash
dotnet restore services/api/Kreyora.slnx
dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1
dotnet test services/api/tests/Kreyora.UnitTests/Kreyora.UnitTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Conversation|FullyQualifiedName~Instagram|FullyQualifiedName~Outbound"
dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Conversation|FullyQualifiedName~Outbound|FullyQualifiedName~Instagram"
dotnet test services/api/Kreyora.slnx --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --configuration Release --no-build
# live local API on a throwaway Postgres container, then:
pnpm generate:api
pnpm install --frozen-lockfile && pnpm ci:frontend
git diff --check
# Docker: compare resource lists with the baseline; remove only resources created by this run
```

## External documentation (read-only, during the build)

Before coding the send path, verify against current official Meta documentation (public pages only; no credentials):
- the Instagram Send API endpoint and payload;
- the response `message_id`;
- the text length limit;
- the `HUMAN_AGENT` tag format;
- throttling, transient, and window-error codes.

Evidence will be cited in the checkpoint. **Approval of this plan includes authorization for that read-only documentation access.**

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Does a staff reply automatically take over from automation? | **Yes.** Prevents the bot replying over a human; release stays explicit |
| Q2 | Internal notes ("if approved" in the prompt) | **Defer.** Not needed for the M08 exit gate; small follow-up later |
| Q3 | Include status actions (resolve/reopen/close/spam) in S05 | **Yes.** S06's daily workflow needs them; defined in ADR-017 |
| Q4 | Who may release automation back | **Operator and above** (`conversations.write`), audited |
| Q5 | Does a reply typed in the Instagram app (echo) take over automation? | **No.** Recorded only; revisit at M09 when automation exists |

## Out of scope

- Internal notes (Q2); outbound media; templates.
- `HUMAN_AGENT` until Meta approval; profile-name lookup (S07).
- Inbox UI (S06); real Instagram sends (S07 sandbox); AI generation (M09).

## Build checklist

- [x] Task 1 — Read-only Meta doc verification (send endpoint, limits, tag, errors); record evidence
- [x] Task 2 — ADR-017; domain changes (state machine, origin/actor, `ProviderPageId`) + unit tests
- [x] Task 3 — Migration `AddConversationOwnershipAndOutboundOrigin`
- [x] Task 4 — `SendTextAsync` + provider `SendMessageAsync` + error mapping (stubbed HTTP tests)
- [x] Task 5 — Real `ConversationGate` (enqueue + delivery-time); delivery reconciliation to timeline
- [x] Task 6 — `IConversationReplyService` (staff reply, automation placeholder), takeover/release/assign/labels/status services + controller routes + audit
- [x] Task 7 — Echo normalization + ingestion + merge
- [x] Task 8 — Integration and race test matrix (1–14)
- [x] Task 9 — OpenAPI/TS regeneration; full gates; Docker cleanup; checkpoint `M08-S05.md` (`REVIEW`); status docs

**Build notes (2026-10-05):** Task 1's documentation check found `POST /me/messages` documented for Page tokens. The `ProviderPageId` column and forced reauthorization were therefore dropped (deviation recorded in the checkpoint and ADR-017 §8). The staff reply retries once on a row-version conflict, so an inbound message landing mid-reply does not fail it.
