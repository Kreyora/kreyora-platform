# M08-S03 — Real Webhook Validation and Inbound Normalization (Instagram) — Scoped Plan

## Identity

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 03 — Real webhook validation and inbound normalization
- **Author:** Phase 1 (Architect)
- **Date:** 2026-09-22
- **Status:** `APPROVED` (2026-10-05) — live sandbox checklist carried to M08-S07
- **Prerequisites:** M08-S02 `APPROVED`; ADR-014 `Accepted`. Owner Dev-mode sandbox available for manual verification.

### Status history

| Date | Status | Note |
|---|---|---|
| 2026-09-22 | `PLANNING` | Original plan authored (sections below, unchanged). |
| not recorded in repo | approved | Owner approved this plan via an "implement" instruction to Muse Spark, per the Muse handoff and the owner's 2026-10-05 statement. The repository carried no approval entry (documentation drift). |
| 2026-09-22 | `REVIEW` | Implementation + checkpoint `artifacts/checkpoints/M08-S03.md`. |
| 2026-10-05 | `CHANGES REQUESTED` | Owner-authorized after review: findings B1–B5 confirmed and reproduced (see "Corrective scope"). |
| 2026-10-05 | `IN PROGRESS` | Owner approved the corrective scope C1–C6, the ADR-015 direction, and the recommended Q1 policy ("ok do your recommended things"). |
| 2026-10-05 | `REVIEW` | Corrective build complete; evidence in `artifacts/checkpoints/M08-S03.md` § "Corrective implementation". Live-sandbox checklist outstanding. |
| 2026-10-05 | `APPROVED` | Owner approval; live-sandbox checklist carried to M08-S07. |

## Objective

Implement Meta's official verification challenge + HMAC-SHA256 request validation in `InstagramChannelProvider`, resolve connections from IG provider identifiers, persist raw events through the unchanged M07 fast path, and normalize only verified inbound types to `v1` envelopes. Measure acknowledgement latency separately from processing. No conversation/message persistence (S04), no replies (S05), no UI (S06).

## Gate, Boundaries, and Scope

### Allowed

1. **`InstagramWebhookOptions`** (new, Infrastructure): `AppSecret` bound from environment only. **No startup hard-requirement** (would break Simulator-only CI/test factory boot): provider fails closed per-request when unconfigured.
2. **`InstagramChannelProvider : IChannelProvider`** (new, `Infrastructure/Integrations/Instagram`, singleton like Simulator):
   - `ValidateWebhookAsync`: GET → `hub.mode=subscribe`, `hub.verify_token` equals supplied secret, echo `hub.challenge`. POST → HMAC-SHA256(rawBody, AppSecret) vs `X-Hub-Signature-256` with constant-time compare; missing signature → Invalid; unconfigured AppSecret → Invalid (fail-closed). Returns `ProviderEventId = "ig_" + hex(SHA256(rawBody))` (exact redeliveries dedupe; distinct messages differ) and `ExternalAccountId` = `entry[0].id` (IGID).
   - `NormalizeInboundAsync`: `messages` → `TextMessageReceivedPayload` (quick-reply payload as text when no text body), `MediaMessageReceivedPayload` (attachments, share/story URLs); `message_reactions` → `ReactionReceivedPayload`; `messaging_seen` → `MessageStatusUpdatedPayload(Read)`; `messaging_postbacks` with no message body, `is_echo`, `is_deleted`, `is_unsupported` → **skipped** with reason (persisted raw; redacted diagnostic). Unknown fields → skipped. `SenderName` = null (Profile API is S04+; documented). `OccurredAt` from ms timestamps; tenant/connection from `RawWebhookPayload`.
   - `SendMessageAsync`, `ValidateOrRefreshConnectionAsync` → `NotSupportedException` (owned by S05 future work and the S02 graph client respectively).
3. **Connection resolution:** add default interface method `IChannelProvider.ResolveExternalAccountId(JsonDocument body)` returning null (Simulator untouched); Instagram override reads `entry[0].id` for `object == "instagram"`. Ingress tries this hook before existing header/body fallbacks.
4. **Ingress minimal change:** `HandleChallengeAsync` prefers `WebhookVerificationToken` over decrypted PAT (challenge semantics; Simulator-safe — its handler accepts secret-or-default and its tests set no credentials).
5. **Registration:** `AddSingleton<IChannelProvider, InstagramChannelProvider>` alongside Simulator.
6. **Tests:** signed fixtures with real HMAC under a test-only secret; challenge ok/mismatch; bad signature → 401 + zero event rows; unknown IGID → 404; exact redelivery → `isDuplicate`; per-type normalization mapping; echo/deleted/unsupported/postback skips; ack-latency test (signed POST → 202 + row, ms budget, decoupled from processing); AppSecret-unset fail-closed.
7. Checkpoint `artifacts/checkpoints/M08-S03.md` (`REVIEW`).

### Prohibited

- Conversation/message/customer entities or persistence (S04); replies/outbound (S05); frontend (S06).
- Changing simulator behavior, M07 dedup/fast-path semantics, or accepted ADRs.
- Real IGIDs, secrets, or personal payloads in fixtures/snapshots/logs/docs.
- `debug_token`/app-secret handling beyond HMAC verification.
- Starting M08-S04 before S03 approval.

## Test plan

- `InstagramChannelProviderTests` (unit): challenge/sign/normalize/skip matrix, HMAC vectors, fail-closed config, NotSupported stubs.
- `InstagramWebhookIngressTests` (unit, service-level): 401/404/duplicate/latency paths.
- `InstagramWebhookIntegrationTests` (Testcontainers): end-to-end signed POST → persisted raw → normalized envelopes via processing service; cross-tenant rejection.
- Owner manual sandbox checklist (not CI): Meta dashboard challenge handshake, real DM inbound, redelivery dedupe observation.

## Quality gates

- `dotnet build services/api/Kreyora.slnx --configuration Release --disable-build-servers /m:1` (0/0)
- `dotnet test services/api/Kreyora.slnx --configuration Release` (Docker available)
- `dotnet ef migrations has-pending-model-changes ... --no-build` (expect none)
- `pnpm ci:frontend` (expect green, no frontend change)
- `git diff --check`; grep proofs: AppSecret only via env/options; no secret literals in tests.

---

## Corrective scope (2026-10-05) — `CHANGES REQUESTED`

**Approval state:** approved by the owner on 2026-10-05 (C1–C6, ADR-015 direction, Q1 = recommended policy: accept `Degraded`/`Expired`; acknowledge-and-ignore `Disabled`/`Revoked`/`Pending`). Originally proposed with the note: no production code may change until the owner approves this section (and ADR-015, below). Reproduction tests and one test-isolation fix were authorized and are already in the worktree.

### Evidence sources

- Live source at HEAD `4beb641` plus the uncommitted S03 worktree.
- Official Meta documentation, accessed 2026-10-05:
  - [M1] Webhooks — Getting Started: https://developers.facebook.com/docs/graph-api/webhooks/getting-started — "Your endpoint should respond to all Event Notifications with `200 OK HTTPS`"; failed deliveries are retried "with decreasing frequency over the next 36 hours. Your server should handle deduplication"; batches of up to 1000 updates; "Multiple changes from different objects that are of the same type may be batched together"; `hub.verify_token` comes from the "Verify Token field in your app's App Dashboard".
  - [M2] Instagram Messaging — Webhooks: https://developers.facebook.com/documentation/business-messaging/instagram-messaging/webhooks — `entry.id` is the Instagram Professional account ID; the reaction payload has `mid`, `action` (react/unreact), `reaction`, `emoji` and **no own event ID**; seen events carry `read.mid`; to receive webhooks the app needs `instagram_basic`, `instagram_manage_messages`, `pages_manage_metadata`, **Published status (regardless of review status)**, and App Review for users without app roles.
  - [M3] Messenger Platform Webhooks: https://developers.facebook.com/documentation/business-messaging/messenger-platform/webhooks — the app is installed on the Page with `POST /{page-id}/subscribed_apps` using a Page access token.
- The reproduction tests below were executed against real PostgreSQL via Testcontainers.

### Findings — classification

Legend: **Confirmed** = defect proven by source reading; **Reproduced** = a failing test demonstrates it; **Unresolved** = hypothesis or fact not yet evidenced.

| ID | Finding | Classification | Evidence |
|---|---|---|---|
| B1 | The documented app callback `/v1/webhooks/instagram` cannot pass Meta's verification handshake: with no route connection there is no expected token, so the provider rejects every token. The verify token is app-level in Meta's design [M1]. | Reproduced | `B1_Http_ChallengeAtDocumentedCallback_EchoesChallengeForAppVerifyToken` → `403 {"detail":"Verification token mismatch"}` over real HTTP |
| B2a | Instagram connections are created `Active` without live validation when `PlainTextSecret`, the `Instagram` options, or both are omitted. | Reproduced (3/3 input combinations) | `B2_CreateInstagramConnection_WithoutCompleteValidationInputs_IsRejected` → success=True for each |
| B2b | Account ownership is unique only per tenant; another tenant can register the same Instagram account (service and database both accept it); ingress resolves with an unordered `FirstOrDefaultAsync` that ignores tenant filters. | Reproduced (service + database); routing nondeterminism confirmed by source | `…InSecondTenant_IsRejectedByService` → success=True; `…IsRejectedByDatabase` → persisted |
| B2c | Unsigned routing input overrides signed content: an `X-External-Account-Id` header, or a route connection ID, places a validly signed payload for account A under tenant B. | Reproduced over HTTP | forged header → `202`, 1 row under B; route ID of B → `202`, 1 row under B |
| B3 | A signed delivery with entries for two accounts is attributed entirely to the first entry's connection: tenant A received tenant B's message and B received nothing. Meta documents same-type multi-object batching [M1]. | Reproduced | `B3_MultiAccountDelivery_EachEntryReachesOnlyItsOwnTenant` → A=[mid_a, mid_b], B=[] |
| B4a | Inbound dedup uses the *referenced* message ID for reactions and seen receipts. Seen → react → unreact on one business message across deliveries keeps only the first event. | Reproduced | `B4_SeenThenReactThenUnreact…` → 1 of 3 recorded (`status` only) |
| B4b | Two envelopes with the same key in one delivery (seen + reaction on one message, or one message repeated) cause a unique violation; the whole delivery is lost. | Reproduced | outcome `threw:DbUpdateException`, 0 inbound rows |
| B4c | After any persistence failure the `catch` block re-saves the still-tracked inserts, throws again, and leaves the event in `Processing`. The job selects only `Received`/`Failed` (`WebhookProcessingJob.cs:63-65`) and replay treats `Processing` as a no-op (`WebhookProcessingService.cs:268`), so the event is stuck permanently and not visible in the DLQ. This is pre-existing M07 behavior that Instagram traffic now triggers. | Reproduced (natural duplicate and injected failure) | status=`Processing` after `threw:DbUpdateException` in 3 tests |
| B5 | `InboundEvent.ProviderMessageId` is limited to 128 characters (`InboundEvent.cs:42-44`). A longer message ID throws `ArgumentOutOfRangeException`, classified Permanent → `DeadLetter`. | Reproduced for a synthetic 201-character ID. Real Instagram `mid` length is **Unresolved** (Meta documents no maximum) — to be observed in the sandbox. | `B5_MessageIdLongerThan128Characters…` → `DeadLetter` |
| D1 | Successful ingress returns `202`; Meta asks for `200 OK` [M1]. Whether Meta treats 202 as failure: **Unresolved**. | Confirmed deviation from documented expectation | `WebhookIngressContracts.cs:26` |
| D2 | Unknown accounts → `404`, inactive connections → `403`. Meta retries failures for up to 36 hours [M1], so connections in `Degraded`/`Expired` lose inbound DMs after the retry window even though inbound signing does not depend on the Page token. | Confirmed by source + documentation | `WebhookIngressService.cs:142-154` |
| D3 | The webhook app must be **Published** to receive any webhooks, and the Page must be subscribed via `subscribed_apps` [M2][M3]. Neither is implemented or documented in the repo; the readiness doc already noted "published". | Confirmed (documentation) | — |
| U1 | Whether Meta disables an app's subscription after sustained non-200 responses. | Unresolved — not stated in [M1] | — |

Behavior that is correct and keeps passing:
- Exact redelivery dedups (`ExactRedelivery_IsDeduplicated`).
- The same message in two differently wrapped deliveries yields one inbound event (`B4_SameMessageInTwoDifferentDeliveries_ProducesOneInboundEvent`).
- A wrong verify token is rejected over HTTP (`B1_Http_…RejectsWrongToken`).
- An invalid signature persists nothing.

### Recommended corrective design

**C1 — App-level verification (B1).**
- Add `InstagramWebhookOptions.VerifyToken` (environment/user-secrets only: `InstagramWebhook__VerifyToken`).
- The Instagram GET challenge compares `hub.verify_token` to it with a constant-time compare and fails closed when it is unset. The provider ignores connection secrets for Instagram challenges.
- Revert the S03 reordering in `HandleChallengeAsync` (it only served the per-connection design), so Simulator behavior returns exactly to M07.
- *Alternatives rejected:*
  - Keep per-connection tokens via `/{connectionId}`: Meta allows one callback per app object, so this binds every account to one tenant.
  - Store the token in the database: needs a platform-settings surface; no benefit for MVP.

**C2 — Routing integrity and account ownership (B2).**
1. **Routing source.** For providers whose payload identifies the account (`ResolveExternalAccountId`/split returns non-null), ignore `X-External-Account-Id`/`X-Account-Id` headers. The Simulator keeps header routing, since its hook returns null.
2. **Account match.** After signature validation, if the provider reports an account (`WebhookValidationResult.ExternalAccountId`) that differs from the resolved connection's `ExternalAccountId`, reject (`403`, nothing persisted). This closes the route-ID path without breaking Simulator route tests: the Simulator reports the header value or null.
3. **Mandatory live validation for Instagram.**
   - Create requires `PlainTextSecret` plus `Instagram` options, with `ExternalAccountId == Instagram.InstagramAccountId`.
   - A reauthorizing update with a new secret requires options and `InstagramAccountId == connection.ExternalAccountId`. Otherwise the request fails with `ValidationError` and writes nothing.
4. **Global ownership.**
   - Replace the per-tenant unique index `(TenantId, Channel, ExternalAccountId)` with a global unique index `(Channel, ExternalAccountId)` for all channels.
   - The service duplicate check queries globally (`IgnoreQueryFilters`) and returns a neutral `409` ("already connected to a Kreyora workspace") without revealing the owning tenant.
   - Transfer path: the owning tenant deletes its connection (deletes are hard deletes, `ChannelConnectionService.cs:349`).
   - *Alternatives rejected:*
     - Per-tenant uniqueness with ingress refusing ambiguous matches: an unvalidated squatter can deny service to the real owner.
     - Uniqueness among `Active` only: every non-active status can return to `Active` (`ChannelConnection.cs:85-87`), which recreates the ambiguity.
   - *Tradeoffs:* reveals one bit (the account is connected somewhere); stale connections block transfer until deleted.
   - Existing tests reuse account IDs only within one tenant, so they are compatible; the full suite will confirm.

**C3 — Multi-account deliveries (B3).**
- Add a default interface method `IChannelProvider.SplitByAccount(JsonDocument body)` returning `null`; the Simulator is unchanged.
- The Instagram provider groups `entry[]` by `entry.id` (preserving order) and returns one slice per account: `{"object":"instagram","entry":[…raw entry text…]}`.
- Ingress, for splitting providers (no route connection):
  1. Validate the signature on the **full** body first (see C5).
  2. Resolve each slice's connection.
  3. Persist one `WebhookEvent` per known account in a single `SaveChanges`. `ProviderEventId` stays `ig_` + SHA-256(full body), unique per connection.
  4. Unknown or ignored accounts are dropped with a redacted log (hashed account ID). The delivery is acknowledged once.
- Defense in depth:
  - `RawWebhookPayload` gains optional `ExternalAccountId`; processing passes the connection's value.
  - The Instagram normalizer skips entries for any other account.
- *Alternatives rejected:*
  - Storing the full body under each tenant: copies other tenants' customer PII into each tenant's raw log.
  - Filtering only in the normalizer: other tenants' messages are lost.
- *Consequence:* the stored raw payload is a per-account slice of the verified body, not byte-identical to the request. This needs an ADR-012 note: verification remains at ingress.

**C4 — Inbound event identity and transaction recovery (B4, B5).**
1. Add optional `NormalizedInboundEnvelope.DeduplicationKey`. This is a non-breaking optional field, so the envelope stays `v1` under ADR-011 §3. Instagram keys:
   - message: `msg:{mid}` (multi-part `msg:{mid}:p{i}`)
   - seen: `read:{mid}:{senderId}`
   - reaction: `reaction:{mid}:{senderId}:{action}:{timestamp}`
   - fallback: `raw:{sha256(item JSON)}`

   Redeliveries carry identical fields, so they dedup; react/unreact/re-react differ by action/timestamp.
2. `InboundEvent` gains a `DeduplicationKey` column storing SHA-256 hex of the key (fixed 64 characters):
   - unique index `(ConnectionId, DeduplicationKey)`;
   - the unique `(ConnectionId, ProviderMessageId)` index becomes non-unique (S04 lookups and status receipts still use the referenced `mid`);
   - `ProviderMessageId` widens to 512 (B5);
   - the Simulator and legacy key is `ProviderMessageId ?? EventId`, identical to current semantics;
   - backfill existing rows as `ProviderMessageId ?? Id`.
3. Processing keeps an in-batch `HashSet` of keys besides the database check.
4. **Recovery:**
   - On any exception, `ChangeTracker.Clear()`, re-load the event, `RecordFailure`, and save. A unique violation stays Transient, so the retry dedups against the committed row.
   - The job also reclaims events left in `Processing` with `ModifiedAt` older than a 15-minute lease (covers crashes), reusing the existing column, so no migration is needed for this.
5. *Alternatives rejected:*
   - Composite key in the existing column: mixes semantics and keeps the 128-character risk.
   - Envelope `EventId` as the key: the Simulator's random IDs would break its dedup.
   - `INSERT … ON CONFLICT` raw SQL: bypasses EF tenant enforcement.

**C5 — Documented delivery semantics (D1, D2).**
- Default interface methods on `IChannelProvider`, preserving Simulator behavior:
  - `UsesConnectionSecretForSignature` (default `true`; Instagram `false`) — Instagram validates the signature **before** connection lookup and never decrypts the Page token on webhook POSTs;
  - `AcknowledgementStatusCode` (default `202`; Instagram `200`, per [M1]).
- Invalid signature: `401`.
- Valid signature for an unknown account: `200 {"status":"ignored"}`, not persisted, with a redacted warning log.
- Connection-status policy — **owner product decision requested** (Q1). Recommended:
  - `Active`/`Degraded`/`Expired`: accept and persist (customer messages are not lost while the seller reauthorizes);
  - `Disabled`/`Revoked`/`Pending`: `200`, not persisted (data minimization).

**C6 — Low-severity items in scope.**
- Instagram skip diagnostics via `ILogger` (event type and reason only, no content).
- `occurredAt` falls back to `ReceivedAt` instead of `UtcNow`.
- Remove the duplicate body parse and bare `catch` blocks in ingress.
- Replace the stale S01 contract test `Registry_HasNoInstagramAdapter_Yet` with a registry-resolution assertion.
- Switch the S03 test helper to validated creation (`FakeInstagramGraphHandler`) or a direct entity insert.

**Out of scope (recorded, not built):**
- Automating `subscribed_apps` at connect time (outbound Meta mutation; candidate for S07 or a connection follow-up).
- `InstagramGraphOptions` lacks `ValidateOnStart` despite the S02 checkpoint wording.
- Compose environment pass-through for Instagram and encryption settings (only if the owner chooses Compose for the sandbox).
- App Review and business verification.

### Migration and ADR impact

- **Migration** (one EF migration, e.g. `M08S03_InstagramRoutingAndInboundIdentity`):
  - `channel_connections`: drop unique `(tenant_id, channel, external_account_id)`; add unique `(channel, external_account_id)`. Preceded by a guard statement that fails with the duplicate **count only** if cross-tenant duplicates exist.
  - `inbound_events`: add `deduplication_key` (nullable) → backfill → `NOT NULL` → unique `(connection_id, deduplication_key)`; make `(connection_id, provider_message_id)` non-unique; widen `provider_message_id` to 512.
  - Local and test databases only; no production deployment exists. It runs through the existing controlled migrator, never at API startup.
- **ADR-015 (Proposed, owner acceptance required before build):** "Social account ownership, webhook account fan-out, and inbound event identity". It amends:
  - ADR-010: global unique ownership and routing precedence (signed payload > headers; route ID must match the signed account);
  - ADR-012: stored raw payload is the per-account slice of a verified delivery;
  - ADR-011: records the optional `DeduplicationKey` (non-breaking).

  ADR-010/011/012 receive an appended "Amended by ADR-015" note; their text is not rewritten.
- **ADR-014 wording:** append a dated status note. It is `Accepted` on the owner's 2026-09-22 decision; the "Decision (proposed)", "No adapter code in S01", and "observed sandbox webhook" acceptance lines are historical S01 text, and the sandbox webhook remains outstanding. Nothing is rewritten in place.

### Corrective acceptance criteria

1. All 12 skipped reproduction tests in `InstagramWebhookCorrectiveReproTests` are un-skipped and pass unchanged in intent.
2. Over real HTTP:
   - the challenge at `/v1/webhooks/instagram` with the configured app token returns `200` and the challenge;
   - a wrong or missing token, or an unset `VerifyToken`, returns `403`.
3. Instagram creation without complete validation inputs writes nothing; a second tenant cannot own the same account (service `409`, database unique violation).
4. Forged headers or a mismatched route ID never persist a payload under a non-matching connection.
5. A multi-account signed delivery yields one `WebhookEvent` per known account; each tenant sees only its own entries, raw slices included.
6. Seen/react/unreact sequences are fully recorded:
   - same-batch duplicates are processed once;
   - an injected persistence failure leaves the event `Failed` (retryable) or `DeadLetter`, never `Processing`;
   - stale `Processing` events are reclaimed by the job.
7. A 201-character message ID processes successfully.
8. An accepted Instagram delivery returns `200`; a valid signature for an unknown account returns `200 ignored` with zero rows; an invalid signature returns `401` with zero rows.
9. Every Simulator and M07 suite passes without modification, and all existing S01–S03 tests pass (S03 helper adjusted per C6).
10. Release build 0 warnings / 0 errors; full backend suite green; EF has no pending model changes after the new migration; `pnpm ci:frontend` green; `git diff --check` clean.
11. Testcontainers cleanup verified against the recorded Docker baseline.
12. Checkpoint updated with the commands and outputs actually run. Live-sandbox items are recorded as executed or outstanding, never assumed.

### Exact verification commands

```bash
dotnet restore services/api/Kreyora.slnx
dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1
dotnet test services/api/tests/Kreyora.UnitTests/Kreyora.UnitTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Instagram"
dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~InstagramWebhookCorrectiveReproTests|FullyQualifiedName~InstagramWebhookIntegrationTests|FullyQualifiedName~InstagramConnectionIntegrationTests|FullyQualifiedName~WebhookIngressIntegrationTests|FullyQualifiedName~WebhookProcessing"
dotnet test services/api/Kreyora.slnx --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --configuration Release --no-build
pnpm install --frozen-lockfile && pnpm ci:frontend
git diff --check
# Docker: compare `docker ps -a`, `docker images`, `docker volume ls`, `docker network ls` against the pre-run baseline (order-insensitive)
```

No API route or response-schema change is planned (webhook bodies stay `{"status":…}`), so OpenAPI/TypeScript regeneration is expected to be unnecessary. This will be confirmed by diffing the live OpenAPI snapshot during implementation.

### Manual sandbox prerequisites (after the corrective build is approved and green)

Each item needs the owner's account access or separate authorization. Secrets are configured by name only and never pasted into chat or files.

1. Meta app switched to **Published/Live** [M2]. Before App Review, only app-role users' events are delivered.
2. Webhooks product → Instagram object:
   - callback `https://<tunnel-host>/v1/webhooks/instagram`;
   - verify token = the value configured as `InstagramWebhook__VerifyToken`;
   - fields `messages`, `messaging_seen`, `message_reactions`.

   The tunnel needs separate authorization.
3. Page installed on the app: `POST /{page-id}/subscribed_apps?subscribed_fields=messages,messaging_seen,message_reactions` with the Page access token (Graph Explorer, owner) [M3].
4. API process receives `InstagramWebhook:AppSecret`, `InstagramWebhook:VerifyToken`, and the existing `SecretEncryption:*` keys.
   - **Recommended:** host `dotnet run` (Development, user secrets; the encryption configuration is preserved as-is).
   - Compose currently forwards none of these to the `api` service, so it would need an approved env pass-through.
5. Connection created through the API with `PlainTextSecret` (Page token) + `Instagram.PageId` + `Instagram.InstagramAccountId`.
6. An app-role tester account sends a DM, a reaction, and lets a business message be seen.
7. Record, without identifiers or content:
   - handshake status;
   - delivery status code;
   - `WebhookEvent`/`InboundEvent` counts and types;
   - observed `mid` length (closes B5-Unresolved);
   - whether any redelivery was observed (it cannot be forced; record "not observed" if so).

---

## Build checklist (moved from root `task.md` on 2026-10-05)

Root `task.md` was retired on 2026-10-05: it duplicated status from `docs/context/CURRENT_WORK.md` and scope from this plan, and it was overwritten every step. Its last content is preserved below verbatim, with heading levels lowered. Status is tracked only in `CURRENT_WORK.md`, the milestone file, and the checkpoint.

## 1. Overview

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 03 — Real webhook validation and inbound normalization
- **Status:** `REVIEW` (corrective build complete 2026-10-05)
- **Phase:** Corrective build complete — awaiting owner review + manual sandbox checklist (owner approved C1–C6, ADR-015 direction, and Q1 recommended policy on 2026-10-05)
- **Governing Plan:** `docs/plan/M08-S03_WEBHOOK_NORMALIZATION_PLAN.md` (original scope + "Corrective scope (2026-10-05)")
- **Active Milestone File:** `docs/milestones/08_FIRST_SOCIAL_CHANNEL_AND_INBOX.md`
- **Previous Checkpoint:** `artifacts/checkpoints/M08-S02.md` (APPROVED 2026-09-22)
- **Current Checkpoint:** `artifacts/checkpoints/M08-S03.md` (review outcome appended 2026-10-05)

#### 2. Original builder checklist (implemented 2026-09-22 by Muse Spark; plan approved via owner "implement" instruction — approval entry was missing from the repo)

- [x] Task 1: `InstagramWebhookOptions` (env-bound AppSecret, fail-closed per-request)
- [x] Task 2: `InstagramChannelProvider` — challenge + HMAC validation, body-hash dedup ID, IGID extraction, v1 normalization with documented skips, NotSupported outbound/health stubs; singleton registration
- [x] Task 3: Connection resolution — `ResolveExternalAccountId` DIM hook + ingress fallback; challenge-secret preference (verify token first)
- [x] Task 4: Tests — signed-fixture unit/integration matrix + ack-latency test
- [x] Task 5: Quality gates & review checkpoint (results as reported by Muse Spark on 2026-09-22)

#### 3. Review and reproduction (done 2026-10-05, authorized test-only changes)

- [x] Verified B1–B5 against source and official Meta docs; classification table in the plan
- [x] Added `services/api/tests/Kreyora.IntegrationTests/Integrations/InstagramWebhookCorrectiveReproTests.cs` (12 skipped repros + 2 passing guards)
- [x] Fixed the order-dependent assertion in `InstagramWebhookIntegrationTests.InvalidSignature_CreatesNoTrustedEvent` (scoped to its connection)
- [x] Full backend suite run; Testcontainers cleanup verified against the Docker baseline

#### 4. Corrective implementation checklist (approved and completed 2026-10-05)

- [x] **C0** — ADR-015 written and recorded `Accepted` (owner approval 2026-10-05), plus appended amendment notes on ADR-010/011/012 and a status note on ADR-014
- [x] **C1** — App-level `InstagramWebhook:VerifyToken`; constant-time challenge; revert ingress challenge-secret reorder (Simulator back to M07 behavior)
- [x] **C2** — Payload-first routing (headers ignored when the provider resolves the account); post-validation account match; mandatory Instagram live validation on create/reauthorize; global `(Channel, ExternalAccountId)` ownership + neutral 409
- [x] **C3** — `SplitByAccount` DIM; per-account `WebhookEvent` slices in one transaction; normalizer account filter via optional `RawWebhookPayload.ExternalAccountId`
- [x] **C4** — `DeduplicationKey` on envelope + `inbound_events` (hashed, unique per connection); in-batch guard; `ChangeTracker.Clear()` recovery; 15-min stale-`Processing` reclaim; `provider_message_id` widened to 512
- [x] **C5** — `UsesConnectionSecretForSignature` / `AcknowledgementStatusCode` DIMs (Instagram: signature-before-lookup, 200 ack); unknown account → 200 ignored; connection-status policy per owner decision Q1
- [x] **C6** — Skip diagnostics (no PII); `ReceivedAt` fallback; ingress parse cleanup; replace stale S01 contract test; S03 helper uses validated creation
- [x] **C7** — Migration `M08S03_InstagramRoutingAndInboundIdentity` with duplicate guard; un-skip all repros; full gates (plan "Exact verification commands"); checkpoint update; status → `REVIEW`

#### 5. Open owner inputs

- **Q1 (product intent) — decided 2026-10-05:** accept `Degraded`/`Expired`; acknowledge (200) and do not store `Disabled`/`Revoked`/`Pending`.
- **Q2 (account access, needed at sandbox time — next):** ability to switch the Meta app to Published/Live and subscribe the Page via `subscribed_apps`.
