# Milestone 06 — Order Operations, Manual Payments, Fulfilment, and Notifications

## Objective

Give sellers a safe operational workflow to confirm, cancel, verify, collect, prepare, dispatch, and fulfil orders. Complete the COD and merchant-QR manual-payment model, transactionally coordinate inventory, and deliver auditable notifications through a provider-neutral outbox.

## Dependencies

- Milestone 05 exit gate approved.
- COD collection and merchant-QR verification responsibilities have an approved operating procedure.
- No live gateway is required or permitted in this milestone.

## Implementation design

Order, payment, and fulfilment state machines are independent but coordinated through explicit application policies. Every transition records actor, tenant, reason, time, correlation ID, and prior/new state. Browser text or uploaded proof cannot itself set `paid`; only an authorized manual verification or a future signed provider callback can do so.

Cancellation, reservation release, stock allocation/commitment, and notification creation occur transactionally or through a durable outbox with compensating/retry behavior.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | State-transition policies and action authorization | `NOT STARTED` |
| 02 | COD and merchant-QR payment domain | `NOT STARTED` |
| 03 | Inventory allocation, cancellation, and fulfilment coordination | `NOT STARTED` |
| 04 | Notification outbox and development provider | `NOT STARTED` |
| 05 | Seller order workspace integration | `NOT STARTED` |
| 06 | End-to-end lifecycle and failure verification | `NOT STARTED` |

## Prompt 01 — State-transition policies and action authorization

> Implement explicit transition policies for OrderStatus, PaymentStatus, and FulfilmentStatus based on the approved state model. Expose allowed actions with denial reasons for UI use, but enforce every action server-side. Require appropriate role, reason where relevant, expected version/concurrency token, idempotency, and audit event. Prevent invalid combinations such as fulfilment after cancellation or unverified manual payment becoming paid. Add exhaustive table-driven domain and authorization tests before adding endpoints.

**Review checkpoint:** approve transition matrix, role matrix, denial reasons, and exhaustive tests.

## Prompt 02 — COD and merchant-QR payment domain

> Implement store payment configuration, PaymentAttempt, manual payment evidence/proof reference, verification decision, COD collection record, and provider-neutral transaction references. For merchant QR, support optional authorized proof upload through the existing media-security boundary, `awaiting_verification`, accept/reject with reason, amount/reference checks, and immutable verification audit. For COD, keep payment pending through fulfilment and allow authorized collection recording according to policy. Do not create a fake eSewa/Khalti integration. Add migrations, commands/queries, endpoints, authorization, idempotency, and tests.

**Review checkpoint:** approve payment model, proof handling, who can mark paid, and audit evidence.

## Prompt 03 — Inventory allocation, cancellation, and fulfilment coordination

> Implement application orchestration for order confirmation, cancellation, payment verification effects, allocation/reservation commitment, release, preparation, dispatch, delivery, failed delivery, and COD collection. Define which transition commits stock and which cancellations/releases remain possible. Use one database transaction where bounded; otherwise use durable outbox/compensation with observable intermediate states. Handle duplicate commands and worker retries safely. Add integration tests for stock reconciliation across every terminal path and concurrency conflicts between cancellation and fulfilment.

**Review checkpoint:** approve stock ownership timeline, transaction boundaries, compensation, and reconciliation results.

## Prompt 04 — Notification outbox and development provider

> Implement NotificationRequest, template/version, recipient, channel preference, delivery attempt, status, retry policy, and redacted error metadata. Create notifications from order events through the durable outbox. Implement a safe development sink that records rendered output without contacting customers. Add a provider-neutral interface for future email/SMS/channel notifications, bounded retries, dead-letter visibility, and manual replay authorization. Protect customer contact data in logs and admin views. Add template, idempotency, retry, redaction, and tenant-isolation tests.

**Review checkpoint:** approve notification lifecycle, safe development behavior, retry/DLQ visibility, and PII handling.

## Prompt 05 — Seller order workspace integration

> Replace seller order, payment, fulfilment, activity, and notification fixtures with generated real clients. Preserve the approved screens and connect allowed-action responses, version conflicts, confirmation dialogs, reason capture, QR proof review, COD collection, timeline, and notification delivery state. Enforce role-aware visibility as UX only while relying on server authorization. Add accessible loading/error/denied/conflict recovery and end-to-end tests for Owner, Operator, and Viewer.

**Review checkpoint:** approve operational UX and demonstrate that invalid actions remain impossible even with direct API calls.

## Prompt 06 — End-to-end lifecycle and failure verification

> Execute complete COD and merchant-QR order scenarios from checkout through cancellation or delivery. Include duplicate actions, stale versions, concurrent cancel/dispatch, rejected proof, partial infrastructure failure, outbox retry, notification DLQ/replay, job restart, and cross-tenant access attempts. Verify immutable snapshots, stock reconciliation, state histories, actor/reason audit, and notification status. Produce a lifecycle evidence table and fix milestone-scoped defects.

**Review checkpoint:** approve lifecycle evidence with no invalid or unaudited transition.

## Milestone exit gate

- Authorized sellers can process, cancel, verify, and fulfil orders safely.
- COD and merchant QR follow documented manual operating policies.
- No client input alone can mark payment paid.
- Inventory reconciles across confirmation, cancellation, failure, and fulfilment.
- Every transition has actor/reason/time/correlation evidence.
- Notifications are durable, retryable, redacted, and visible without using a live provider.

