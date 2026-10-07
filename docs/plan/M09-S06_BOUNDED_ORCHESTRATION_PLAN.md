# M09-S06 — Bounded Orchestration, Budgets, and Action Logs — Scoped Plan

## Identity

- **Milestone:** 09 — Constrained AI Assistant, RAG, and Commerce Tools
- **Step:** 06 — Bounded orchestration, budgets, and action logs
- **Author:** Claude (planning)
- **Date:** 2026-10-07
- **Status:** `REVIEW` — implemented 2026-10-07 (checkpoint `artifacts/checkpoints/M09-S06.md`); was `IN PROGRESS`, approved by the owner on 2026-10-07 ("ok implement"): Q1–Q11 as recommended
- **Prerequisites:** M09-S05 `APPROVED` (2026-10-07; merged `075faae`); ADR-018, ADR-019, ADR-020 `Accepted`.
- **Manual work for the owner in this step:**
  - answer the decisions below (mostly numbers: budgets, timeouts, limits);
  - at the checkpoint, review the budget/timeout values, failure replies, the log schema and the kill-switch evidence (the milestone's review requirement);
  - optional: try the owner playground with **made-up** messages on the live Gemini key you already set in S01 (about 10 minutes);
  - no new keys, no money, no Meta changes.

## Milestone prompt (verbatim)

> Implement the assistant orchestration loop with system/policy versioning, normalized inbound context, approved retrieval, tool selection/execution, maximum tool iterations, total timeout, response-size limits, token/cost budget, per-tenant concurrency, transient retry policy, circuit breaker/kill switch, safe fallback, and redacted AIActionLog. Validate final output against conversation ownership and content rules before enqueueing. Never log raw secrets, unnecessary PII, or unrestricted chain-of-thought. Add deterministic fake-model tests for loops, timeouts, malformed calls, hallucinated tool results, provider failure, budget exhaustion, and redaction.

**Review checkpoint:** approve budget/timeout values, failure responses, log schema, and kill-switch evidence.

## What exists today (verified 2026-10-07)

- **Model boundary (S01, ADR-018):**
  - `IAiChatClient` → `ResilientAiChatClient`: global kill switch `Ai:Enabled` (read per call); data policy refuses `ContainsPersonalData` unless `AllowPersonalData` and a `NoTraining` provider; one fallback to the Fallback profile on timeout / rate limit / provider unavailable;
  - Fake mode; per-profile reasoning control; Gemini thought signatures round-trip (never stored);
  - `Ai:Limits`: timeout 30 s, output 800 tokens, tool steps 6, replies 60/hour (platform caps).
- **Evaluation prompt `eval-v0`** (S01): 7 rules (grounding, clarify, escalate, PII, own orders only, injection, language). It scored 86% / 0 fabrications on Gemini Flash-Lite. The scorer already detects **ungrounded numbers** and reply language.
- **Policy (S02):**
  - toggle, reply style (match / Romanized / Devanagari / English), languages, tone, brand note, business hours + outside-hours behavior, unidentified-media behavior;
  - escalation keywords + fixed categories; allowed tools; per-shop budgets (tool steps 4, replies 20/hour, output 600);
  - policy version (`xmin`); activation query.
- **Retrieval (S03):** `IKnowledgeRetrievalService.RetrieveAsync(query)` → High/Low/None passages with citations, marked untrusted.
- **Tools (S04/S05, ADR-020):** registry `kreyora-tools.v2`. Trusted context with automation state and turn ID; strict schemas; per-call deadline; same-turn replay; values-free traces (logged, **not yet persisted**).
- **Outbound (M08-S05, ADR-017):** `IConversationReplyService.EnqueueAutomationReplyAsync(conversation, text, key)` goes through the outbox with the enqueue- and delivery-time gate (takeover, messaging window, length, connection).
- **Not yet:** anything that runs a turn, persisted AI logs, budgets beyond per-call limits, a circuit breaker.

## Objective

One function, **run an assistant turn for a conversation**, that is bounded in every dimension, fails safe, never sends anything that breaks the shop's rules or the conversation's ownership, and leaves a redacted log of what happened. S07 calls it from real inbound messages; the owner playground calls it with made-up messages.

## Design

### A. The turn (`IAssistantTurnService.RunAsync(conversationId, triggerMessageId)`)

1. **Gate (no model yet):**
   - assistant active (activation query), automation active (no takeover), platform switch on, circuit closed, budgets left, reply rate under the policy limit, business hours (DoNotAnswer → stop);
   - **one turn per conversation at a time** (database advisory lock) and **≤ 2 concurrent turns per shop** (Q7).
   - A duplicate trigger for the same message returns the earlier turn (unique key).
2. **Deterministic pre-checks (Q6):**
   - the seller's escalation keywords, or explicit "talk to a person" phrases → `EscalateToHuman` + holding message, without calling the model;
   - a photo/media without text → the S02 `UnrecognizedMediaBehavior`.
3. **Context:**
   - **conversation:** the latest customer messages since the last reply (coalesced), plus up to 10 earlier messages, trimmed to a character budget; product references from storefront links (S04 resolver);
   - **knowledge:** one approved-knowledge retrieval on the customer's latest text; passages go in as **quoted, untrusted reference data** with citations;
   - **system prompt** `assistant-system-v1`: the 7 eval-v0 rules, plus the shop's name, language/reply style, tone, brand note, hours, the S05 confirmation rule, and "never invent links". Versioned and hashed into the log.
   - **Data rule (Q5):** conversation content is sent with `ContainsPersonalData = true`, so the S01 data policy refuses it on free tiers. Only shops on the platform's **synthetic/test allowlist** (`Ai:DataPolicy:SyntheticTenantIds`) are sent to free-tier models. Real shops get the safe fallback until a paid no-training provider is approved (ADR-018 unchanged).
4. **Loop:**
   - model call → tool calls through the S04/S05 registry (same turn ID, so retries replay) → results back → repeat;
   - bounded by **model calls, tool calls, parallel calls per response, per-call and total deadline, token budget** (Q1, Q2);
   - malformed or unknown tool calls get the registry's error JSON and count against the budget;
   - repeating the identical tool call is refused (loop guard).
5. **Validate the final text (Q4):**
   1. **Ownership:** re-read the conversation; it must still be automated, not escalated during the turn, and **no newer customer message may have arrived** (else `superseded`: no send; S07 re-triggers).
   2. **Content:**
      - non-empty, ≤ reply limit, plain text (markdown stripped);
      - **no number that didn't come from a tool result, a retrieved passage or the customer** (the S01 grounding check);
      - **no link except storefront links produced by tools in this turn**;
      - no system-prompt leak (canary); no card/OTP-like numbers echoed;
      - language matches the policy's reply style.
   3. **Failure:** one corrective retry within budget, then the safe fallback.
6. **Send:** `EnqueueAutomationReplyAsync`, with the turn ID as idempotency key; the gate re-checks at enqueue and delivery.
7. **Safe fallback (Q3):**
   - for any failure (provider down, budget exhausted, loop limit, validation failed twice, data rule): a fixed holding message in the customer's language ("Thank you! A team member will reply shortly.") **plus `EscalateToHuman`** (`tool_unavailable` / `low_confidence`), so a person picks it up;
   - at most once per conversation per 12 hours; otherwise silence (the team already owns it).
8. **Log:** one `assistant_turns` row (below), written in every outcome.

### B. Budgets and limits (Q1, Q2) — values for your approval

| Limit | Recommended default | Where |
|---|---|---|
| Model calls per turn | policy tool steps + 1 (default 4 + 1 = 5; platform cap 7) | policy / `Ai:Limits` |
| Tool calls per turn | 8 | `Ai:Orchestration` |
| Tool calls per model response | 3 (extras refused) | `Ai:Orchestration` |
| Per model call timeout | 20 s | `Ai:Orchestration` |
| Total turn deadline | 45 s | `Ai:Orchestration` |
| Reply length | ≤ 900 characters (Instagram limit 1,000) and policy output tokens (600) | `Ai:Orchestration` / policy |
| Context budget | ≤ 6,000 characters of conversation + ≤ 3,000 of knowledge (S03) | `Ai:Orchestration` |
| Tokens per turn | ≤ 20,000 input + output (stop and fall back when exceeded) | `Ai:Orchestration` |
| Turns per shop per day | 150 (free tier); configurable later per plan (M10) | `Ai:Orchestration` |
| Model calls per day, whole platform | 800 (protects the free daily quota) | `Ai:Orchestration` |
| Replies per conversation per hour | policy (20; cap 60) | policy |
| Concurrent turns per shop | 2; one per conversation | `Ai:Orchestration` |
| Cost | estimated from configured prices per model (free tier = 0); logged per turn | `Ai:Profiles:*:Price*` |

### C. Resilience (Q8)

- **Transient retries:** keep S01's "primary, then fallback model once" per call. No other turn-level retries (S07's job retries replay safely through the turn key).
- **Circuit breaker:** per model profile, **5 provider failures within 2 minutes → open for 5 minutes**. While open, turns skip the model and use the safe fallback. In-process; logged.
- **Kill switches (evidence at the checkpoint):**
  1. platform `Ai:Enabled` (configuration reload, no restart);
  2. shop policy toggle;
  3. conversation takeover;
  4. the circuit breaker.
  - Each is tested: no model call, no tool call, correct fallback or silence.

### D. AIActionLog (Q9) — `assistant_turns`

- **One row per turn:**
  - tenant, conversation, trigger message ID, turn ID (unique per trigger), started/finished, outcome (`replied`, `escalated`, `fallback`, `skipped`, `superseded`, `blocked`), outcome reason code;
  - policy version, system prompt version + hash, registry version;
  - model calls: profile, provider, model, latency, input/output tokens, finish reason, failure kind;
  - tool traces (the S04/S05 values-free traces);
  - knowledge citations (document/version/chunk IDs and scores);
  - validation results (rule codes only); estimated cost; the outbound message ID.
- **Never stored:** prompts, customer text, reply text (it already lives in the message timeline), tool arguments (only field names + keyed hash), thought signatures or any model reasoning, keys.
- **Retention:** 90 days, purged by the existing job runner. Tenant-scoped, read-only API for owners/admins (`GET /v1/assistant/turns`) as S08 input.

### E. Owner playground (Q10)

- `POST /v1/assistant/playground` (Owner/Admin, antiforgery): the owner types **made-up** customer messages and sees the reply, the tools used, citations and budget use.
- Runs the same turn in **seller-preview mode**: write tools dry-run, nothing is sent, no hold or link is created. The log is marked as a playground run.
- Content is the owner's own typing (labelled "don't paste real customer messages"), so it may use the free tier.

### F. ADR-021 (Q11)

Records the budgets, fallback, data rule (synthetic allowlist), log schema and retention, and kill switches. Proposed now; accepted at the checkpoint.

## Tests (deterministic, fake model; real PostgreSQL)

- **Loops:** the model keeps calling tools → stops at the model-call cap → fallback; the identical tool call repeated → refused; more than 3 tool calls in one response → extras refused.
- **Timeouts:** a slow fake model → per-call timeout; total deadline reached mid-loop → fallback; nothing sent twice.
- **Malformed calls:** bad JSON, unknown tool, write tool after takeover → the registry error goes back to the model; the turn still completes or falls back within budget.
- **Hallucinated tool results:** the model states a price/fee/stock not in any tool result or passage → validation blocks → corrective retry → fallback. A fake "tool result" text in the reply is treated as plain text and fails grounding.
- **Provider failure:** each failure kind → fallback + escalation; the circuit opens after 5 and closes after the window.
- **Budget exhaustion:** per-turn tokens, shop daily turns, platform daily calls, replies per hour → no model call where applicable, a logged reason, fallback or silence as specified.
- **Kill switches:** platform switch, shop toggle, takeover before and **during** a turn (reply not sent), circuit.
- **Ownership:** a newer customer message mid-turn → `superseded`, nothing sent; escalation during the turn → nothing sent.
- **Data rule:** a real shop on the free tier → refused by data policy → fallback; a synthetic-allowlisted shop → model called.
- **Content rules:** length, markdown stripped, links other than this turn's checkout link blocked, canary leak blocked, card/OTP echo blocked, wrong script.
- **Redaction:** the stored turn row and logs contain no customer text, reply text, phone digits, keys or thought signatures (asserted on the serialized row and captured logs).
- **Concurrency/idempotency:** two turns for one conversation → one runs; a third concurrent shop turn waits or skips; a duplicate trigger → same turn, one reply.
- **Isolation:** another tenant's conversation can't be run; playground and turn log APIs are tenant-scoped and role-checked.
- **Playground:** dry-run writes, never enqueues, logs as playground.

## Contracts and migrations

- **New service:** `IAssistantTurnService` (S07 calls it). New routes: `POST /v1/assistant/playground`, `GET /v1/assistant/turns` (read).
- **Migration (additive):** `assistant_turns` (unique tenant + conversation + trigger message; JSONB steps/citations; indexes for purge and listing).
- **Configuration:**
  - `Ai:Orchestration:*` (values above);
  - `Ai:DataPolicy:SyntheticTenantIds`;
  - optional `Ai:Profiles:*:InputPricePerMillion` / `OutputPricePerMillion`.
- **ADR-021.** OpenAPI regenerated.

## Security and data handling

- Real customer content reaches a model only when ADR-018 allows it (paid, no-training, approved). Until then, real shops get the safe human hand-off.
- Nothing sensitive is persisted: see the Log section above. Validation and logs use rule codes, never content.
- Untrusted inputs (customer text, knowledge passages, tool results) are fenced as data in the prompt. The model can't change tenant, tools or budgets.
- Sends go only through the existing gated outbox; ownership is re-checked right before enqueueing.

## Acceptance criteria

1. The turn service runs the bounded loop with versioned prompt/policy, coalesced context, retrieval, tools, all budgets and limits, retry/fallback and circuit breaker.
2. Output is validated for ownership and content before enqueueing; the safe fallback works for every failure path.
3. `assistant_turns` is written for every outcome with no prompts, customer/reply text, keys or reasoning.
4. Kill-switch evidence for all four switches.
5. All listed fake-model tests pass; full gates green; Docker at baseline.
6. ADR-021 and checkpoint `M09-S06.md` (`REVIEW`).

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Loop limits | **5 model calls, 8 tool calls (3 per response), 20 s per call, 45 s per turn, reply ≤ 900 characters** |
| Q2 | Token/cost budgets | **≤ 20k tokens per turn; 150 turns per shop per day; 800 model calls per day platform-wide** (free-tier protection; plans adjust in M10) |
| Q3 | Safe fallback | **Holding message in the customer's language + hand to a person**, at most once per chat per 12 h; otherwise silent |
| Q4 | When the answer breaks a rule | **One corrective retry, then the safe fallback** |
| Q5 | Real customer content on the free tier | **Never** (ADR-018). Only shops on a platform synthetic/test allowlist use free tiers; real shops get the hand-off until a paid no-training provider is approved |
| Q6 | Check keywords/media before calling the model | **Yes:** seller keywords + "talk to a person" phrases escalate without the model; media follows the S02 setting |
| Q7 | Concurrency | **One turn per chat; ≤ 2 per shop; a newer customer message cancels sending the older reply** |
| Q8 | Circuit breaker | **5 failures in 2 min → pause that model 5 min** (fallback while paused) |
| Q9 | Action log | **Per-turn row, no prompts/texts/reasoning, 90-day retention, owner read API** |
| Q10 | Owner playground | **Yes:** try made-up messages, dry-run writes, nothing sent |
| Q11 | Record as ADR | **Yes, ADR-021** |

## Out of scope

- Triggering turns from real inbound messages, debounce, Instagram quick replies, and live sandbox runs (S07).
- UI for the playground and turn log (S08).
- The full 72-case evaluation (S08).
- Plan-based budgets (M10).
- Paid providers (by configuration, when you approve one).

## Build checklist

- [x] Task 1 — ADR-021; `Ai:Orchestration` + data-policy options and validation
- [x] Task 2 — Turn gate: activation, switches, budgets, rate, hours, advisory locks, idempotent turn record; migration
- [x] Task 3 — Context builder: coalesced messages, product references, retrieval, system prompt v1 (versioned/hashed)
- [x] Task 4 — Bounded loop with the registry, loop guard, circuit breaker, token/cost accounting
- [x] Task 5 — Output validator (ownership, grounding, links, length, leak, PII echo, language) + corrective retry + safe fallback (+ escalation)
- [x] Task 6 — Enqueue through the gated outbox; `assistant_turns` writer, purge, read API
- [x] Task 7 — Owner playground API; OpenAPI
- [x] Task 8 — Fake-model tests (loops, timeouts, malformed, hallucination, provider failure, budgets, kill switches, ownership, redaction, concurrency, isolation)
- [x] Task 9 — Full gates; Docker check; checkpoint `M09-S06.md` (`REVIEW`); status docs
