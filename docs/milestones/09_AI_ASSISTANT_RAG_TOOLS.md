# Milestone 09 — Constrained AI Assistant, RAG, and Commerce Tools

## Objective

Add a multilingual seller assistant that can participate safely in the validated social channel. The assistant may interpret language and compose responses, but catalog, inventory, price, delivery, payment, and order facts must always come from authorized application tools. Human takeover must stop automation immediately.

## Dependencies and hard gates

- Milestone 08 exit gate approved or the simulator path is explicitly accepted for pre-production AI evaluation.
- Initial AI provider is selected by an accepted quality/cost/latency/privacy ADR.
- Data-processing terms and redaction policy are approved before real customer content is sent.

## Implementation design

The pipeline is: inbound message → policy/ownership/entitlement checks → bounded orchestration → approved retrieval and/or authorized tool calls → validated response → outbound outbox. All inputs and outputs are tenant-scoped. Tool schemas are versioned; write tools require customer/conversation context, authorization, validation, idempotency, and audit.

RAG is limited to approved seller FAQ, delivery, returns, and brand material. Application tools provide all live commerce facts. Logs store redacted summaries and structured traces rather than unrestricted prompts or PII.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Provider evaluation and AI boundary | `NOT STARTED` |
| 02 | Assistant policy and approved knowledge lifecycle | `NOT STARTED` |
| 03 | Tenant-isolated retrieval | `NOT STARTED` |
| 04 | Read-only commerce tool registry | `NOT STARTED` |
| 05 | Controlled write tools | `NOT STARTED` |
| 06 | Bounded orchestration, budgets, and action logs | `NOT STARTED` |
| 07 | Conversation integration, escalation, and takeover | `NOT STARTED` |
| 08 | Frontend integration and multilingual/adversarial evaluation | `NOT STARTED` |

## Prompt 01 — Provider evaluation and AI boundary

> Define an evaluation dataset representative of Nepal social commerce: Nepali Devanagari, English, Romanized Nepali, code mixing, price/stock, size/color ambiguity, delivery/COD/QR, unavailable items, complaints, order status, prompt injection, sensitive data, and escalation. Benchmark approved candidate models for structured tool use, grounding, latency, cost, privacy terms, retention, regional availability, and failure behavior. Record the provider/model choice and fallback/disable policy by ADR. Implement only a provider-neutral AI client contract, configuration validation, safe development fake, and contract tests in this step.

**Review checkpoint:** accept provider ADR, evaluation baseline, data policy, and disable/fallback behavior.

## Prompt 02 — Assistant policy and approved knowledge lifecycle

> Implement tenant AssistantPolicy with activation state, supported languages, tone/brand constraints, business hours, escalation rules, allowed tools, maximum budgets, and safe defaults. Implement KnowledgeDocument lifecycle from upload/reference through validation, extraction, review, approval, active version, supersession, rejection, and deletion. Only approved active content may be retrieved. Reuse secure media/storage paths, enforce type/size limits, and avoid sending raw unsupported documents to the model. Add authorization, audit, migrations, APIs, and lifecycle tests.

**Review checkpoint:** approve safe defaults, activation/readiness checks, document approval workflow, and data handling.

## Prompt 03 — Tenant-isolated retrieval

> Implement provider-neutral embedding and retrieval interfaces, chunk/version metadata, tenant/document filters, deterministic citations/source references, deletion/reindex workflow, and an offline test implementation. Choose PostgreSQL vector support or another architecture only through an ADR with operational consequences. Retrieval must filter tenant and approval state before similarity ranking, not after. Add tests for cross-tenant collision attempts, stale/superseded/deleted chunks, empty/low-confidence retrieval, malicious document instructions, and citation traceability.

**Review checkpoint:** approve retrieval ADR and prove cross-tenant/approval isolation.

## Prompt 04 — Read-only commerce tool registry

> Implement a versioned, allowlisted tool registry and schema validation for read tools: SearchProducts, CheckInventory, GetPrice, GetShippingInfo, and authorized order-status lookup. Each tool must derive tenant/customer/conversation context from trusted application state, call existing application queries, minimize returned fields, enforce timeouts, and produce a traceable structured result. The model cannot provide tenant IDs, prices, stock, or authorization decisions. Add tool authorization, schema, timeout, idempotent-read, cross-tenant, and stale-data tests.

**Review checkpoint:** approve each tool schema, authorization boundary, result minimization, and trace evidence.

## Prompt 05 — Controlled write tools

> Implement controlled write tools for CreateQuote or OrderDraft, ReserveInventory, ReleaseInventory, CreateCheckoutLink, and EscalateToHuman. Reuse production-tested application commands rather than duplicating business logic. Require explicit conversation/customer context, schema validation, entitlement/policy checks, idempotency, expected versions where relevant, bounded quantities, audit, and user confirmation rules for consequential actions. Do not let the model mark payment paid, fulfil orders, change prices, or bypass publication/stock. Add duplicate-call, malformed-argument, unauthorized, stale, cancellation, and cross-tenant tests.

**Review checkpoint:** approve the AI action permission matrix and demonstrate that tool calls cannot bypass normal APIs.

## Prompt 06 — Bounded orchestration, budgets, and action logs

> Implement the assistant orchestration loop with system/policy versioning, normalized inbound context, approved retrieval, tool selection/execution, maximum tool iterations, total timeout, response-size limits, token/cost budget, per-tenant concurrency, transient retry policy, circuit breaker/kill switch, safe fallback, and redacted AIActionLog. Validate final output against conversation ownership and content rules before enqueueing. Never log raw secrets, unnecessary PII, or unrestricted chain-of-thought. Add deterministic fake-model tests for loops, timeouts, malformed calls, hallucinated tool results, provider failure, budget exhaustion, and redaction.

**Review checkpoint:** approve budget/timeout values, failure responses, log schema, and kill-switch evidence.

## Prompt 07 — Conversation integration, escalation, and takeover

> Connect the orchestration pipeline to normalized inbound messages. Check connection capability, assistant readiness, tenant entitlement, conversation ownership, customer safety state, and rate limits before invocation and again before outbound enqueue. Implement escalation reasons and staff queue behavior. Make human takeover transactionally suppress queued AI responses and block future automation until authorized release. Add race tests for simultaneous inbound messages, AI completion, staff reply, takeover, release, duplicate events, and provider retry.

**Review checkpoint:** approve end-to-end ownership semantics and prove no bot message is sent after takeover.

## Prompt 08 — Frontend integration and multilingual/adversarial evaluation

> Replace assistant policy, knowledge, test-console, tool-trace, usage, escalation, and AI action-log fixtures with real clients while retaining demo mode. Run the approved offline evaluation suite across Nepali, English, Romanized Nepali, ambiguity, unavailable stock, delivery/COD/QR, order status, prompt injection, data-exfiltration attempts, abusive content, complaints, and escalation. Score grounding, correct tool choice, unauthorized-action refusal, language quality, handoff, latency, and estimated cost. Define pass thresholds by ADR, fix milestone-scoped failures, and keep automation disabled if thresholds are not met.

**Review checkpoint:** accept evaluation evidence and explicitly approve or reject pilot AI activation.

## Milestone exit gate

- Every commerce claim is traceable to an application tool or approved knowledge source.
- Cross-tenant retrieval/tool access is impossible in tests.
- Write tools reuse authorized, idempotent application commands.
- Tool loops, latency, cost, concurrency, and entitlements are bounded.
- Logs are auditable and redacted.
- Human takeover prevents all automated outbound messages.
- Multilingual and adversarial evaluation meets accepted thresholds; otherwise AI remains disabled.

