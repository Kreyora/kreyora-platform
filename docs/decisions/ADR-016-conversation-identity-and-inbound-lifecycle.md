# ADR-016 — Conversation Identity and Inbound Lifecycle Rules

- **Status:** `Accepted`
- **Date:** 2026-10-05
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** Milestone 08, Milestone 09

> Accepted by the project owner on 2026-10-05 together with the M08-S04 plan ("implement"), decisions D1–D3 in `docs/plan/M08-S04_CONVERSATIONS_MESSAGES_PLAN.md`.

## Context

Plan §10.4 defines the conversation states (`new → bot_active ↔ human_assigned → awaiting_customer → checkout_in_progress → order_created → resolved`, with `closed` and `spam` as dispositions). It does not say:

- how inbound provider events map to customers and threads;
- what happens when a customer writes into a resolved, closed, or spam thread.

Milestone 08 requires deterministic lookup/creation rules, identity links scoped to one tenant and connection, and no automatic cross-channel merging.

The existing `Customer` aggregate (M05) is a checkout customer. It requires a Nepal mobile number (unique per tenant) and privacy acknowledgement. A social-channel sender has none of these.

## Decision

1. **Channel identity.**
   - A `CustomerChannelIdentity` is unique per `(ConnectionId, ExternalUserId)`. It belongs to one tenant and one connection.
   - Its optional link to a checkout `Customer` is set only by an explicit later action, never automatically.
   - The M05 `Customer` invariants are unchanged.
2. **One thread per identity per connection.**
   - `Conversation` is unique per `(ConnectionId, CustomerChannelIdentityId)`, matching the provider's single DM thread.
   - A new thread starts as `New` with `AutomationMode = Automated` (plan: `new → bot_active`). AI invocation itself stays gated by M09 entitlement and enablement.
   - `StoreId` is copied from the connection (ADR-010).
3. **Inbound on an existing thread.**
   - `Resolved` or `Closed` reopens to `New`.
   - `Spam` stays `Spam`: the message is stored but the unread count is not incremented.
   - Otherwise the status is unchanged and the unread count increments.
   - Last-message and last-customer-message times take the maximum of their current value and the event's provider time.
4. **Ordering and receipts.**
   - The timeline is ordered by provider time, not arrival.
   - Read receipts advance a per-conversation watermark, and advance an outbound message's delivery status monotonically when it exists.
   - Reactions are current-state rows per reactor and provider message, last write wins by provider time, with no foreign key to the message.
5. **Atomicity.** Conversation state is written in the same unit of work as the inbound event (ADR-015 dedup). Concurrent first contact is resolved by unique indexes plus the processing retry.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Make `Customer.Phone` optional and use it for social senders | One customer table | Weakens M05 checkout invariants and phone uniqueness; invites implicit merging | Rejected |
| New thread per session (after resolve) | Clean per-case history | Diverges from the provider's single thread; ambiguous lookup | Rejected |
| Spam reopens like resolved | Simple | Spammers regain inbox attention | Rejected |
| Separate consumer job for inbound events | Decoupled | Eventual consistency, new watermark state, more failure modes | Rejected for MVP |

## Consequences

- **Product impact:**
  - Each customer has one continuous thread per connected account.
  - Resolved threads reopen when the customer writes again.
  - Spam stays quiet.
  - The inbox shows a masked identifier until a profile source provides a name.
- **Architecture impact:**
  - New Conversations module (Domain/Application/Infrastructure).
  - Webhook processing calls `IConversationIngestionService` as an explicit application orchestrator.
- **Security/privacy impact:**
  - Identities never cross connections or tenants.
  - Only necessary fields are stored.
  - Owners can erase an identity's content (audited, counts-only metadata).
  - A message retention period is still to be decided before production.
- **Cost/operations impact:** none beyond new tables and indexes.
- **Migration or rollback impact:** additive migration `AddConversationsMessagesAndChannelIdentities` (five tables). Rollback drops them.

## Validation evidence

- `ConversationDomainTests`: status rules, watermarks, monotonic delivery, reaction last-write-wins, masking.
- `ConversationIngestionIntegrationTests`:
  - first/second message, duplicates;
  - out-of-order messages and reactions, receipts;
  - concurrent first contact;
  - ADR-016 status rules;
  - cross-tenant identity separation;
  - atomic rollback;
  - erasure.
- `ConversationEndpointTests`: authentication, roles, antiforgery, cross-tenant `404`, paging, keyset timeline, minimization.

## Supersession conditions

- Verified cross-channel identifiers plus an approved merge policy.
- Providers with multiple threads per customer.
- Product demand for per-case threads.
