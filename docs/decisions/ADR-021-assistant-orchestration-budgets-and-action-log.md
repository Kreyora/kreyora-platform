# ADR-021 — Assistant orchestration, budgets, safe fallback and action log

- **Status:** `Accepted` (2026-10-07, with the M09-S06 approval; was `Proposed`)
- **Date:** 2026-10-07
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** M09 (S06 orchestration, S07 conversation integration, S08 UI/evaluation); M10 (plan budgets); M11 (operations)
- **Amends:** ADR-017 (adds the `Handoff` outbound origin)

## Context

M09-S06 adds the loop that turns a customer message into an assistant reply. It must be bounded (iterations, time, tokens, cost, concurrency), fail safe, check its output against ownership and content rules before sending, and keep a redacted log. It must also show evidence of the kill switches.

**Constraints:**
- **ADR-018:** free-tier models may receive synthetic data only; the platform switch `Ai:Enabled`; the data policy refuses personal data unless a paid no-training provider is approved.
- **ADR-020:** the AI action matrix.
- **ADR-017:** automation may not send after a human takeover.

## Decision

1. **Turn service** (`IAssistantTurnService`):
   1. a lease;
   2. a gate: platform switch, takeover, activation, newer customer message, outside hours (DoNotAnswer), replies per hour;
   3. deterministic pre-checks: seller keywords / "talk to a person" → hand-off; media without text → the S02 setting;
   4. budgets and the data rule;
   5. context: coalesced customer messages after the last handled trigger, approved knowledge as fenced untrusted data, product references, prompt `assistant-system-v1` (version + template hash logged);
   6. the bounded tool loop through the ADR-020 registry;
   7. output validation;
   8. an ownership re-check;
   9. enqueue through the gated outbox.
2. **Limits** (owner-approved defaults; `Ai:Orchestration`):

   | Limit | Value |
   |---|---|
   | Model calls per turn | tool steps + 1 (5), cap 7 |
   | Tool calls per turn | 8 (3 per model response) |
   | Model call timeout | 20 s |
   | Turn deadline | 45 s |
   | Reply length | ≤ 900 characters |
   | Context | ≤ 20 messages / 6,000 characters |
   | Tokens per turn | ≤ 20,000 |
   | Turns per shop per day | 150 |
   | Model calls per day, platform | 800 |
   | Concurrency | 1 turn per conversation; 2 per shop |

   On the last allowed model call, tools are switched off, so the model must answer.
3. **Output rules** (rule codes only):
   - empty, too long;
   - **ungrounded number** (not in tool results, knowledge, conversation or shop hours);
   - **foreign link** (only links produced by tools in this turn);
   - prompt leak (per-turn canary);
   - card/OTP-like digits; tool markup;
   - script (Devanagari enforced; English/Romanized tolerated for each other).
   - One corrective retry, then the safe fallback.
4. **Safe fallback:**
   - for any failure, budget exhaustion, open circuit, data-rule block or repeated rule break: `EscalateToHuman` (system takeover) plus **one fixed hand-off notice** in the customer's script, at most once per conversation per 12 hours;
   - the platform switch, the shop toggle and an existing takeover end silently instead.
5. **Data rule:** conversation content is sent with `ContainsPersonalData = true`. Free tiers serve only shops on the operator's `Ai:DataPolicy:SyntheticTenantIds`. Real shops get the hand-off until a paid no-training provider is approved. The owner playground (made-up text) is synthetic by definition.
6. **Resilience:**
   - per call, S01's primary → fallback;
   - a per-profile circuit breaker (5 provider failures in 2 min → skip for 5 min; in-process);
   - no other turn-level retries; a duplicate trigger replays the finished turn.
7. **Kill switches:** the platform `Ai:Enabled` (reloadable), the shop policy toggle, the conversation takeover, the circuit breaker.
8. **AI action log** (`assistant_turns`):
   - **one row per turn**: outcome + reason code, versions (policy, prompt + hash, registry), model calls (profile, provider, model, latency, tokens, outcome), tool traces (S04 values-free), knowledge citations (IDs, scores), validation codes, token totals, estimated cost (configured prices; free = 0), outbound message ID;
   - **never stored:** prompts, customer text, reply text, tool argument values, keys, model reasoning or thought signatures;
   - retention 90 days (daily purge); owner/admin read API.
   - While running, the row is the per-conversation lease (partial unique index).
9. **ADR-017 amendment — `OutboundMessageOrigin.Handoff`:** the hand-off notice is checked like automation (spam, connection, messaging window) but is **allowed during the takeover it accompanies** and is **not cancelled** by takeover suppression. Automation replies stay blocked after a takeover.
10. **System-safe activation:** the activation check reads store readiness through `IStoreReadinessQuery` (no member permission), because background turns run without a role.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Stay silent on failure | No risk of a wrong message | The customer waits with no answer; staff may not notice | Rejected (Q3); hand-off + notice instead |
| Send the notice before the takeover as automation | No new origin | Takeover suppression cancels queued automation messages, and the delivery gate refuses them; the notice would never arrive | Rejected; narrow `Handoff` origin |
| Use the `System` origin for the notice | Exists | Skips the gate entirely (incl. the 24 h window) and the timeline | Rejected |
| Distributed circuit breaker / budgets in Redis | Shared across instances | Redis is not an MVP dependency | Deferred; budgets are DB-counted, the breaker is per instance |
| Store prompts/replies for debugging | Easier analysis | PII and leakage risk; replies already live in the timeline | Rejected |

## Consequences

- **Product:** answers are bounded and grounded. Failures reach a person with a polite notice. Owners can try the assistant in a playground and read a turn log.
- **Architecture:**
  - `IAssistantTurnService`, `IAssistantTurnLogQuery`, `AssistantCircuitBreaker`, `IStoreReadinessQuery`;
  - `assistant_turns` table; `Handoff` origin; the recurring purge job;
  - `AssistantText` is shared with the evaluation harness.
- **Security/privacy:** see 5 and 8. Logs are values-free (tested on the whole Serilog pipeline).
- **Cost/operations:** daily caps protect the free quota; the cost estimate is configurable per profile; 90-day log retention.
- **Migration or rollback:** additive migration. Rollback = `Ai:Enabled=false` (silent), or remove shops from the synthetic allowlist (hand-off only).

## Validation evidence

Fake-model integration tests over PostgreSQL:
- loops, repeated/malformed calls, invented prices and tool markup;
- provider failure and the circuit; turn deadline;
- token, daily and platform budgets; the reply rate;
- the four kill switches; takeover or a newer message mid-turn;
- the data rule; pre-checks; model escalation with the notice cooldown; the window rule for the notice;
- concurrency; isolation; the playground;
- redaction of the row and of all host logs.

Unit tests for the validator, script rule, prompt (no digits in rules), circuit, text rules, hours and turn entity.

## Supersession conditions

- M10 plan entitlements replace the fixed daily caps.
- Multiple API instances need a shared breaker.
- S07 adopts quick-reply confirmation or debounce changes the trigger model.
- A paid provider is approved (the data rule then allows real shops).
