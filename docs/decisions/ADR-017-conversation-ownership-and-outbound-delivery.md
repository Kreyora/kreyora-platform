# ADR-017 — Conversation Ownership, Takeover Invariant, and Outbound Delivery Semantics

- **Status:** `Accepted`
- **Date:** 2026-10-05
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** Milestone 08, Milestone 09

> Accepted by the project owner on 2026-10-05 together with the M08-S05 plan ("ok implement"); decisions Q1–Q5 in `docs/plan/M08-S05_STAFF_REPLY_TAKEOVER_PLAN.md`.

## Context

M08-S05 requires authorized staff replies through the durable outbox, assignment, human takeover with controlled release, actor/origin preservation, and provider window rules. It also requires a hard invariant: after a takeover, automation must not send. Constraints:

- The M07 outbox commits `Sending` before calling the provider (`xmin` concurrency).
- Its gate was checked only at enqueue.
- Outbound messages carried no origin or actor.
- Meta's Send API (`POST /me/messages` or `/{PAGE-ID}/messages` with a Page access token, accessed 2026-10-05) has no idempotency key. It documents a 24-hour standard window (error 1545041) and a 7-day `HUMAN_AGENT` tag that requires App Review.
- Plan §10.4 defines `bot_active ↔ human_assigned` and requires that only an authorized, explicit release resumes automation.

## Decision

1. **Origin and actor.** Every outbound message has an `Origin` (`Staff` | `Automation` | `System`) and an optional `ActorUserId`; timeline messages also carry the actor.
   - `System` covers the provider-neutral M07 outbox API and earlier rows, and keeps M07 behavior.
   - Staff replies enter the timeline immediately as pending.
   - Automation messages enter the timeline only once the provider accepts them.
2. **Conversation gate**, evaluated against committed state at enqueue **and** immediately before `MarkSending`:
   - spam conversations are blocked;
   - automation is blocked while `AutomationMode = HumanTakeover`;
   - for channels that enforce a 24-hour window: open → allowed; 24h–7d → allowed with `HUMAN_AGENT` only for staff on Instagram when `InstagramMessaging:HumanAgentTagApproved = true` (default false); otherwise denied with a stable reason code.
3. **Takeover invariant.** A takeover sets `HumanTakeover` (active threads → `HumanAssigned`) and, in the same transaction, cancels every `Queued`/`Failed` automation message of the conversation. After commit, no automation message can reach `Sending`:
   - **enqueue:** the gate reads the committed mode;
   - **an enqueue that slipped in before commit:** the delivery-time gate cancels it;
   - **a delivery that read the old mode:** its `MarkSending` save conflicts on `xmin` with the cancellation, so it does not send.

   **Sole exception:** a message whose `Sending` state committed before the takeover (an in-flight provider call cannot be recalled). It is counted in the takeover audit (`automationMessagesInFlight`).
4. **Staff reply implies takeover (Q1).** A staff reply on an automated conversation takes over in the same transaction. Release is explicit and audited, and requires `conversations.write` (Operator and above, Q4).
5. **Status actions (Q3).**
   - `resolve` and `close` from any non-spam state;
   - `reopen` only from `Resolved`/`Closed` (→ `New`);
   - `mark_spam` from any state;
   - `unmark_spam` only from `Spam` (→ `New`).

   Invalid transitions return `409 invalid_transition`; same-state actions are no-ops. Takeover and release keep `Resolved`/`Closed`/`Spam` dispositions.
6. **Delivery semantics.**
   - **Retried with the M07 backoff:** throttling (4/17/32/613, HTTP 429), provider-declared `is_transient`, and connection failures before the request was sent.
   - **Expired token (190):** marks the connection `Expired`.
   - **Outcomes that may have been processed** (timeout or reset after sending, a 5xx without an error code, a 2xx without `message_id`) are **never retried automatically**; they are recorded as `delivery_unconfirmed`. Delivery is at most once whenever the outcome is ambiguous, because a retry could double-message a customer.
7. **Echoes (Q5).** Provider echoes become `ProviderNative` timeline rows unless Kreyora already holds that provider message ID. If an echo arrives before Kreyora's send completes, the echo row is removed and the Kreyora row takes the ID in the same save. Echoes do not take over automation.
8. **Sending endpoint.** `POST /me/messages` with the stored Page access token, which resolves to that token's Page. No Page ID is persisted, and existing connections need no reauthorization.
9. **Internal notes (Q2)** are deferred.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Gate only at enqueue (M07) | Simple | Queued automation sends after takeover | Rejected: violates the invariant |
| Pessimistic `SELECT … FOR UPDATE` on the conversation during delivery | Closes the read window | Long-held locks during provider calls; deadlock risk | Rejected: optimistic `xmin` plus a re-check is sufficient and lock-free |
| Retry every non-2xx/timeout | Fewer lost messages | Duplicate customer DMs (no provider idempotency) | Rejected: at most once on ambiguity, with an explicit staff resend |
| Persist Page ID; send to `/{PAGE-ID}/messages` | Explicit page | Migration plus forced reauthorization of existing connections | Rejected: `/me/messages` is documented for Page tokens |
| Assignment implies takeover | Fewer clicks | Staff may want to monitor while automation answers | Rejected: replies imply takeover; assignment does not |

## Consequences

- **Product impact:**
  - Staff replies show immediately as pending, then sent or failed.
  - The bot stops as soon as a human replies or takes over.
  - Late replies explain the window rule.
  - Instagram-app replies appear in the inbox.
- **Architecture impact:**
  - New: `IOutboundEnqueuer` (internal, caller-authorized), `IConversationOutboundReconciler`, a real `ConversationGate` (replacing the always-allow placeholder), `IConversationReplyService`, and inbox ownership operations.
  - The Instagram provider is registered scoped (it uses the typed Graph `HttpClient`).
- **Security/privacy impact:**
  - Replies require `conversations.write`, antiforgery, and an idempotency key.
  - The Page token is decrypted only in the send path (Bearer header, never logged).
  - Audit records takeover, release, assignment, and status changes with IDs and counts only.
- **Cost/operations impact:** unconfirmed deliveries need a deliberate staff resend; the dead-letter queue shows them.
- **Migration or rollback impact:** additive migration `AddConversationOwnershipAndOutboundOrigin`: `outbound_messages.origin` (default `System`) and `actor_user_id`; `messages.outbound_message_id` (unique filtered) and `actor_user_id`; plus one index. Rollback drops them.

## Validation evidence

- `ConversationReplyIntegrationTests` (26 cases):
  - staff reply lifecycle, idempotency, denial reasons, `HUMAN_AGENT` tag, a window closing while queued;
  - takeover races: queued, slipped past, forced gate/`MarkSending` interleaving, in-flight reporting;
  - inbound-during-reply race; throttle retry; unconfirmed no-retry; token expiry;
  - echo cases, including echo-before-send merge;
  - reassignment race, membership validation, release and status machine, labels.
- `ConversationEndpointTests`: role, CSRF, and cross-tenant checks on every new route; HTTP reply.
- `InstagramSendTests`: Send API request shape and error mapping.
- `ConversationDomainTests`: state machine.

## Supersession conditions

- Meta adds idempotent sends, or changes window or tag rules.
- M09 needs automation-specific ownership states.
- Internal notes are approved.

## Correction note (2026-10-05, M08-S07) — appended

The Context section above cites "error 1545041" for the 24-hour window. Meta's Send API error reference (accessed 2026-10-05) lists the outside-window errors as `10 / 2018278` ("This message is sent outside of allowed window") and `2534022`. It lists `551 / 1545041` as "This person isn't available right now."

M08-S07 corrected the Instagram client:

- Both outside-window errors map to the stable `window_closed` reason code, with the raw Meta code kept in the diagnostic message.
- 1545041 is reported as "not available".

The decision itself is unchanged: the local gate still enforces the window before any send. See `docs/architecture/INSTAGRAM_PRODUCTION_READINESS.md` R12.
