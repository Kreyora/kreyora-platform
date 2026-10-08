# ADR-022 — Assistant conversation integration and ownership semantics

- **Status:** `Accepted` (2026-10-08, with the M09-S07 approval; was `Proposed`)
- **Date:** 2026-10-07
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** M09 (S07 integration, S08 UI/evaluation); M10 (entitlements, notifications); M12 (pilot)
- **Amends:** ADR-017 (native-app replies now take over); builds on ADR-020/021

## Context

M09-S07 connects the assistant turn (ADR-021) to real inbound messages. The milestone requires:
- checks before invocation **and again before enqueue**;
- escalation and a staff queue;
- a takeover that transactionally suppresses queued AI responses and blocks automation until an authorized release;
- race tests.

The review must approve the end-to-end ownership semantics and prove that **no bot message is sent after takeover**.

**Existing mechanics (M08-S05, ADR-017):**
- a takeover cancels queued automation messages in its own transaction;
- the gate refuses automation at enqueue and again right before delivery claims a message;
- a claim racing a takeover loses on the row version.

ADR-017 also decided that echoes of messages typed in the provider's app "never take over automation"; this ADR changes that.

## Decision

1. **Ownership:** a conversation is owned by the assistant (`Automated`) or by a person (`HumanTakeover`).
   - **It moves to a person by:** staff takeover; a staff reply (implicit); the assistant's escalation; **a seller reply typed in the Instagram app**.
   - **It moves back only by** an explicit, audited release (Owner/Admin/Operator). Release records `AutomationResumedAt`.
2. **The guarantee: no assistant message starts sending after a takeover commits.**
   - **Linearization point:** the claim (`Queued → Sending`), a single-row update guarded by the row version.
   - **The takeover**, in one transaction, sets the owner and cancels every queued/failed automation message of the conversation.
   - **The delivery job** re-reads ownership right before the claim; a takeover committing in between makes the claim fail on the row version.
   - **Therefore:** either the claim committed first (the message was already in flight; the takeover audit reports it as `automationMessagesInFlight`), or the message is cancelled / refused.
   - A turn still running when a takeover lands is stopped by the second guard pass, else by the enqueue gate, else by the delivery gate.
   - **The only exception:** the assistant's own hand-off notice (`Handoff`, ADR-021) accompanying its own escalation.
3. **Guard:** the same checks run before invoking the model and again right before enqueue:
   - connection active and able to send text;
   - customer safety (not spam, identity not erased);
   - entitlement (operator allowlist until M10 plans);
   - ownership (automated; trigger received after the last release; no newer customer message);
   - readiness.
4. **After release:** the assistant answers only customer messages received **after** the release. Earlier messages belong to the person who owned the chat.
5. **Native-app replies (amends ADR-017):** an Instagram-app echo that remains unmatched to any Kreyora send 30 s after ingestion means the seller answered from the app. The conversation is taken over (audited, trigger `native_app_reply`, automation suppressed). Our own sends are excluded by the delivery reconciler and by provider-message-ID matching.
6. **Trigger:**
   - customer messages schedule a turn **after the webhook transaction commits**, with a 4 s debounce (a burst gets one answer);
   - only shops the assistant may serve get jobs (platform switch on, shop entitled). Other shops keep the M08 behaviour, including §5 (their app replies do not take over);
   - busy turns re-schedule with backoff; crashes retry and replay by turn key;
   - a per-minute sweeper schedules unanswered messages (30-minute window) that lost their trigger. It reads only entitled shops with an enabled assistant policy, so other shops cannot crowd out the batch.
7. **Staff queue:** "needs a person" = owned by a person, not resolved/closed/spam, and the customer is waiting; oldest wait first, with the escalation reason and waiting time.
   - **Waiting since:** the assistant's escalation, while no person has answered since; otherwise the first of the customer's trailing messages.
   - **A person answering** means a staff reply or a reply typed in the Instagram app. Assistant replies and the hand-off notice do not count, so an escalated chat stays in the queue after its notice is delivered.
   - No auto-assign; in-app only (email/push alerts with M10).

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected |
|---|---|---|---|
| Hold a lock across the provider call so even in-flight sends stop | "Zero" sends after takeover in every case | Long database locks during a network call; deadlocks/timeouts under load | Rejected; in-flight messages are reported instead |
| Release re-answers waiting messages | No customer left waiting | Surprise replies to messages a person may already have handled | Rejected (Q7) |
| Ignore native-app replies (ADR-017 as before) | Simpler | The bot keeps answering while the seller types in the Instagram app | Rejected (Q6) |
| Auto-assign escalations | Clear owner | Small shops; adds rules nobody asked for | Deferred |

## Consequences

- **Product:** the assistant answers automatically. Any human action stops it at once; a seller typing in the Instagram app counts too. Release is deliberate.
- **Architecture:**
  - `AssistantSendGuard`, `IAssistantInboundHook`, `IAssistantTurnScheduler`, `AssistantTurnJob`, `AssistantTurnSweepJob`, `NativeReplyCheckJob`, `IAssistantEntitlementQuery`;
  - `conversations.automation_resumed_at`; the `needsPerson` list filter.
- **Operations:** the `assistant-turn-sweep` recurring job (every minute). The entitlement allowlist is operator configuration (`Ai:Entitlements`).
- **Migration or rollback:** additive column. Rollback = remove shops from the allowlist (the assistant stops; inbound continues as before).

## Validation evidence

Race and integration tests over PostgreSQL with the full DI host and real webhook ingress/processing:
- duplicates; bursts; AI vs staff reply;
- takeover at every stage (before invocation, during the model call, after enqueue / before claim, **between the gate read and the claim**, in flight);
- release; busy retry; crash replay; guard re-checks; native-app replies (including our own echo arriving early);
- the sweeper; the queue; isolation.

Live sandbox session (M09-S07 Task 8).

## Supersession conditions

- M10 plan entitlements and notifications.
- Multiple providers with different echo semantics.
- Quick-reply consent (ADR-020).
