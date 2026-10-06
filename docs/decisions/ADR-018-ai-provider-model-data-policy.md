# ADR-018 — AI provider, model selection, data policy, fallback and disable

- **Status:** `Proposed` (owner acceptance at the M09-S01 checkpoint)
- **Date:** 2026-10-06
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** Milestone 09 (all steps); M10/M11 (activation, deployment)

## Context

M09 adds a constrained AI assistant. Its hard gates are:
- "Initial AI provider is selected by an accepted quality/cost/latency/privacy ADR";
- "Data-processing terms and redaction policy are approved before real customer content is sent" (`docs/milestones/09_AI_ASSISTANT_RAG_TOOLS.md`; `docs/plan/plan.md` §10.7).

The owner directed (2026-10-06) that development be **zero cost** on free tiers. Evidence was gathered in M09-S01 (`docs/architecture/AI_EVALUATION_BASELINE.md`):

- **Terms checked 2026-10-05/06:**
  - **NVIDIA API trial:** trial/evaluation only; no production; no personal data; inputs may train NVIDIA models (§1.2, §2.6a, §4.3, §3.3).
  - **Google Gemini API unpaid services:** inputs used to improve Google products; human review possible; "Do not submit sensitive, confidential, or personal information". **Paid services:** prompts and responses are not used to improve products; logged for a limited time for abuse detection (Gemini API terms, last updated 2026-04-28).
  - **OpenRouter free:** 20 requests/min, 50/day (1,000/day after $10 of credits).
- **Speed probe on 30 free models:**
  - **Gemini 3.5 Flash-Lite:** 83 tok/s, text reply 1.8 s, valid tool call 1.1 s.
  - **Gemini 3.5 Flash with thinking off:** 72 tok/s.
  - **Nemotron 3 Super with thinking off (OpenRouter):** 53 tok/s.
  - **Gemma 4 31B:** congested (OpenRouter) or 41.7 s and leaking its thinking into the reply (Google).
  - **Gemini 2.5:** retired for new users.
- **Screening on a 24-case synthetic Nepal-commerce set** (free-tier daily caps stopped it early):
  - **Gemini 3.5 Flash-Lite, 22 cases:** 86% passed; right tool 100%; **0 fabricated prices, stock or fees**; behavior 100%; injection refusal 100%; escalation recall 100%; language match 86%.
  - **Romanized Nepali specifically:** 7 of 9 replies in Romanized script, 2 in Devanagari; occasional mixed-script replies.
  - **Gemini 3.5 Flash, 5 cases:** 80% (one free-tier `ProviderUnavailable`).
- **Owner decisions during S01:**
  - drop NVIDIA (poor playground quality);
  - then drop OpenRouter: **Gemini (Google AI Studio) only for now**;
  - stop free-tier testing at this point; the full 72-case evaluation happens in M09-S08 before activation;
  - paid models must remain possible later without code changes (R-PAID);
  - AI should be **on by default per chat**, with the seller taking over a chat when they want to reply themselves.

## Decision

1. **Provider and models (development):** Google AI Studio (Gemini API) through its OpenAI-compatible endpoint (`https://generativelanguage.googleapis.com/v1beta/openai`).
   - **Primary profile:** `gemini-3.5-flash-lite` (non-thinking; sends no reasoning field).
   - **Fallback profile:** `gemini-3.5-flash` with `ReasoningEffort: none`.
   - Google limits each model separately, so the fallback helps when one model is limited or unavailable.
2. **Boundary:** every AI caller uses `IAiChatClient` (Application) with model **profiles**, never provider names, URLs or keys.
   - The single adapter speaks the OpenAI-compatible chat-completions format.
   - Provider-specific opaque data (e.g. Gemini 3 thought signatures, `tool_calls[].extra_content`) is round-tripped unchanged (`AiToolCall.ProviderData`).
   - Thinking is controlled per profile (`ReasoningEffort`).
   - A reply ending with `finish_reason=length` is treated as incomplete.
3. **Data policy:**
   - **Free tiers receive synthetic data only.** Enforced in code: `Ai:DataPolicy:AllowPersonalData` may be true only when every profile's provider is marked `NoTraining=true`, and requests flagged `ContainsPersonalData` are refused otherwise (`PolicyViolation`).
   - **Real customer content** requires all of:
     - (a) a **paid, no-training endpoint**: paid Gemini, or another paid provider by configuration;
     - (b) the owner's approval of that provider's data-processing terms;
     - (c) the **redaction rule**: phone numbers, addresses, card, bank, ID numbers, passwords and OTPs are masked before sending unless the task needs them (details implemented with orchestration in S06).
4. **Control levels:**
   - **Per conversation (seller):** automation **on by default**; "Take over" or any staff reply stops AI for that chat; "Hand back to automation" resumes it (ADR-017, built in M08).
   - **Per shop (seller, M09-S02):** owner preference is **on automatically once the shop's setup is complete**, with a seller toggle.
   - **Platform kill switch (operator, `Ai:Enabled`):** **off** until M09-S08 approves activation on an approved paid endpoint; then **on in production**, used only for emergencies (provider outage, misbehaving model, cost spike). It is read per call, so it takes effect on configuration reload.
5. **Fallback behavior:** Primary → Fallback on `Timeout`, `RateLimited` or `ProviderUnavailable`, within one deadline. Never on `InvalidRequest`, `InvalidResponse`, `ContentRefused` or configuration problems. If both fail: **no AI reply**. The conversation is left for a team member; the "a team member will reply" handling is designed in S06/S07.
6. **Paid upgrade path (R-PAID):** add a provider entry (`BaseUrl`, `ApiKey` from secrets, `NoTraining`, optionally `MaxTokensParameter` / `ReasoningEffortParameter`) and point a profile at it. No code change for any OpenAI-compatible service (paid Gemini, OpenAI, others); a different API gets a new adapter behind the same interface.
7. **Reply language:** the assistant mirrors the customer's script. Known gap: about 1 in 5 Romanized messages answered in Devanagari or mixed script. Addressed by:
   - a per-shop **reply style** setting (match customer / always Romanized / always Devanagari, M09-S02);
   - worked examples in the instructions (S02/S06);
   - a pre-send script check with one regeneration (S06);
   - an S08 threshold.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| NVIDIA API trial | 80 models, about 40 rpm | Trial only; no production; no personal data; owner found quality poor; observed 0.7 tok/s | Rejected by owner |
| OpenRouter free models (Gemma 4, Nemotron 3 Super, …) | Many models, one key, easy switching | 50 requests/day; Gemma congested; free endpoints may train | Dropped by owner after the probe; can be re-added by configuration |
| Gemma 4 31B (owner's starting pick) | Open weights, multilingual | 41.7 s on Google free tier, thinking leaked into replies, no thinking control | Deferred; re-evaluate when hosted paid or local |
| Gemini 3.6 / 3.7 / 3.8 Flash | Newer generation (Google calls 3.8 current) | Unavailable or timing out on the free tier during the probe | Re-evaluate in S08 on the paid tier |
| Microsoft.Extensions.AI abstractions | Standard .NET abstraction | New dependency; types would leak into Application | Own minimal contract chosen (Q1) |
| Paid provider from day one | Real-data testing earlier | Cost; owner wants zero cost now | Deferred to pilot (R-PAID) |

## Consequences

- **Product impact:**
  - Fast replies (about 2 s per model call on Flash-Lite).
  - The seller can always take over a chat.
  - AI is off platform-wide until S08 approves.
  - Romanized-reply consistency needs the S02/S06 measures before activation.
- **Architecture impact:**
  - `Kreyora.Application.Ai` contract.
  - `Kreyora.Infrastructure.Ai`: options + validator, OpenAI-compatible adapter, fake, resilient client.
  - Evaluation harness `services/api/tools/Kreyora.AiEvaluation` (opt-in, not in CI).
  - The architecture test keeps the contract free of HTTP and provider types.
- **Security/privacy impact:**
  - Keys only in user secrets / environment.
  - No prompts, tool arguments, replies or keys in logs.
  - Personal data is blocked from free tiers by start-up validation and per-request checks.
- **Cost/operations impact:**
  - **Development: $0** (free tier, daily caps observed: Flash about 5 benchmark cases/day, Flash-Lite about 22).
  - **Pilot estimate on paid Gemini 3.5 Flash-Lite:** about **$0.65 per 1,000 replies**, from measured tokens (about 1,630 input / 61 output per answer at $0.30 / $2.50 per million). Expect a few times more once shop policy and knowledge enter the prompt (S02/S03).
  - **Fallback on paid Gemini 3.5 Flash:** about $3.80 per 1,000 replies.
  - Budget caps come in S06.
  - Prices from the Gemini pricing page, last updated 2026-10-01; Google notes 3.8-generation pricing changes after 2026-12-31.
- **Single-provider risk:** a Google-wide outage leaves no AI reply. Mitigation: safe "team member will reply" handling; a second provider can be added by configuration.
- **Migration or rollback impact:** no database change. Rollback = `Ai:Enabled=false` or `Ai:Mode=Fake`.

## Validation evidence

- **Tests:**
  - `Kreyora.UnitTests/Ai/*`: options validation incl. the data policy, fallback rules, reasoning control, logging redaction, paid-provider switch by configuration, harness scorers;
  - `Kreyora.ContractTests/Ai/AiChatClientContractTests`: documented and live-observed response shapes, thought-signature round trip;
  - the architecture test `ApplicationAiContract_ShouldNotDependOn_HttpOrProviderTypes`.
- **Evidence:** `docs/architecture/AI_EVALUATION_BASELINE.md`, `artifacts/evaluations/M09-S01/` (summary, probe results).
- **Re-validation:** M09-S08 full 72-case evaluation and owner grading on the activation configuration (paid tier) before any real customer traffic.

## Supersession conditions

Revisit this ADR if any of these happen:
- S08 evaluation misses its thresholds;
- Google changes terms, retires these models or changes prices materially;
- the owner chooses a paid provider other than Gemini;
- Romanized-reply consistency cannot be fixed with policy, examples and the pre-send check;
- a provider outage shows the single-provider risk is unacceptable.
