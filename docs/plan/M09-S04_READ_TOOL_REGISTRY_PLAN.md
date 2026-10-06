# M09-S04 — Read-Only Commerce Tool Registry — Scoped Plan

## Identity

- **Milestone:** 09 — Constrained AI Assistant, RAG, and Commerce Tools
- **Step:** 04 — Read-only commerce tool registry
- **Author:** Claude (planning)
- **Date:** 2026-10-06
- **Status:** `REVIEW` — implemented 2026-10-06 (checkpoint `artifacts/checkpoints/M09-S04.md`); was `IN PROGRESS`, approved by the owner on 2026-10-06 ("ok implement"): Q1–Q9 as recommended (Q6-A)
- **Prerequisites:** M09-S03 `APPROVED` (2026-10-06; merged `836ab16`); ADR-018 and ADR-019 `Accepted`.
- **Manual work for the owner in this step:**
  - answer the decisions below;
  - at the checkpoint, review each tool's schema and result fields (the milestone's review requirement);
  - no keys, no money, no Meta changes. (If you pick Q6-B, one later shared-post test in the Instagram sandbox; see Q6.)

## Milestone prompt (verbatim)

> Implement a versioned, allowlisted tool registry and schema validation for read tools: SearchProducts, CheckInventory, GetPrice, GetShippingInfo, and authorized order-status lookup. Each tool must derive tenant/customer/conversation context from trusted application state, call existing application queries, minimize returned fields, enforce timeouts, and produce a traceable structured result. The model cannot provide tenant IDs, prices, stock, or authorization decisions. Add tool authorization, schema, timeout, idempotent-read, cross-tenant, and stale-data tests.

**Review checkpoint:** approve each tool schema, authorization boundary, result minimization, and trace evidence.

**Carried into this step (owner decision M1, M09-S02):** exact matching of shared Instagram posts/reels and storefront links.

## What exists today (verified 2026-10-06)

- **AI boundary (S01):** `AiToolDefinition(Name, Description, ParametersJsonSchema)`, `AiToolCall(Id, Name, ArgumentsJson, ProviderData)`; `AiChatRequest.Tools`. The evaluation harness has **fake** versions of the five tools (`tools/Kreyora.AiEvaluation/FakeTools.cs`), answered from the synthetic catalog.
- **Policy (S02):** `AssistantPolicy.ReadTools` = SearchProducts, CheckInventory, GetPrice, GetShippingInfo, GetOrderStatus; `EscalateToHuman` always allowed; write tools refused until S05. The seller chooses which read tools are on.
- **Catalog:** `IPublicStorefrontService` lists and searches **published, visible** products (title/slug `ILIKE`, ≤ 64 characters) and returns variants with `PriceNpr` / `CompareAtPriceNpr`. `IStorefrontCatalogReadService.GetPublishedVariantAsync`. Product page URL: `/store/{storeSlug}/product/{productSlug}`.
- **Inventory:** `IStorefrontInventoryReadService.GetAvailableQuantityAsync(variantId)` (available = on hand − active reservations).
- **Delivery:**
  - seller rules with zones (district / municipality / locality, free text, normalized), flat or threshold fee, ETA text, COD flag.
  - `IStorefrontQuoteService.CreateQuoteAsync` is **stateless** (signed token, no database write) and picks the rule. Rule selection itself is private (`FindRuleAsync`).
- **Payments:** `IStorePaymentConfigurationService` (COD / merchant QR availability).
- **Orders:**
  - order numbers are `ORD-{ULID}` (about 80 random bits, not guessable);
  - each order has the customer's name, phone and address, and an optional `CustomerId`;
  - `IOrderQueryService` is staff-facing and returns full detail, including PII.
- **Who the customer is in a chat:** `Conversation.CustomerChannelIdentityId` → `CustomerChannelIdentity.CustomerId`. That link exists in the model but **nothing sets it yet**, so no order is linked to an Instagram customer today. S05 (checkout link from the chat) is the natural place to create the link.
- **Shared posts:** the Instagram normalizer keeps each attachment's `type` and `payload.url` only. Which fields Meta sends for a shared post or reel (for example a media ID) was **never verified live** (S02 known issue).

## Objective

A versioned, allowlisted registry of five read tools that S06 orchestration can hand to the model. Every fact (product, price, stock, delivery, order) comes from existing application data, scoped by context the server derives itself. Also: a deterministic resolver for product links the customer sends.

## Design

### A. Registry (Q1, Q2, Q9)

- **Contracts (Application `Assistant/AssistantToolContracts.cs`):**
  - `IAssistantTool`: `Name`, `Version`, `Description`, `ParametersSchema`, `Timeout`, `ExecuteAsync(AssistantToolContext, JsonElement arguments, CancellationToken)`.
  - `IAssistantToolRegistry`:
    - `GetDefinitions(policy)`: the allowlisted tools for this shop's policy, as `AiToolDefinition`s;
    - `ExecuteAsync(context, AiToolCall)` → `AssistantToolOutcome` (result JSON for the model + trace).
- **Allowlist:** a call runs only if the tool is registered, is a read tool, **and** is enabled in the shop's policy. Anything else returns `tool_not_allowed`; nothing executes.
- **Schema validation (Q2):**
  - strict: `additionalProperties: false`, types, required fields, string length limits, integer ranges, enums;
  - invalid arguments return `invalid_arguments` with field names, so the model can correct itself;
  - unknown fields such as `tenantId`, `price` or `stock` are **rejected**, not ignored.
- **Versioning:** registry version `kreyora-read-tools.v1`; each tool has `Version = 1`. Traces record both. A schema change bumps the version.
- **Timeouts (Q9):** each tool runs under its own deadline (default 3 s, `Ai:Tools:TimeoutSeconds`). A timeout returns `timeout`; the request is never left hanging. The overall turn budget is S06.
- **Failures are values:** exceptions become `unavailable`, with no stack trace or data in the result.

### B. Trusted context

`AssistantToolContext` is built **by the server** from the conversation: tenant (trusted tenant scope), store, conversation ID, customer channel identity, linked customer ID (if any), and the policy.
- The model's arguments are **lookup keys only** (search words, product/variant IDs from earlier results, a place name, an order number). They are never authority.
- Every product/variant/order ID in arguments is re-checked against the tenant and the published catalog; an ID from another tenant answers exactly like a missing one (`not_found`).
- Data reads run in the tenant scope **and** a public-storefront scope for the tenant's store. The catalog tools therefore see exactly what the shop's public storefront shows, and nothing unpublished.

### C. The five tools (schemas and result fields; Q3–Q5)

All results share an envelope: `{ ok, data | error{code,message}, asOf, tool, version }`. No tenant IDs, internal costs, supplier data, reservations, or other customers' data. Prices are NPR from the server.

| Tool | Arguments (strict) | Returns (minimized) | Source |
|---|---|---|---|
| **SearchProducts** | `query` (1–64 chars), `limit` (1–5, default 5) | per product: `productId`, `title`, `options` (e.g. sizes/colors), `fromPriceNpr`, `available` (any variant in stock) | public catalog search + inventory read |
| **CheckInventory** | `productId`, optional `variantId`, optional `quantity` (1–100) | per variant: `variantId`, `name`, `options`, `availability` (`in_stock` / `low_stock` / `out_of_stock`), `canFulfil` (for `quantity`) — **no exact counts** (Q3) | published variant + inventory read |
| **GetPrice** | `productId`, optional `variantId` | per variant: `variantId`, `name`, `priceNpr`, `compareAtPriceNpr`; note "final total confirmed at checkout" | published variant |
| **GetShippingInfo** | `place` (1–80 chars), optional `items` [{`variantId`, `quantity`}] ≤ 10 | matched place, `feeNpr` (or base fee + `freeAboveNpr` without items), `etaText`, `codAvailable`, `qrAvailable`; or `place_not_served` / `place_unknown` with the served place names | delivery rules (new read query, Q5), stateless quote for items, payment configuration |
| **GetOrderStatus** | optional `orderNumber`, optional `phoneLast4` (4 digits) | per order: `orderNumber`, `status`, `paymentStatus`, `fulfilmentStatus`, `placedAt`, `itemCount`, `etaText` — **never name, phone, address or other PII** | new minimal order-status query (Q4) |

- **The model never supplies a price or stock figure.** `GetShippingInfo` takes variant IDs and quantities; the server prices them.
- **Order status authorization (Q4):**
  - orders linked to this chat's customer are shown without questions;
  - otherwise the customer must give the order number **and** the last 4 digits of the order's phone number;
  - wrong or unknown pairs return one uniform `not_verified` answer;
  - **5 failed attempts per order in 24 h lock chat lookups for that order** (stored per tenant and order; audited). That makes guessing the 4 digits impractical, and the ULID order number is already unguessable.
- **Place matching (Q5):** the place is normalized and matched against the shop's zone names at every level (district / municipality / locality), plus a built-in gazetteer: the 77 districts in English, Devanagari and common Romanized spellings, and major cities mapped to their district (for example Pokhara → Kaski, Biratnagar → Morang).

### D. Product references the customer sends (Q6, owner decision M1)

- **`IProductReferenceResolver`** is not a model tool. S07 runs it on each inbound message, so the model cannot invent a reference.
- **Storefront links (built now):**
  - finds URLs in the message text;
  - accepts only this shop's own storefront, in both forms: `…/store/{storeSlug}/product/{productSlug}` and `https://{storeSlug}.{PlatformBaseDomain}/product/{productSlug}`;
  - maps the link to the published product.
  - Other shops' links and foreign domains → no match.
  - No URL is ever fetched (no SSRF).
- **Instagram shared posts/reels (Q6):** the recommendation is to **capture now and match later**.
  - The normalizer starts keeping any post identifiers Meta sends (stored, not interpreted).
  - Matching (product ↔ Instagram post) waits for a live capture of real shared-post payloads in the S07 sandbox session, because those fields are unverified (no fabricated provider behavior).
  - Until then a shared post follows the S02 `UnrecognizedMediaBehavior` setting (default: ask for details).

### E. Trace (Q7)

- **Every execution produces an `AssistantToolTrace`:** tool, version, registry version, call ID, conversation ID, start, duration, outcome code, argument field names + a hash of the arguments, and result counts/IDs. **No customer text, phone digits or PII.**
- S04 logs it through structured logging (values-free, like S01/S03). S06's action log persists it with the turn.

### F. Owner tooling (Q8)

| Method & route | Permission | Purpose |
|---|---|---|
| `GET /v1/assistant/tools` | read | The registry: names, versions, descriptions, schemas, and which are on in the policy. Evidence for the schema review |
| `POST /v1/assistant/tools/{name}/preview` | write (Owner/Admin) | Run a tool as **seller preview**: shop context, no customer. `GetOrderStatus` therefore requires verification. Antiforgery |

## Tests

**Unit + integration (real PostgreSQL / Testcontainers; no network, no model):**
- **Authorization:**
  - a tool that is off in the policy, unknown, or a write tool → `tool_not_allowed`, nothing executed;
  - order status for a linked customer works; unlinked without verification → `not_verified`;
  - a wrong phone suffix → `not_verified`; the lockout triggers after 5 failures and is audited;
  - preview routes: Viewer/Operator 403, antiforgery, unauthenticated 401.
- **Schema:**
  - missing required fields, wrong types, oversize strings, out-of-range numbers → `invalid_arguments`;
  - **extra fields such as `tenantId`, `priceNpr`, `stock` → rejected**;
  - malformed JSON → `invalid_arguments`.
- **Timeout:** a deliberately slow tool → `timeout` within the deadline; the next call still works.
- **Idempotent read:** the same call twice gives the same result, and **row counts are unchanged** for every tool (the lockout counter only changes on failed verification).
- **Cross-tenant:**
  - two shops with identical products: search returns only the caller's;
  - another tenant's product/variant/order IDs → `not_found` / `not_verified`, never data;
  - another shop's storefront link → no match.
- **Stale data:**
  - change a price, stock or publication between two calls → the second call reflects it (no caching);
  - unpublished or hidden products disappear from search and lookups;
  - an active reservation lowers availability;
  - a deleted delivery rule stops matching;
  - results carry `asOf`.
- **Minimization:** result JSON contains only the documented fields. Snapshot tests per tool assert no PII, tenant IDs or exact stock counts.
- **Place matching:** zone-level matches, gazetteer aliases (English / Devanagari / Romanized), unknown places, places the shop doesn't serve.
- **Reference resolver:** both storefront URL forms; other slugs/domains; a product that isn't published; several links in one message.
- **Trace:** produced for success and every failure code; contains no argument values or PII.
- **Harness:** the evaluation tool's fake tools adopt the registry's real schemas, so S08 evaluates the real contract.

## Contracts and migrations

- **New routes:** `GET /v1/assistant/tools`, `POST /v1/assistant/tools/{name}/preview` (additive; OpenAPI regenerated).
- **New application queries (additive):**
  - delivery rule lookup by place;
  - published product with variants by ID;
  - minimal order status for a customer or for an order number + phone suffix.
- **Migration (additive):** `assistant_order_lookup_failures` (tenant, order, failure count, window start, locked until). If Q6-A: no change to message storage; the post identifiers fit the existing raw payload, and normalization keeps them in metadata.
- **Configuration:** `Ai:Tools:TimeoutSeconds` (default 3), `Ai:Tools:LowStockThreshold` (default 3).

## Security and data handling

- Tenant, store and customer always come from server state. Model arguments are lookup keys and are re-validated, and unknown fields are rejected.
- Results are minimized: no PII, no exact stock, no internal data. Order status needs a linked customer or two-factor knowledge (number + phone suffix), with a lockout.
- No URL fetching. Links are matched against the shop's own storefront patterns only.
- Traces and logs are values-free.
- Nothing in S04 calls a model or sends anything to a provider.

## Acceptance criteria

1. The registry is versioned and allowlisted with strict schema validation, per-tool timeouts, failure values and traces.
2. The five read tools return only current application data, with documented minimized fields and context derived from trusted state.
3. Order status needs a linked customer or verification with a lockout.
4. The storefront-link resolver works (plus the Q6 outcome for shared posts).
5. Authorization, schema, timeout, idempotent-read, cross-tenant, stale-data and minimization tests pass; full gates green; Docker at baseline.
6. Checkpoint `M09-S04.md` (`REVIEW`) with every tool's schema and an example result for your review.

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Where the tools get their data | **Existing application services** (public catalog, inventory read, delivery rules, payment configuration, orders), plus three small additive read queries. No tool touches the database directly |
| Q2 | Schema validation | **A small strict validator in our code** for the subset we use (objects, strings, integers, enums, arrays). No new package. Alternative: the `JsonSchema.Net` library |
| Q3 | How much stock to reveal | **Bands only** (`in_stock` / `low_stock` ≤ 3 / `out_of_stock`) + "can we fulfil N?". Exact counts never go to the model or the customer |
| Q4 | Order-status authorization | **A:** linked orders + order number **and** last 4 phone digits, uniform failure, lockout after 5 failures per order per 24 h. (B: linked orders only, which returns nothing until S05 links customers. C: always hand over to a person) |
| Q5 | Matching the customer's place to delivery zones | **Zone names at every level + a built-in gazetteer** (77 districts in English / Devanagari / Romanized, major cities → district). You review the gazetteer list at the checkpoint |
| Q6 | Instagram shared posts/reels (M1) | **A:** storefront links fully now; for shared posts, keep the identifiers Meta sends now and build matching after the S07 live capture. (B: build matching now from Meta's documentation, which needs your authorization for read-only Meta docs access plus a live check later. C: move both to S07) |
| Q7 | Tool trace persistence | **Structured, values-free trace now (logged); persisted with S06's action log** |
| Q8 | Owner tool console | **Yes:** list schemas (read) + preview a tool as seller (Owner/Admin). The screen comes in S08 |
| Q9 | Tool timeout | **3 seconds per tool** (configurable); turn budgets in S06 |

## Out of scope

- Write tools: QuoteCart, order draft, reservations, checkout link (S05).
- Orchestration and the action-log table (S06).
- Running tools in live conversations and linking chat customers to orders (S05/S07).
- UI (S08).
- Photo recognition (Phase 2).

## Build checklist

- [x] Task 1 — Registry contracts, strict schema validator, allowlist (policy ∩ registry), timeouts, failure values, trace
- [x] Task 2 — Trusted context builder (conversation → tenant/store/customer/policy) + public-storefront scope
- [x] Task 3 — SearchProducts, CheckInventory, GetPrice (+ published product query)
- [x] Task 4 — GetShippingInfo (+ delivery rule lookup by place, gazetteer, payment availability)
- [x] Task 5 — GetOrderStatus (+ minimal status query, verification, lockout table + audit; migration)
- [x] Task 6 — Storefront-link resolver; shared-post identifier capture (per Q6)
- [x] Task 7 — Tool list + preview API; OpenAPI; harness adopts real schemas
- [x] Task 8 — Tests (authorization, schema, timeout, idempotent read, cross-tenant, stale data, minimization, resolver, trace)
- [x] Task 9 — Full gates; Docker check; checkpoint `M09-S04.md` (`REVIEW`); status docs
