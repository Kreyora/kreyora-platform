# M09-S07 — Conversation Integration, Escalation, and Takeover — Scoped Plan

## Identity

- **Milestone:** 09 — Constrained AI Assistant, RAG, and Commerce Tools
- **Step:** 07 — Conversation integration, escalation, and takeover
- **Author:** Claude (planning)
- **Date:** 2026-10-07
- **Status:** `REVIEW` — built and checkpointed 2026-10-07 (`artifacts/checkpoints/M09-S07.md`); plan approved 2026-10-07 ("ok implement"), Q1–Q10 as recommended
- **Prerequisites:** M09-S06 `APPROVED` (2026-10-07; merged `0e92635`); ADR-017, ADR-018, ADR-020, ADR-021 `Accepted`.
- **Manual work for the owner in this step** (more than recent steps, because of the live session):
  - answer the decisions below;
  - at the checkpoint, approve the **end-to-end ownership semantics**, with the "no bot message after takeover" proof (the milestone's review requirement);
  - **live sandbox session (Q9), about 45–60 minutes, on a separate go-ahead**, reusing the M08 Instagram sandbox you kept:
    - start the tunnel and API when I ask (or let me start them);
    - send about 8 made-up DMs from the tester account;
    - reply once from the Instagram app;
    - take over and release once in the Kreyora inbox.
  - **No new keys**: the Google AI Studio key from S01 and the M08 sandbox secrets are already in user secrets. No money, no Meta configuration changes.

## Milestone prompt (verbatim)

> Connect the orchestration pipeline to normalized inbound messages. Check connection capability, assistant readiness, tenant entitlement, conversation ownership, customer safety state, and rate limits before invocation and again before outbound enqueue. Implement escalation reasons and staff queue behavior. Make human takeover transactionally suppress queued AI responses and block future automation until authorized release. Add race tests for simultaneous inbound messages, AI completion, staff reply, takeover, release, duplicate events, and provider retry.

**Review checkpoint:** approve end-to-end ownership semantics and prove no bot message is sent after takeover.

## What exists today (verified 2026-10-07)

- **Inbound (M07/M08):**
  - the webhook job normalizes events (event-level dedup) and ingests customer messages into conversations **in the same transaction** (ADR-016);
  - echoes of our own sends are matched by provider message ID;
  - replies the seller sends **from the Instagram app** are stored as `ProviderNative` echoes and change nothing else.
- **Turn service (S06, ADR-021):**
  - `IAssistantTurnService.RunAsync(conversation, triggerMessage)` with lease, gate, pre-checks, budgets, data rule, bounded loop, validation, ownership re-check (automation + newer message), gated enqueue, safe fallback with the `Handoff` notice;
  - busy results aren't persisted (so a retry can run);
  - duplicate triggers replay.
  - **Nothing calls it yet.**
- **Takeover (M08-S05, ADR-017):**
  - staff takeover and staff replies (implicit takeover) cancel queued automation messages in the same transaction;
  - the enqueue- and delivery-time gate refuses automation after takeover;
  - the delivery job re-reads the gate right before claiming a message (`Queued → Sending`); a takeover that cancelled it in between wins on the row version;
  - release is explicit (Owner/Admin/Operator), audited.
- **Escalation (S05):** system takeover + category on the conversation (detail API), audit; the inbox list can filter by status/assignee/unread only.
- **Missing:**
  - the inbound → turn trigger (with debounce and retry);
  - re-checking connection/readiness/entitlement/safety before enqueue;
  - tenant entitlement (plans are M10);
  - a "needs a person" queue;
  - native-app seller replies don't stop the AI;
  - live verification of AI replies and takeover (M08 limitation L-A).

## Objective

Customer messages reach the assistant automatically and safely. A person can take over at any moment, and from that moment **no assistant message starts sending** until someone authorized releases the conversation.

## Design

### A. Inbound → turn trigger

1. **Hook:** after the webhook job commits, for each **new customer message** (not echoes, reactions or duplicates), schedule `AssistantTurnJob(tenant, conversation, message)` with a **debounce delay (Q1, 4 s)**.
   - A burst of messages becomes one answer: earlier jobs find a newer message and end `superseded` without a model call (S06).
2. **Job:** runs as the system in the tenant (`ITenantJobRunner`) and calls `RunAsync`.
   - **Busy** (another turn in this chat or shop) → re-scheduled with backoff (5 s, 10 s, 20 s; max 6 tries).
   - **Crashes** → Hangfire retry; the turn key makes retries replay, never double-send.
   - Scheduling happens **after commit** and never fails the webhook; a missed schedule is caught by a sweeper (Q2).
3. **Sweeper (Q2):** every minute, finds customer messages from the last 30 minutes with no turn and no newer message in an AI-owned chat, and schedules them. This covers lost jobs and restarts.

### B. One guard, checked twice (`AssistantSendGuard`)

The same checks run **before invocation and again right before enqueue** (the milestone requirement):

| Check | Source | Failure |
|---|---|---|
| Connection active and can send text | `ChannelConnection` status + capabilities | `blocked: connection_unavailable` (silent) |
| Assistant readiness | S02 activation (system-safe since S06) | `blocked: assistant_inactive` (silent) |
| Tenant entitlement (Q3) | `Ai:Entitlements` (operator allowlist until M10 plans) | `blocked: not_entitled` (silent) |
| Conversation ownership | automation mode = Automated; no newer customer message | `blocked: automation_paused` / `superseded` |
| Customer safety (Q4) | not Spam; identity not erased; not blocked by staff | `blocked: customer_safety` (silent) |
| Rate limits | replies/hour (policy), shop/platform daily, concurrency | S06 behavior |

### C. Ownership semantics (ADR-022, Q5) — for your approval

1. **Who owns a conversation:** the assistant (Automated) or a person (HumanTakeover). It changes only through:
   - staff takeover, a staff reply (implicit), an assistant escalation, or **a seller reply from the Instagram app (new, Q6)**;
   - and back only by **explicit release** (Owner/Admin/Operator).
2. **The guarantee: no assistant message starts sending after a takeover commits.**
   - The **linearization point is the claim** (`Queued → Sending`, a single row update with a version check).
   - The takeover cancels queued assistant messages **in the same transaction**, and the delivery gate re-reads ownership right before the claim. Either the claim committed before the takeover (the message was already in flight and is reported to staff as "in flight" in the takeover audit), or the message is cancelled / refused.
   - A turn still thinking when the takeover lands is stopped by the pre-enqueue guard, or by the enqueue gate, or by the delivery gate.
3. **The single exception:** the assistant's own hand-off notice (`Handoff`, ADR-021) for **its own** escalation.
4. **After release:** the assistant answers **the next** customer message. It does not reply to messages sent while a person owned the chat (Q7).

### D. Escalation reasons and the staff queue (Q8)

- **Reasons:** the S05/S06 categories (fixed situations, keyword, person requested, model escalation, tool unavailable / fallback, low confidence) shown on the conversation.
- **Queue:**
  - `GET /v1/conversations?needsPerson=true`: conversations owned by a person with an unanswered customer message (or escalated and not yet replied to), oldest first;
  - each item shows the reason, how long the customer has waited, and the assignee;
  - list items gain `escalationCategory` and `waitingSince`.
- **Assignment:** no auto-assign (shops are small). Anyone with inbox access can take it. The unassigned count appears in the inbox badge (S08 UI).
- **Notifications:** in-app queue only now; email or push alerts to owners/admins come with M10 notifications.

### E. Native-app seller replies (Q6)

- A `ProviderNative` echo means the seller answered from the Instagram app. To avoid confusing it with the echo of our own just-sent message, a **check job runs 30 s later**: if the echo is still unmatched to any of our outbound messages, the conversation is taken over (audited `trigger: native_app_reply`) and queued assistant messages are cancelled.

### F. Live sandbox session (Q9; separate go-ahead; owner present)

- **Setup:** the M08 sandbox shop is added to `Ai:DataPolicy:SyntheticTenantIds` and the entitlement allowlist (made-up messages only); `Ai:Enabled=true`, `Ai:Mode=Live` (Gemini free tier, synthetic content).
- **Script, from the tester account:**
  1. a product/price question → grounded AI reply;
  2. a burst of 3 messages → one reply;
  3. "talk to a person" → hand-off notice + the conversation in the queue;
  4. release in the inbox → the next message is answered by the AI;
  5. **reply from the Instagram app** → the AI stops (native takeover);
  6. take over in the inbox, then the customer writes → no AI reply;
  7. send a **shared post/reel** → capture the identifier fields (closes S04 Q6-A's open item);
  8. a hold request → proposal → "yes" → hold (S05 two-phase, live).
- **Evidence:** turn-log rows, outbound statuses, audit events. **No message text in evidence** (IDs and outcome codes only).

## Tests (real PostgreSQL; fake model; simulator channel)

- **Trigger:**
  - one webhook with a customer message → one scheduled turn → one reply;
  - echoes, reactions and read receipts schedule nothing;
  - **duplicate webhook delivery** → one message, one turn, one reply;
  - the sweeper schedules a lost trigger exactly once.
- **Simultaneous inbound messages:** 3 messages within the debounce → exactly one model turn and one reply (the others `superseded`); two conversations in one shop run concurrently within the shop cap.
- **AI completion vs staff reply:** the staff reply commits while the model is "thinking" → no automation enqueued; if already enqueued, it is cancelled; the staff message is delivered.
- **AI completion vs takeover, deterministic interleavings at each step:**
  1. before invocation;
  2. during the model call;
  3. after enqueue, before claim;
  4. after claim (in flight: delivered, reported in the audit as in flight).
  - In 1–3 **no automation message is ever claimed**. Asserted on `OutboundMessages` status and delivery attempts.
- **Release:**
  - release while a turn is queued → the queued turn (triggered before release) stays blocked;
  - the next message after release is answered;
  - Viewer can't release (403).
- **Provider retry:**
  - the turn job retried after a crash mid-turn → no duplicate reply;
  - outbound delivery retried after a transient provider error → one delivered message;
  - a busy result re-schedules with backoff.
- **Native-app reply:** a native echo with no matching outbound → takeover after the check delay; our own echo arriving before the reconciler → **no** takeover.
- **Guard re-check before enqueue:** connection deactivated, entitlement removed, conversation marked spam, identity erased mid-turn → nothing enqueued.
- **Queue:** `needsPerson` lists escalated / taken-over chats with waiting customers, oldest first; tenant-scoped; roles.
- **Isolation:** another tenant's IDs in job payloads are refused (tenant from the job envelope, conversation re-checked).

## Contracts and migrations

- **New:**
  - `AssistantTurnJob` + scheduler (`IAssistantTurnScheduler`), sweeper (recurring), native-reply check job;
  - `AssistantSendGuard`;
  - `IAssistantEntitlementQuery` (operator allowlist; M10 replaces it).
- **API (additive):** `ConversationQuery.NeedsPerson`; list items `escalationCategory`, `waitingSince`.
- **Migration:** likely none. If the queue query needs it, an additive index on `messages (conversation_id, direction, received_at)`.
- **Configuration:**
  - `Ai:Orchestration:DebounceSeconds` (4), `NativeReplyCheckSeconds` (30), `TriggerSweepMinutes` (30);
  - `Ai:Entitlements:Mode` (`AllTenants` | `Allowlist`), `AllowedTenantIds`.
- **ADR-022** (ownership semantics end to end; amends ADR-017 with the native-reply takeover). OpenAPI regenerated.

## Security and data handling

- The tenant always comes from the job envelope / tenant scope; the conversation and message are re-checked under it.
- Every send passes the guard twice and the delivery gate once more. Takeover wins every race except a message already claimed, which is reported.
- **Live session:** made-up content only, on an allowlisted sandbox shop; evidence contains IDs and codes, never message text or secrets.

## Acceptance criteria

1. Inbound customer messages trigger debounced, retry-safe turns (sweeper included). Duplicates and bursts produce one reply.
2. The guard runs before invocation and before enqueue (connection, readiness, entitlement, ownership, safety, rates).
3. Takeover (staff, staff reply, escalation, native-app reply) stops all assistant sends from the claim point; release is explicit and authorized.
4. The escalation queue works (filter, reasons, waiting time).
5. All race tests pass; full gates green; Docker at baseline.
6. The live session passes (or its gaps are documented with your decision).
7. ADR-022 and checkpoint `M09-S07.md` (`REVIEW`).

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Debounce (wait for more messages before answering) | **4 seconds**: a burst gets one answer; still feels instant on Instagram |
| Q2 | Safety net for lost triggers | **Yes:** a sweeper every minute for unanswered customer messages from the last 30 minutes |
| Q3 | Tenant entitlement before M10 plans | **Operator allowlist** (`Ai:Entitlements:Mode=Allowlist`): AI runs only for shops the platform enabled. M10 replaces it with plans |
| Q4 | Customer safety state | **No AI** for chats marked spam, erased customers, or customers blocked by staff (blocking means marking spam for now) |
| Q5 | Ownership semantics (ADR-022) | **As in Design C:** takeover wins from the claim point; already in-flight messages are reported; only the hand-off notice is exempt; explicit release |
| Q6 | Seller replies from the Instagram app | **Yes, count them as a takeover** (checked 30 s later so our own echoes don't trigger it) |
| Q7 | After release | **The AI answers the next customer message only** (no catch-up replies) |
| Q8 | Staff queue | **"Needs a person" filter + reason + waiting time; no auto-assign; in-app only** (email alerts with M10) |
| Q9 | Live sandbox session | **Yes, at the end of S07, on your go-ahead**, with the 8-message script above (about 45–60 min, made-up content) |
| Q10 | Quick-reply buttons for hold consent (ADR-020 open item) | **Not in S07.** Keep the server-checked reply rule. Revisit before the pilot, after the live session shows how customers answer |

## Out of scope

- Inbox UI for the queue, playground and turn log (S08).
- The full 72-case evaluation (S08).
- Plan-based entitlements and email/push alerts (M10).
- Paid provider selection (owner decision before the pilot).
- Shared-post product matching (built after the S07 capture).

## Build checklist

- [x] Task 1 — ADR-022; options (debounce, sweeper, native check, entitlements) + validation
- [x] Task 2 — `AssistantSendGuard` (connection, readiness, entitlement, ownership, safety, rates) in the turn service, before invocation and before enqueue
- [x] Task 3 — Post-commit trigger from webhook processing; `AssistantTurnJob` (system tenant scope, busy backoff, retry-safe); sweeper
- [x] Task 4 — Native-app reply detection (delayed check, matched-echo exclusion, audited takeover)
- [x] Task 5 — Staff queue: `needsPerson` filter, reasons, waiting time; OpenAPI
- [x] Task 6 — Race and integration tests (simultaneous inbound, AI vs staff reply, takeover interleavings, release, duplicates, provider retry, guard re-checks, native reply, queue, isolation)
- [x] Task 7 — Full gates; Docker check
- [x] Task 8 — Live sandbox session (on your go-ahead); evidence without text — partial: script steps 1, 3, 4 passed live; 2, 5, 6, 7, 8 skipped by the owner (gaps documented in the checkpoint)
- [x] Task 9 — Checkpoint `M09-S07.md` (`REVIEW`); status docs

## Build notes (2026-10-07, appended)

Deviations and findings during the build. None changes an approved decision:
- **Hook gating:** the inbound hook schedules jobs only for shops the assistant may serve (platform switch on, shop entitled). Without this, every inbound message of every shop would schedule a job. A side effect: shops without the assistant keep the M08 echo behaviour, so their app replies do not take over. Recorded in ADR-022 §6.
- **Sweeper starvation:** the first version took 200 candidates and filtered by entitlement afterwards, so messages from shops without the assistant could crowd out entitled shops. The allowlist and an enabled assistant policy are now filtered in SQL.
- **Queue definition:** "the customer spoke last" (`LastCustomerMessageAt >= LastMessageAt`) hid escalated chats once the hand-off notice was delivered, because delivery updates `LastMessageAt`. The queue now uses a waiting time:
  - since the escalation, while no person has answered since;
  - otherwise since the first of the customer's trailing messages.

  Only staff replies and Instagram-app replies count as a person answering. Recorded in ADR-022 §7.
- **Rates:** rate checks (replies per hour, daily budgets) stay in the turn's first pass (S06). The guard holds the checks that can change while the model is thinking.
- **Task 6 coverage:** 24 integration tests in `ConversationIntegrationTests` (signed webhook ingress → processing → recorded job → delivery through a recording Graph client) and 8 unit tests in `ConversationIntegrationUnitTests`. A mutation check (disabling the guard's second pass and the release cut-off) made 6 of them fail as expected.

