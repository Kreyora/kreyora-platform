# Milestone 13 — Gated Phase 2 Expansion Tracks

## Purpose

This file is a post-pilot menu, not a single milestone to execute wholesale. Each approved track becomes its own numbered milestone, branch, ADR set, prompt sequence, acceptance dossier, and release decision. Run only tracks justified by the Milestone 12 retrospective.

## Global entry rules

- MVP is stable and pilot findings are approved.
- Each track has a business owner, operational owner, evidence, success metric, rollback/disable plan, and capacity/cost estimate.
- Product truth, tenant isolation, idempotency, audit, outbox, observability, and controlled deployment rules remain unchanged.
- Do not run multiple high-risk provider/payment/data migrations concurrently unless explicitly approved.

## Track A — Additional social channel

Use this sequence separately for every new provider/channel:

### Prompt A1 — Evidence and capability delta

> Evaluate the proposed channel using current official documentation and real sandbox/app access. Compare it with the existing `IChannelProvider` capability matrix. Record approval, costs, rate/window/template rules, webhook signing, tokens/scopes, identity/media behavior, retention, review, and production restrictions. Decide whether the existing interface fits; extend it only through an ADR that preserves existing adapters. Stop if evidence/access is insufficient.

### Prompt A2 — Adapter and connection lifecycle

> Implement only verified connection, credential, refresh/revocation, capability, health, and webhook behaviors inside a provider-specific adapter. Reuse the encrypted connection model and durable ingress. Add contract tests and official sandbox/signed-fixture tests. Do not emulate unsupported capabilities.

### Prompt A3 — Normalization, outbound, and inbox UX

> Map verified events to existing normalized identities, conversations, messages, delivery states, and outbound pipeline. Add channel-specific UX only where capability differences require it. Preserve takeover, authorization, audit, retry, DLQ, replay, redaction, and tenant isolation. Add end-to-end tests.

### Prompt A4 — Sandbox, production review, and staged rollout

> Prove connect, inbound, duplicate, reply, delivery state, rate/window denial, token expiry/reauthorize, failure/replay, and takeover in the official sandbox. Document production approval separately. Roll out through tenant flags to one pilot seller, monitor accepted metrics, and retain a kill switch.

**Exit gate:** the new channel has independent production evidence and cannot regress existing channels.

## Track B — Live eSewa or Khalti gateway

Execute separately per gateway.

### Prompt B1 — Commercial and technical readiness

> Validate merchant/platform account ownership, KYC, settlement destination/timing, supported flow, fees/VAT, refunds, disputes, reconciliation, signed callback/verification, credential environments, sandbox, production approval, and customer disclosure using official evidence and business/legal/accounting owners. Select one integration model by ADR. Stop if settlement or legal responsibility is unresolved.

### Prompt B2 — Provider adapter and payment attempt

> Implement the gateway adapter behind the existing Payments boundary: create payment attempt, return safe redirect/initiation data, store provider references, verify signatures, query status when supported, handle expiration/failure, encrypt credentials, and redact logs. The browser callback is never payment proof. Add official sandbox/contract tests.

### Prompt B3 — Callback idempotency and reconciliation

> Persist callbacks immutably, validate signature/replay window, match tenant/order/attempt/amount/currency, apply idempotently, and update payment through existing state policies. Add reconciliation jobs, discrepancy queue, authorized investigation, refund hooks only if validated, and exhaustive duplicate/out-of-order/forged/mismatched tests.

### Prompt B4 — Checkout UX and staged release

> Add the gateway only when available for the current store/order and clearly show pending, success, failure, expired, and reconciliation states. Roll out behind tenant flags, prove sandbox then restricted production transactions, monitor settlement/reconciliation, and retain a disable switch that leaves COD/QR and order truth intact.

**Exit gate:** signed evidence and reconciliation—not redirect/browser input—controls `paid`.

## Track C — Custom domains and automated TLS

### Prompt C1 — Domain ownership and state machine

> Define `requested → dns_pending → verified → provisioning_tls → active | failed | disabled` with domain normalization, uniqueness, reserved names, ownership challenge, retry, expiry, and audit. Select DNS/TLS/proxy automation by ADR and document abuse/support responsibilities.

### Prompt C2 — Verification and host routing

> Implement DNS ownership verification and safe host-to-store mapping. Defend against host-header confusion, dangling mappings, tenant takeover, wildcard conflicts, and stale caches. Custom domains must never accept an arbitrary tenant identifier.

### Prompt C3 — Certificate lifecycle and UX

> Automate certificate request/renewal/failure handling through the accepted platform, expose safe seller status/remediation, alert before expiry, and retain the platform subdomain fallback. Test duplicate requests, failed DNS, renewal, disable/delete, and reassignment.

### Prompt C4 — Staged production validation

> Validate with controlled domains, monitor routing/TLS, run tenant-isolation tests, and roll out gradually with a kill/fallback path.

**Exit gate:** ownership, routing, certificate lifecycle, fallback, and support procedures are proven.

## Track D — Automated subscriptions and commercial fee ledger

### Prompt D1 — Business/accounting model

> Approve plan prices, billing periods, trials, prorations, taxes/VAT, invoices, cancellation, grace, refunds, failed payment, service fees, gateway fees, settlement, and accounting ownership with legal/accounting review. Do not copy competitor pricing as Kreyora policy.

### Prompt D2 — Subscription provider and webhook model

> Implement the selected billing provider behind an abstraction with customer/subscription references, checkout/portal if supported, signed idempotent webhooks, reconciliation, and secret isolation. Preserve the existing versioned entitlement model.

### Prompt D3 — Entitlement transitions and dunning safety

> Map verified subscription states to future-dated entitlement changes. Define grace/dunning behavior that never deletes or corrupts commerce truth. Add audit, notification, support override, replay, and time-bound tests.

### Prompt D4 — Fee ledger and reporting

> If the business model activates transaction/service fees, implement append-only fee assessments, reversals, settlements, and reconciliation separately from immutable customer order totals. Add reports and evidence required by accounting owners.

**Exit gate:** commercial calculations, signed events, entitlements, invoices/fees, reconciliation, and customer disclosures are approved.

## Track E — Returns/refunds, carriers, reports/exports, and advanced operations

Split these capabilities rather than bundling them:

### Prompt E1 — Returns/refunds

> Define return eligibility, request, approval, receipt, restock/disposition, refund, rejection, partial quantities, evidence, customer notification, and audit state machines. Coordinate inventory and gateway/manual refund evidence without rewriting original order/payment snapshots.

### Prompt E2 — Carrier integration

> Validate one carrier API/contract, account ownership, coverage, rate, pickup, tracking, webhook/signature, label, cancellation, cash collection, settlement, failure, and support behavior. Implement behind a provider boundary with reconciliation and manual fallback.

### Prompt E3 — Reports and exports

> Define report metrics and source-of-truth ownership. Build tenant-scoped asynchronous exports with authorization, row/size/time limits, formula-injection-safe CSV/XLSX generation, private expiring download, audit, retention, and job failure visibility.

### Prompt E4 — Promotions and customer accounts

> Add promotions or customer accounts only from approved seller demand. Define abuse, identity, privacy, price snapshot, stacking, inventory, cancellation, and support implications before implementation.

**Exit gate:** each sub-capability has its own domain model, tests, security/operations evidence, and staged rollout.

## Track completion report

Every Phase 2 track must state:

- Pilot evidence that justified it.
- ADRs and external validations.
- Schema/API/event changes.
- Compatibility and migration plan.
- Security/privacy/tenant review.
- Cost and capacity observations.
- Feature flag, staged rollout, monitoring, and rollback/disable evidence.
- Exact success metric and post-release review date.

