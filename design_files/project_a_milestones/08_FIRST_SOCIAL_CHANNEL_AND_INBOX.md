# Milestone 08 — First Validated Social Channel and Unified Inbox

## Objective

Implement exactly one real social-channel adapter after its capabilities, approvals, sandbox, credentials, and policies are evidenced. Connect inbound customer identities and messages to the unified inbox, enable staff reply and human takeover, and prove failures are visible and replayable.

## Dependencies and hard gate

- Milestone 07 exit gate approved.
- The chosen provider is recorded in an accepted ADR.
- Official API documentation, app/account ownership, sandbox access, production-review requirements, data-retention terms, rate limits, webhook signing rules, token lifecycle, and allowed outbound behavior are available.

If these are not available, complete only Prompt 01, mark the milestone `BLOCKED`, and do not create an imitation provider integration.

## Implementation design

Provider-specific code implements the Milestone 07 contract and stays inside the integration boundary. Conversations and messages store normalized product facts plus external references required for troubleshooting. Customer identity links are scoped to one channel connection and tenant; automatic cross-channel identity merging is not allowed without verified identifiers and policy.

Human takeover is a transactional conversation-state change checked by outbound automation. Staff replies and AI replies use the same durable outbound pipeline but different actor/origin metadata.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Provider readiness evidence and adapter contract plan | `NOT STARTED` |
| 02 | Connection/OAuth or credential lifecycle | `NOT STARTED` |
| 03 | Real webhook validation and inbound normalization | `NOT STARTED` |
| 04 | Customer identities, conversations, and messages | `NOT STARTED` |
| 05 | Staff reply, assignment, and human takeover | `NOT STARTED` |
| 06 | Unified inbox frontend integration | `NOT STARTED` |
| 07 | Sandbox/production-readiness verification | `NOT STARTED` |

## Prompt 01 — Provider readiness evidence and adapter contract plan

> Evaluate candidate first channels against the accepted capability template using current official provider documentation and actual account/app access. Record evidence for inbound/outbound features, conversation windows/templates, webhook signatures, verification challenge, token/scopes/refresh, rate limits, media, delivery receipts, sandbox limitations, app review, production restrictions, data retention, and costs. Select exactly one candidate by ADR or report the blocking gaps. Map only verified capabilities onto `IChannelProvider`; list unsupported/unknown features and required fallback UX. Do not write the adapter until the ADR is accepted.

**Review checkpoint:** accept the provider ADR and evidence, or stop the milestone as blocked.

## Prompt 02 — Connection/OAuth or credential lifecycle

> Implement the selected provider’s documented connection flow using development/sandbox credentials supplied outside source control. Validate state/redirect integrity where OAuth is used, requested scopes, account/page/phone selection as applicable, encrypted token storage, expiry/refresh/revocation, connection validation, disconnect, and reauthorization. Update capability and health records from real provider responses. Add contract/integration tests using recorded-safe fixtures or an official sandbox; never store raw secrets or personal payloads in test snapshots.

**Review checkpoint:** approve successful connect, refresh/reauthorize, disconnect, scope failure, and redaction evidence.

## Prompt 03 — Real webhook validation and inbound normalization

> Implement the provider’s official verification challenge and request-signature validation in its adapter. Resolve the correct connection from provider identifiers, persist raw events through the existing fast path, and normalize only verified inbound event types. Handle duplicates, delivery/read receipts, unsupported types, deleted messages, and provider retries according to documented semantics. Add official-sandbox or signed-fixture tests and measure acknowledgement latency separately from processing.

**Review checkpoint:** approve cryptographic validation, connection routing, normalized mapping, and duplicate/retry behavior.

## Prompt 04 — Customer identities, conversations, and messages

> Implement CustomerChannelIdentity, Customer, Conversation, Message, external references, assignment metadata, labels, unread state, and automation ownership state. Define deterministic rules for conversation lookup/creation and identity linkage within a tenant/connection. Store only necessary provider/customer fields and apply retention/redaction policies. Consume normalized provider events idempotently and update delivery state without duplicating messages. Add migrations, commands/queries, indexes, isolation tests, and out-of-order event tests.

**Review checkpoint:** approve identity boundaries, data minimization, conversation rules, and message timeline evidence.

## Prompt 05 — Staff reply, assignment, and human takeover

> Implement authorized staff reply through the durable outbound pipeline, conversation assignment/unassignment, labels, internal notes if approved, human takeover, and controlled release back to automation. The takeover transition must immediately block new automated outbound messages and cancel or suppress queued-but-not-sent AI replies safely. Preserve actor/origin and audit metadata. Enforce provider capability/window rules with clear denial reasons. Add race tests for inbound message, staff reply, AI enqueue placeholder, takeover, provider retry, and reassignment.

**Review checkpoint:** approve takeover invariant, staff reply, provider-window behavior, and audit trail.

## Prompt 06 — Unified inbox frontend integration

> Replace inbox, conversation, assignment, reply, takeover, connection health, delivery status, failure, and replay fixtures with generated real clients while retaining explicit demo mode. Implement live refresh through the simplest approved mechanism, accessible message composition, provider capability denials, optimistic reply with durable reconciliation, failed-message actions, session/permission recovery, and redacted diagnostic links. Add Owner/Admin/Operator/Viewer end-to-end tests with real backend and simulator/sandbox fixtures.

**Review checkpoint:** approve daily operator workflow and demonstrate that the inbox truth matches durable backend state.

## Prompt 07 — Sandbox/production-readiness verification

> Execute the official sandbox end-to-end path: connect, receive a real test customer message, deduplicate a retry, create/update conversation, assign, staff reply, receive delivery status if supported, take over, suppress automation, expire/revoke credentials, diagnose, reauthorize, and replay a safe failed event. Verify rate/window behavior and document every production-review prerequisite still outstanding. Do not claim production readiness without accepted credentials and review evidence.

**Review checkpoint:** approve sandbox evidence and explicitly classify production connection as `READY`, `CONDITIONALLY READY`, or `BLOCKED`.

## Milestone exit gate

- Exactly one evidence-backed provider adapter exists.
- A verified sandbox inbound message reaches the correct tenant conversation once.
- Authorized staff reply works through the durable outbox.
- Assignment and human takeover work, and automation cannot send after takeover.
- Provider errors, token expiry, retries, DLQ, and replay are visible.
- Production claims match actual approval status.

