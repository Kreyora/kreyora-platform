# Milestone 10 — Onboarding, Dashboard, Entitlements, and Usage

## Objective

Turn implemented features into an operable seller product. Add resumable onboarding and activation readiness, deterministic plan entitlements and usage quotas, operational dashboard projections, and alerts at 70/90/100 percent usage. Do not implement automated subscription collection.

## Dependencies

- Milestone 09 exit gate approved, with AI either approved for pilot or explicitly disabled.
- Plan names, limits, and commercial prices may remain administrative configuration until business owners approve them.
- Quota enforcement must never corrupt existing orders, messages, or inventory.

## Implementation design

Readiness is computed from real module facts, not editable checklist booleans. Each requirement reports `complete`, `incomplete`, `blocked`, or `not_applicable`, with a safe corrective route. Storefront activation and AI activation have separate readiness gates.

Plans and entitlements are versioned. A tenant subscription/assignment references a plan version for a defined period. `UsageEvent` is append-only and idempotent; projections calculate current usage. Enforcement policies are feature-specific and distinguish warning, soft limit, hard limit, and safety override.

Dashboard numbers are read models sourced from domain/integration events, never alternative commerce truth.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Real onboarding/readiness engine | `NOT STARTED` |
| 02 | Versioned plans, entitlements, and manual assignment | `NOT STARTED` |
| 03 | Usage events, projections, and quota decisions | `NOT STARTED` |
| 04 | Dashboard and reporting projections | `NOT STARTED` |
| 05 | Frontend integration and quota/readiness UX | `NOT STARTED` |
| 06 | Fresh-tenant activation and first-order verification | `NOT STARTED` |

## Prompt 01 — Real onboarding/readiness engine

> Replace onboarding fixture states with a real readiness engine. Define versioned requirements for tenant profile, store branding/policies, published catalog, inventory, delivery, COD/QR configuration, team/security basics, validated channel, assistant policy/knowledge/evaluation, and activation review. Each check must query its owning module through stable contracts and return status, evidence timestamp, blocker code, and corrective route. Keep storefront and AI activation gates separate. Add resumability, authorization, audit, APIs, and tests for stale/incomplete/blocked/not-applicable states.

**Review checkpoint:** approve requirement ownership, activation gates, and corrective navigation.

## Prompt 02 — Versioned plans, entitlements, and manual assignment

> Implement Plan, PlanVersion, EntitlementDefinition, limit/rule values, TenantPlanAssignment or Subscription record, effective period, status, and administrative/manual assignment workflow. Define feature keys for product limits, order volume, channel connections, AI usage, team seats, and other approved MVP controls without copying competitor limits as business facts. Changes create new versions rather than silently rewriting historical rules. Add policy checks, role/support safeguards, audit, migrations, APIs, and deterministic date-boundary tests. Do not add card billing or automatic invoicing.

**Review checkpoint:** approve versioning, assignment ownership, feature keys, and historical determinism.

## Prompt 03 — Usage events, projections, and quota decisions

> Implement append-only UsageEvent with tenant, metric, quantity, source, occurrence time, billing/measurement period, idempotency key, and correlation reference. Build retry-safe projections and a quota decision service returning allowed, warning, soft-limited, hard-limited, current, limit, reset, and reason. Integrate measurement points for products, orders, connected channels, outbound/AI usage, and seats only where definitions are accepted. Generate notification events at 70/90/100 percent without duplicates. Test replay/rebuild, late events, duplicate events, period boundaries, plan changes, concurrency, and failure behavior.

**Review checkpoint:** approve measurement definitions, enforcement behavior, projection rebuild, and notification thresholds.

## Prompt 04 — Dashboard and reporting projections

> Implement event-fed dashboard projections for setup progress, order count/revenue snapshot, payment/fulfilment states, open/unread conversations, reply time, AI/human/escalation counts, low stock, integration health, webhook/job backlog, and plan usage. Define freshness timestamps and degraded/stale indicators. Protect tenant scope and avoid scanning transactional tables on every dashboard request. Add rebuild/reconciliation jobs, APIs, pagination/time filters where needed, and tests comparing projections with authoritative records.

**Review checkpoint:** approve metric definitions, freshness semantics, reconciliation, and tenant isolation.

## Prompt 05 — Frontend integration and quota/readiness UX

> Replace onboarding, readiness, dashboard, usage, plan, and entitlement fixtures with generated real clients while retaining demo mode. Show clear progress, blockers, corrective actions, projection freshness, health degradation, quota warnings, reset dates, and feature-specific hard/soft limit behavior. Preserve historical orders/messages even when new actions are limited. Implement accessible charts only where they add value, responsive summaries, loading/error/empty/denied states, and tests for Owner/Admin/Operator/Viewer.

**Review checkpoint:** approve operational clarity and verify the UI never presents stale projections as current without indication.

## Prompt 06 — Fresh-tenant activation and first-order verification

> Starting from an empty production-shaped database, create a new tenant and complete the real path: owner identity, workspace, store profile, catalog/variant/media, inventory, delivery, COD/QR, storefront activation, test order, seller processing, channel connection readiness, inbound sandbox/simulator message, human takeover, AI test/evaluation state, dashboard update, and usage event. Verify every readiness transition, quota count, audit event, projection, and permission. Repeat relevant steps to prove idempotency and rebuild projections from events. Produce the milestone acceptance dossier.

**Review checkpoint:** approve the fresh-tenant dossier and all activation/usage evidence.

## Milestone exit gate

- Onboarding derives from real module readiness and is resumable.
- Storefront and AI activation are independently gated.
- Plan and entitlement decisions are versioned and deterministic.
- Usage is append-only, idempotent, rebuildable, and correctly limited.
- 70/90/100 percent notifications do not duplicate.
- Dashboard projections reconcile with authoritative records and show freshness.
- A fresh tenant reaches a first processed order through real components.

