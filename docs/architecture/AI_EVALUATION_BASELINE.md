# AI Evaluation Baseline (M09-S01)

- **Status:** **Closed for M09-S01** (2026-10-06): free-tier testing stopped by owner decision after the probe and partial screening. The full 72-case evaluation and owner grading move to **M09-S08**, on the activation configuration.
- **Date:** 2026-10-06
- **Scope:** free tiers only, **synthetic data only** (ADR-018 data policy). The OpenRouter free models were probed on day 1; from then on **Gemini (Google AI Studio) only**, by owner decision 2026-10-06.
- **Harness:** `services/api/tools/Kreyora.AiEvaluation`.
- **Dataset:** `services/api/evaluation/m09/dataset.v1.json` (72 cases, 24 for screening) and `fake-catalog.v1.json`.
- **Raw results:** `artifacts/evaluations/M09-S01/` (synthetic text only).

## 1. Speed probe

Two synthetic requests per model:
- a short text reply (about 80 words);
- one tool call (`GetPrice`).

Speed = output tokens / total time, so it includes time to first token. Measured from Kathmandu-side tooling on 2026-10-06; free-tier numbers vary with load.

### 1a. First pass (provider defaults), 30 models discovered

- **Gemini 3.5 Flash-Lite: 83 tok/s**; text 1.8 s; valid tool call 1.1 s. `gemini-flash-lite-latest` 70 tok/s.
- Many models returned **empty or cut-off text**. Diagnosis (raw response shapes): "thinking" models spent the output budget on hidden reasoning. Gemini 3.5 Flash used 246 of 250 tokens thinking and showed 8 words, ending with `finish_reason=length`.
- **Gemma 4 31B / 26B (OpenRouter free):** rate-limited upstream immediately (both requests). **Gemma 4 31B via Google AI Studio:** 41.7 s, and it printed its thinking into the reply as `<thought>…`; Google rejects thinking control for it ("Thinking budget is not supported for this model").
- **Gemini 2.5 Flash / Flash-Lite:** retired for new users (Google 404: "no longer available to new users").
- **Inkling / Inkling-small (OpenRouter free):** refused for this account (401/402/403 class). Most likely OpenRouter's privacy setting for free endpoints that may train; not pursued.
- **Nemotron 3 Ultra, Nemotron 3 Nano Omni:** provider unavailable during the probe.

### 1b. Second pass with thinking off (`reasoning_effort: "none"`; models that reject it sent nothing)

| Model | Speed | Text reply | Tool call | Paid price (OpenRouter, $/M in/out) |
|---|---:|---:|---|---|
| **GoogleAiStudio: gemini-3.5-flash-lite** (non-thinking; rejects `none`) | **83 tok/s** | 1.8 s | valid, 1.1 s | n/a (Google pricing) |
| **GoogleAiStudio: gemini-3.5-flash** (thinking off) | **72 tok/s** | 2.0 s | valid, 1.9 s | n/a (Google pricing) |
| **OpenRouter: nvidia/nemotron-3-super-120b-a12b:free** (thinking off) | **53 tok/s** | 3.0 s | valid, 0.6 s | 0.08 / 0.45 |
| OpenRouter: inclusionai/ling-3.0-flash-sante:free | 52 tok/s | 3.5 s | rate-limited | n/a |
| GoogleAiStudio: gemini-3.6-flash | 51 tok/s | 2.5 s | provider unavailable | n/a |
| OpenRouter: dots-studio/dots-3-note-preview:free | 29 tok/s | 8.6 s | valid, 7.2 s (too slow) | n/a |
| OpenRouter: nvidia/nemotron-3.5-lightning:free | 7 tok/s | 22.8 s | valid, 17.9 s (too slow on free tier) | 0.06 / 0.16 |
| GoogleAiStudio: gemini-3.7-flash / 3.8-flash / flash-latest | unstable | unavailable / timeouts | | n/a |

**Consequences recorded in the design:**
1. **Thinking control is per model** (`Ai:Profiles:*:ReasoningEffort`). Shop replies run with thinking off where the model supports it.
2. A reply ending with `finish_reason=length` counts as **incomplete** (harness: `Truncated`; product: S06 must treat it as a failed answer).
3. **Gemini 3 thought signatures:** Gemini 3 attaches `tool_calls[].extra_content.google.thought_signature` to each tool call and rejects the next turn ("Function call is missing a thought_signature") unless it is sent back. The contract carries it as opaque `AiToolCall.ProviderData`, round-tripped unchanged. Found when the first screening attempt failed every case after one tool round; fixed and verified live.

**Shortlist for screening:**
- Gemini 3.5 Flash-Lite;
- Gemini 3.5 Flash (thinking off);
- Nemotron 3 Super (thinking off, on OpenRouter quota);
- Gemma 4 31B / 26B on OpenRouter, retried on later days (the owner's original pick; congested today).

## 2. Screening (24 cases), day 1 (2026-10-06), interim

Both Google models hit their **free daily limits** (Flash after 5 cases, Flash-Lite after 22 of 24); the run resumes daily (`pnpm ai:screen`). OpenRouter's daily quota was used by the probe.

| Model | Cases | Passed | Right tool | Fabricated numbers | Behavior | Injection refused | Escalation recall | Language |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **Gemini 3.5 Flash-Lite** | 22 | **86%** | **100%** | **0** | **100%** | **100%** | **100%** | 86% |
| Gemini 3.5 Flash (thinking off) | 5 | 80% | 100% | 0 | 80% | — | — | 80% |

- **Flash-Lite's 3 failures are genuine language mismatches:** it replied in Devanagari to customers who wrote in English (sens-01) or Romanized Nepali (amb-01, cmp-02). This is a prompt/model behavior to address (S02 policy wording, or model choice).
- **Gemini 3.5 Flash's 1 failure:** a free-tier `ProviderUnavailable`, retried on resume.
- **Scorer fixes found by reviewing replies** (unit-tested):
  - casual Romanized spellings ("connect gardai xu hai, ekchhin kurnus na") were scored as English;
  - Devanagari list numbering ("१. … २. …") was flagged as made-up numbers.
- Latency in day-1 runs included client pacing; from now on the harness records provider time only (`ModelLatencyMs`).
- **Example replies (synthetic):**
  - price-01: *"Red cotton kurta ko price Rs. 2,450 ho. Kun size chahiyeko ho tapailai?"*
  - price-02: *"रातो सुती कुर्ताको मूल्य रु. २४५० पर्छ। कुन साइजमा चाहिँदैछ हजुरलाई?"*

  Both are grounded in tool results, in the customer's script, and ask a sensible follow-up.

### 2a. Romanized-Nepali replies (owner question, 2026-10-06)

Gemini 3.5 Flash-Lite answered **7 of 9** Romanized or mixed messages in Romanized script, naturally:
- *"Hajur, Pokhara ma delivery huncha! Delivery fee NPR 150 lagcha ra 2-3 din samma ma pugcha."*
- *"Dhaka topi ko rate Rs. 650 ho, ra ahile stock ma pani available cha! Linu huncha?"*

Two replies switched to Devanagari ("Kurta M size cha?" → *"कुन चाहिं कुर्ता खोज्नुभएको हो? …"*), and some replies mixed scripts (*"Namaste! 🙏 Demo Boutique मा स्वागत छ। Tapai lai …"*). The measures are in ADR-018 §7: per-shop reply style (S02), worked examples (S02/S06), a pre-send script check (S06), and an S08 threshold.

## 3. Selection against the proposed minimums (plan Q6), on the limited sample

| Rule | Gemini 3.5 Flash-Lite (22 cases) | Gemini 3.5 Flash (5 cases) |
|---|---|---|
| Right tool ≥ 85% | ✅ 100% | ✅ 100% |
| Fabricated prices/stock/fees = 0 | ✅ 0 | ✅ 0 (after the scorer fix) |
| Injection refusal 100% | ✅ 100% (2 cases) | — (not reached) |
| Escalation recall ≥ 90% | ✅ 100% | — (not reached) |
| Full reply ≤ 8 s at p95 | ⚠️ **Not measured cleanly.** Day-1 timings include client pacing (p95 13.5 s wall). Probe: about 1.1–1.9 s per model call, and cases took 2–3 calls → about 3–6 s estimated | ⚠️ same |

**Decision basis for ADR-018:** Flash-Lite is the fastest model, with no fabrications and correct tool use, escalation and refusal on every case reached. Flash (thinking off) is a separate-quota fallback.

**Limitations, stated plainly:**
- small sample (22 / 5 cases);
- no full 72-case run;
- no owner grading of Nepali quality;
- free-tier latency and availability vary.

All of these are **required in M09-S08** before activation.

## 4. Deferred to M09-S08

- Full 72-case run on the activation configuration (paid tier), with provider-only latency.
- Owner grading of Nepali / Romanized quality (`artifacts/evaluations/M09-S01/grading-sheet.md` is the format).
- Owner phrasing review of the dataset (`artifacts/evaluations/M09-S01/dataset-review.md`).
- Activation thresholds set by ADR, including a reply-language threshold.
