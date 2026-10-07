# ADR-020 — AI action permission matrix

- **Status:** `Proposed` (owner acceptance at the M09-S05 checkpoint)
- **Date:** 2026-10-07
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** M09 (S05 write tools, S06 orchestration, S07 conversation integration, S08 UI); M10 (entitlements)

## Context

M09-S05 gives the assistant its first actions with side effects. The milestone requires:
- reuse of production-tested commands;
- explicit conversation/customer context, schema validation, policy checks, idempotency, expected versions, bounded quantities, audit and confirmation rules;
- no way for the model to mark payment paid, fulfil orders, change prices, or bypass publication/stock;
- approval of the permission matrix, plus proof that tool calls can't bypass normal APIs.

This is an authorization decision about a new kind of actor (the assistant, `CommerceSystem`), so it is recorded here.

**Facts:**
- **Checkout:** the existing checkout is serializable and revalidates price, stock and publication (`CheckoutSessionService`, `OrderCreationService`). Payment is COD / merchant QR, verified by the seller.
- **Reservations:** they already support `Source = Conversation` and expire through the existing job.
- **Takeover (ADR-017):** stops automation.
- **Data policy (ADR-018):** free-tier models may receive synthetic data only, so the assistant must not collect customer contact details.

## Decision

1. **The assistant may only:**

   | Tool | Effect | Limits | Confirmation | Audit |
   |---|---|---|---|---|
   | QuoteCart | Price items + delivery (stateless signed quote; reference kept server-side) | ≤ 10 lines × 5 units | None | Replay record only |
   | ReserveInventory | Hold stock for this chat (`Source = Conversation`, 15 min) | ≤ 5 lines × 5 units; one hold per variant per chat; ≤ 10 holds per chat per 24 h | **Two-phase**: proposal → customer message after it → confirm (server-checked) | `inventory.reservation.created` |
   | ReleaseReservation | Release this chat's own active holds | Own holds only | None | `inventory.reservation.released` |
   | CreateCheckoutLink | Storefront link with the cart filled (hashed token, 24 h) | ≤ 10 lines × 5 units; ≤ 5 live links per chat | None (the customer confirms at checkout) | `assistant.checkout_link.created` / `.used` |
   | EscalateToHuman | System takeover + reason category | Idempotent | None | `assistant.escalated` |

2. **No order drafts.** Orders come only from the customer's own checkout, through the normal public APIs.
3. **Never available:**
   - mark paid or verify payment;
   - confirm, fulfil or cancel orders;
   - change prices or stock counts;
   - publish/unpublish; discounts, refunds;
   - any staff-only action.

   No such tool exists. The registry rejects every name outside this matrix and the read tools (ADR-020 is the allowlist), and rejects unknown argument fields.
4. **Context and gates:**
   - write tools need a server-built conversation context;
   - after a human takeover they return `automation_paused`;
   - seller preview runs them as a dry run (QuoteCart writes nothing anyway);
   - the shop's policy must enable each tool. Defaults: QuoteCart and CreateCheckoutLink on, holds off.
5. **Reuse:**
   - holds use a new system entry point on `InventoryService` (same locking, expiry, idempotency commands and audit);
   - quotes use `IStorefrontQuoteService`; links end in the normal checkout;
   - escalation uses `Conversation.TakeOver()` and the ADR-017 automation suppression.
6. **Idempotency:**
   - the key is hash(tenant, conversation, turn, tool, canonical arguments);
   - completed calls replay their stored result.
7. **Expected versions:** a link built from a `quoteId` must match the quote's items at still-current prices; checkout revalidates again; confirmations must match the proposed items.
8. **Handover:** a checkout from a link releases that chat's holds and reserves for checkout **inside the same serializable transaction**. The resulting order links the chat identity to the customer (only if not linked yet).

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| AI creates order drafts or orders in chat | Fewer clicks | The AI would collect name/phone/address (PII to free-tier models, ADR-018); more ways to create wrong orders | Rejected (Q1) |
| No holds | Simplest | "Hold one for me" is common for last units | Rejected; bounded holds with confirmation instead (Q3, Q4) |
| Trust the model's "customer said yes" | Simple | Prompt injection / hallucinated consent | Rejected; server checks for a customer message after the proposal |
| Quick-reply payload confirmation | Strongest proof | Depends on S07 sending quick replies on Instagram | Deferred; may harden Q4 in S07 |

## Consequences

- **Product impact:** the assistant can quote, hold, send a checkout link and hand over. The customer always finishes checkout themselves; the seller keeps payment and fulfilment.
- **Architecture impact:**
  - tables `assistant_actions`, `assistant_checkout_links`;
  - conversation escalation fields;
  - `IConversationInventoryHoldService`; optional handover hooks in the checkout session and order commands;
  - public `GET …/assistant-links/{token}`; a storefront landing page.
- **Security/privacy impact:**
  - link tokens are random and stored hashed; the public read has no PII and is not cached;
  - every write is tenant-scoped, audited as `CommerceSystem` with the conversation, and bounded;
  - after takeover the AI cannot act.
- **Operations:** holds expire through the existing reservation job. Links expire after 24 h (no job needed; the expiry is checked on read).
- **Migration or rollback:** additive migration. Rollback = disable the write tools in policies (or the platform AI switch).

## Validation evidence

- Duplicate, malformed, unauthorized, stale, cancellation and cross-tenant tests;
- the confirmation rule;
- the hold handover under a concurrent buyer;
- the order → identity link;
- the no-bypass demonstration (writes go through the same services and produce the same rows as the normal APIs);
- frontend landing-page tests.

## Supersession conditions

- Plan entitlements (M10) add per-plan tool limits.
- S07 adopts quick-reply confirmation.
- A payment gateway (Phase 2) changes what checkout links may carry.
