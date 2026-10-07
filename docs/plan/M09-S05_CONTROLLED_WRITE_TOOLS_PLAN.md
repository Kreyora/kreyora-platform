# M09-S05 — Controlled Write Tools — Scoped Plan

## Identity

- **Milestone:** 09 — Constrained AI Assistant, RAG, and Commerce Tools
- **Step:** 05 — Controlled write tools
- **Author:** Claude (planning)
- **Date:** 2026-10-07
- **Status:** `REVIEW` — implemented 2026-10-07 (checkpoint `artifacts/checkpoints/M09-S05.md`); was `IN PROGRESS`, approved by the owner on 2026-10-07 ("ok implement"): Q1–Q9 as recommended
- **Prerequisites:** M09-S04 `APPROVED` (2026-10-07; merged `e156a14`); ADR-018, ADR-019 `Accepted`.
- **Manual work for the owner in this step:**
  - answer the decisions below;
  - at the checkpoint, review the **AI action permission matrix** (the milestone's review requirement) and ADR-020;
  - optional at the checkpoint: open one checkout link locally and complete a test COD order (about 5 minutes, synthetic data);
  - no keys, no money, no Meta changes.

## Milestone prompt (verbatim)

> Implement controlled write tools for CreateQuote or OrderDraft, ReserveInventory, ReleaseInventory, CreateCheckoutLink, and EscalateToHuman. Reuse production-tested application commands rather than duplicating business logic. Require explicit conversation/customer context, schema validation, entitlement/policy checks, idempotency, expected versions where relevant, bounded quantities, audit, and user confirmation rules for consequential actions. Do not let the model mark payment paid, fulfil orders, change prices, or bypass publication/stock. Add duplicate-call, malformed-argument, unauthorized, stale, cancellation, and cross-tenant tests.

**Review checkpoint:** approve the AI action permission matrix and demonstrate that tool calls cannot bypass normal APIs.

## What exists today (verified 2026-10-07)

- **Registry (S04):** versioned allowlist, strict schemas, per-call scope + deadline, envelope + values-free trace, trusted context (tenant, store, conversation, customer identity, linked customer, policy). Read tools only; `AssistantPolicy.WriteTools` (QuoteCart, CreateOrderDraft, ReserveInventory, ReleaseReservation, CreateCheckoutLink) **cannot be enabled yet**.
- **Quotes:** `IStorefrontQuoteService.CreateQuoteAsync` is stateless (signed token, 10 minutes), checks publication, visibility and stock, and prices on the server. `RevalidateForCheckoutAsync` returns a conflict if anything changed.
- **Inventory:**
  - `InventoryReservationSource.Conversation` already exists;
  - reservations expire through the existing job (all sources, default 15 minutes);
  - reserve/release go through `InventoryService` (serializable, retried, audited, idempotency commands). The staff entry points demand `inventory.write`; the checkout path is a system path without a user.
- **Checkout:**
  - `CheckoutSessionService.CreateAsync` runs in **one serializable transaction**: revalidate quote → resolve customer → reserve for checkout;
  - `OrderCreationService.CreateFromCheckoutAsync` commits the reservations;
  - payment is COD / merchant QR, verified by the seller.
  - The storefront cart is client-side (`use-cart`).
- **Takeover (ADR-017):**
  - `Conversation.TakeOver()` / `Release()`; `AutomationMode` Automated / HumanTakeover;
  - the staff `TakeOverAsync` is audited and needs a member.
- **Gap from S04:** chat customers are not linked to orders (`CustomerChannelIdentity.CustomerId` is never set).
- **Entitlements (plans and quotas):** Milestone 10. Nothing exists yet.

## Objective

Let the assistant move a sale forward **without ever being able to do what a seller or customer must do**:
- it can **quote**, **hold** stock briefly, **send a checkout link**, **release** its holds, and **hand over to a person**;
- the customer still enters their details and places the order on the storefront, through the normal APIs;
- the seller still confirms, verifies payment and fulfils.

## Design

### A. The AI action permission matrix (ADR-020, Q8)

| Tool | What it may do | What it can never do | Preconditions | Limits | Confirmation (Q4) | Audit |
|---|---|---|---|---|---|---|
| **QuoteCart** | Price items + delivery for a place (stateless quote) | Change prices; quote unpublished/hidden/out-of-stock items | Read context | ≤ 10 lines, 1–5 units each | None (no effect) | No (no write) |
| **ReserveInventory** | Hold stock for this chat for 15 minutes (`Source = Conversation`) | Hold for anyone else; exceed stock; extend indefinitely | Conversation context, automation active | ≤ 5 lines, 1–5 units each; one active hold per variant per chat; ≤ 10 holds per chat per day | **Two-phase**: propose → customer replies → confirm | `inventory.reservation.created` (system actor) |
| **ReleaseReservation** | Release this chat's own active holds | Release other chats', checkout or staff reservations | Conversation context | Own holds only | None (safe direction) | `inventory.reservation.released` |
| **CreateCheckoutLink** | Create a link to the storefront with the cart filled | Create orders; set customer details; choose payment; mark paid | Conversation context, automation active | ≤ 10 lines, 1–5 units; 24 h validity; ≤ 5 live links per chat | None (the customer confirms at checkout) | `assistant.checkout_link.created` |
| **EscalateToHuman** | Hand the chat to a person (takeover) with a reason category | Release a takeover; message after takeover | Conversation context | Idempotent | None (safe direction) | `assistant.escalated` |

**Never available to the AI:** mark paid, verify payment, confirm/fulfil/cancel orders, change prices or stock counts, publish/unpublish, discounts, refunds, any staff action. No such tool exists, and the registry only knows the five above.

**After a human takeover:** every write tool returns `automation_paused`; `EscalateToHuman` returns success without change.

### B. Tools in detail (Q1–Q3, Q5)

- **QuoteCart** (Q1: a quote, no order drafts):
  - `{ items[{variantId, quantity}], place }`;
  - resolves the place like `GetShippingInfo` (zones + gazetteer), then calls the existing `CreateQuoteAsync`;
  - returns lines, delivery, totals and a `quoteId` (the signed token, kept server-side as the reference for a link; it expires in 10 minutes).
  - `CreateOrderDraft` is removed from the write-tool list: orders come only from customer checkout.
- **ReserveInventory** (Q3):
  - `{ items[{variantId, quantity}], confirmationId? }`;
  - **Phase 1** (no `confirmationId`): validates (published, visible, in stock, within limits), stores a pending action (10 minutes) and returns `confirmation_required` with a customer-readable summary and a `confirmationId`.
  - **Phase 2** (with `confirmationId`): executes **only if** the pending action belongs to this chat, is unexpired, has identical items, **and a customer message arrived after the proposal** (server-checked from the conversation timeline).
  - Holds are created through a new **system entry point on `InventoryService`** that reuses the same locked, retried, audited reservation logic as checkout (no copy of business rules), with `Source = Conversation` and `ReferenceId = conversation`.
- **ReleaseReservation:**
  - `{ reservationIds? }`; empty = all of this chat's active holds;
  - reuses the existing release logic through the system entry point.
  - IDs that aren't this chat's active holds → `not_found`.
- **CreateCheckoutLink** (Q2):
  - `{ items[{variantId, quantity}] }` or `{ quoteId }`;
  - validates through the quote service, then stores an **`AssistantCheckoutLink`**: tenant, store, conversation, customer identity, lines, quote reference, a random 128-bit token **stored only as a hash**, 24 h expiry, state;
  - returns `https://{storefront}/store/{slug}/link/{token}`.
  - **Storefront side (included, Q2):**
    1. public `GET …/assistant-links/{token}` returns the lines with **current** published prices and availability (rate-limited, no PII);
    2. a small landing page fills the cart and opens checkout;
    3. the checkout session request carries the link token.
  - **At checkout:**
    - the normal session and order APIs run unchanged (revalidation, stock, publication, payment rules);
    - inside the existing serializable transaction, this chat's holds for those variants are **released and re-reserved for checkout atomically**, so the last unit can't be lost in between;
    - when the order is created, the link is marked used and **the chat identity is linked to the customer** (closes the S04 gap; only if not linked yet).
- **EscalateToHuman:**
  - `{ category (fixed list incl. the S02 escalation categories), confidence? }`;
  - a new system command on the conversation service: `TakeOver()` + status HumanAssigned + `EscalationCategory` / `EscalatedAt` on the conversation + audit (category only, never customer text).
  - The inbox shows the reason (field exposed in the conversation detail API).

### C. Cross-cutting rules (Q6, Q7, Q9)

- **Context:** write tools require a **conversation context**; seller preview has none.
- **Seller preview (Q5):** preview runs QuoteCart for real (it writes nothing); the other write tools run as a **dry run**, validating and returning "would hold / would create link" without writing.
- **Policy (Q9):**
  - write tools become selectable in the policy;
  - new policies get **QuoteCart and CreateCheckoutLink on, ReserveInventory and ReleaseReservation off**;
  - EscalateToHuman is always on;
  - existing saved policies are unchanged.
  - **Entitlements:** plan entitlements are M10; until then, policy + platform switch. [UNRESOLVED] wired in M10.
- **Idempotency:**
  - each write call has a key = hash(conversation, turn ID, tool, canonical arguments); S06 provides the turn ID, and tests set it;
  - a duplicate call replays the first result (stored with the action);
  - business rules also prevent duplicates across turns: one hold per variant per chat, and an identical live link is returned instead of a new one.
- **Expected versions:** a link made from a `quoteId` must match the quote (a changed price or stock means `stale`, and the model re-quotes); checkout revalidates again. Phase-2 confirmation must match the proposed items exactly.
- **Bounded quantities:** schema limits plus per-chat daily caps (above).
- **Cancellation:** every write runs in one transaction; a cancelled request leaves no partial hold, link or takeover (tested).
- **Trace:** S04 trace plus `actionId` / `confirmationId` (no values). S06 persists traces.

## Tests

**Unit + integration (real PostgreSQL, full DI; no network, no model):**
- **Duplicate calls:**
  - the same turn and arguments replay one result (one hold, one link, one escalation audit);
  - re-reserving the same variant doesn't stack;
  - an identical live link is reused.
- **Malformed arguments:** wrong types, quantities 0/6/101, > 5 or 10 lines, duplicate variants, smuggled `priceNpr` / `tenantId` / `paid` / `status`, bad `confirmationId`.
- **Unauthorized:**
  - write tool disabled in the policy;
  - seller preview (dry run only);
  - after human takeover (`automation_paused`);
  - releasing another chat's, checkout or staff reservations;
  - public link endpoint with a wrong / expired / used token;
  - preview roles, antiforgery and unauthenticated requests on the APIs.
- **Stale:**
  - price changed after a quote → link from `quoteId` refused;
  - product hidden or unpublished, or stock gone, between proposal and confirmation → refused;
  - expired confirmation or link;
  - checkout via link revalidates (the price shown is the current one).
- **Cancellation:** cancelled mid-call → no hold, link or takeover rows; a released hold returns stock; the expiry job expires holds.
- **Cross-tenant:** another tenant's variants, reservations, conversations and link tokens → not found; no data.
- **Confirmation rule:**
  - phase 2 without a newer customer message → `confirmation_required` again;
  - with one → hold created;
  - changed items → refused.
- **Handover:**
  - checkout from a link with holds keeps the units (a concurrent buyer can't take them);
  - the order links the chat identity → S04 `GetOrderStatus` shows it without questions.
- **"No bypass" demonstration:**
  - every write goes through the same services as the normal APIs (a test asserts the audit and reservation rows match what a normal checkout produces);
  - no tool can mark paid, fulfil, change price, or reserve an unpublished item.
- **Frontend:** landing page (fills the cart, opens checkout, shows errors for expired/invalid links), client port + adapter tests.

## Contracts and migrations

- **New routes:**
  - public `GET public/v1/store/assistant-links/{token}` (+ dev slug route);
  - the checkout session request gains an optional `assistantLinkToken`;
  - the conversation detail gains `escalationCategory` / `escalatedAt`.
- **Migration (additive):**
  - `assistant_checkout_links`;
  - `assistant_pending_actions` (confirmations + idempotent replays);
  - `conversations.escalation_category`, `escalated_at`.
- **Configuration:** `Ai:Tools:HoldMinutes` (15), `Ai:Tools:LinkHours` (24), per-chat caps.
- **ADR-020:** AI action permission matrix (Proposed → acceptance at the checkpoint).
- **OpenAPI** regenerated; frontend client port for the link.

## Security and data handling

- The AI never sees or sets customer contact details; the customer types them on the storefront (fits ADR-018's free-tier "synthetic only" rule).
- Link tokens are random and stored hashed. The public endpoint returns no PII and is rate-limited. Links expire and are single-use for order linking.
- All writes are tenant-scoped, audited as `CommerceSystem` with the conversation ID, and bounded per chat.
- After takeover, the AI cannot act.

## Acceptance criteria

1. Five write tools behind the registry: QuoteCart, ReserveInventory, ReleaseReservation, CreateCheckoutLink, EscalateToHuman.
2. Each reuses the existing services, with context, schemas, policy, idempotency, expected versions, limits, audit and the confirmation rule.
3. The checkout link works end to end through the normal checkout (storefront landing page included); holds hand over atomically; the order links the chat customer.
4. The model cannot mark paid, fulfil, change prices or bypass publication/stock (matrix + tests).
5. Duplicate, malformed, unauthorized, stale, cancellation and cross-tenant tests pass; full gates green; Docker at baseline.
6. ADR-020 and checkpoint `M09-S05.md` (`REVIEW`).

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | "CreateQuote or OrderDraft" | **QuoteCart only.** No AI-created orders or drafts; the customer places the order at checkout. Remove `CreateOrderDraft` from the tool list |
| Q2 | Checkout link | **Saved link (hashed token, 24 h) → storefront landing page fills the cart → normal checkout.** Includes the small storefront page now, so links work end to end. The order links the chat customer |
| Q3 | Stock holds from chat | **Yes, short and bounded:** 15 minutes, ≤ 5 lines × 5 units, one hold per item per chat, ≤ 10 per chat per day; released by the AI, staff, expiry, or handed over to checkout |
| Q4 | Confirmation for consequential actions | **Two-phase for holds** (summary → customer replies → confirm, checked by the server). No extra step for quote and link (the customer confirms at checkout) or escalation |
| Q5 | Seller preview of write tools | **QuoteCart real; the others dry-run** (validate, show what would happen, write nothing) |
| Q6 | After human takeover | **All write tools refused** (`automation_paused`); escalation is a no-op success |
| Q7 | EscalateToHuman | **System takeover + reason category shown in the inbox + audit** (no customer text). Staff notifications come with the inbox work in S07/S08 |
| Q8 | Record the permission matrix as an ADR | **Yes, ADR-020** (Proposed now, accepted at the checkpoint) |
| Q9 | Defaults for new shops | **QuoteCart + CreateCheckoutLink on; holds off** (seller opt-in); EscalateToHuman always on; existing policies untouched |

## Out of scope

- Orchestration, budgets, prompts and persisted action logs (S06).
- Running in live conversations and sending links in DMs (S07).
- Seller UI for links, holds and escalations (S08).
- Plan entitlements (M10).
- Payment gateways (Phase 2).

## Build checklist

- [x] Task 1 — ADR-020; policy: write tools selectable, new defaults, remove `CreateOrderDraft`
- [x] Task 2 — Registry write support: conversation-only, takeover gate, dry-run preview, idempotency/replay store, pending-action confirmations; migration
- [x] Task 3 — QuoteCart (place resolution + existing quote service)
- [x] Task 4 — ReserveInventory / ReleaseReservation via a system entry point on `InventoryService` (`Source = Conversation`), caps, two-phase confirmation
- [x] Task 5 — CreateCheckoutLink: link entity, public endpoint, atomic hold handover in checkout, order → identity link
- [x] Task 6 — EscalateToHuman: system takeover command, category fields, audit, detail API
- [x] Task 7 — Storefront landing page + client port; OpenAPI
- [x] Task 8 — Tests (duplicate, malformed, unauthorized, stale, cancellation, cross-tenant, confirmation, handover, no-bypass, frontend)
- [x] Task 9 — Full gates; Docker check; checkpoint `M09-S05.md` (`REVIEW`); status docs
