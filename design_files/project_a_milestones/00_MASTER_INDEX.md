# Kreyora — Milestone Implementation Pack

## Purpose

This pack converts the completed Kreyora architecture into small, reviewable implementation stages. It deliberately starts with a complete, mock-backed frontend showcase so the product can be demonstrated and corrected before backend integration begins.

The frontend milestone changes delivery order, not the approved product architecture. Later milestones replace mock adapters with real APIs without redesigning the user interface.

## Source hierarchy

When instructions conflict, use this order:

1. An approved Architecture Decision Record (ADR) created during implementation.
2. `plan.md`, especially Sections 10–11.
3. The current milestone file in this pack for sequencing and acceptance evidence.
4. `FRONTEND_DESIGN_DIRECTION.md` for approved frontend visual and motion decisions.
5. `AI_Social_Commerce_Project_Plan.md`.
6. `Deployment_Strategy_and_Infrastructure_Plan.md`.
7. `AI_App_Building_Prompts_Step_By_Step.md`.
8. `Kreyora.docx` as the original blueprint.

Never silently resolve a conflict. Record the conflict, recommendation, decision owner, and effect in an ADR.

## Architecture invariants

- Product: Nepal-focused, multi-tenant social-commerce operating system.
- Frontend: Next.js with TypeScript, mobile-first seller workspace and public storefront.
- Frontend design: dominant white canvas, bold near-black editorial typography, deliberate whitespace/grid, minimal neutral components, product-led color, and restrained accessible motion as defined in `FRONTEND_DESIGN_DIRECTION.md`.
- Backend: ASP.NET Core .NET 10 modular monolith.
- Data: PostgreSQL with EF Core and mandatory tenant scoping.
- Identity: ASP.NET Core Identity, tenant memberships, policy-based RBAC.
- Jobs: Hangfire with PostgreSQL storage for MVP. Redis is optional, not an MVP dependency.
- Files: S3-compatible private object storage, initially Cloudflare R2 unless ADR changes it.
- Deployment: Docker, .NET Aspire locally, GitHub Actions/GHCR, controlled migrations.
- AI: provider-neutral orchestration; AI is never the source of truth for commerce facts.
- MVP payments: COD and merchant QR/manual verification. Live gateways remain gated.
- MVP social scope: exactly one production-validated channel adapter.
- Tenancy, idempotency, audit, authorization, observability, and tests are features, not cleanup work.

## Milestone sequence

| # | File | Outcome | Release checkpoint |
|---:|---|---|---|
| 01 | `01_FRONTEND_SHOWCASE.md` | Complete product showcase using typed mock data and simulated workflows. | Stakeholders approve product UX and route map. |
| 02 | `02_ENGINEERING_FOUNDATION.md` | Monorepo, .NET foundation, Aspire, Postgres, contracts, CI, and local runtime. | Clean checkout builds, tests, and runs. |
| 03 | `03_TENANCY_IDENTITY_RBAC.md` | Real identity, tenant context, memberships, authorization, and audit. | Cross-tenant isolation evidence passes. |
| 04 | `04_CATALOG_INVENTORY_MEDIA.md` | Real catalog, media, stock ledger, and safe reservations. | Frontend catalog mocks are removed. |
| 05 | `05_STOREFRONT_CHECKOUT_ORDERS.md` | Store publishing, delivery quotes, cart, checkout, and canonical orders. | A real COD/QR test order succeeds. |
| 06 | `06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md` | Seller order workflow, manual payment verification, fulfilment, and notifications. | Order lifecycle is demonstrated end to end. |
| 07 | `07_SOCIAL_INTEGRATION_RUNTIME.md` | Provider-neutral webhook, event, outbox, retry, replay, and diagnostics runtime. | Provider simulator proves reliability. |
| 08 | `08_FIRST_SOCIAL_CHANNEL_AND_INBOX.md` | One validated provider plus customer identity and unified inbox. | Real sandbox message and staff reply succeed. |
| 09 | `09_AI_ASSISTANT_RAG_TOOLS.md` | Safe AI orchestration, approved knowledge, DB-backed tools, budgets, and takeover. | AI evaluation and handoff gates pass. |
| 10 | `10_ONBOARDING_DASHBOARD_ENTITLEMENTS.md` | Activation readiness, dashboards, plan entitlements, usage, and quotas. | Fresh tenant reaches first order. |
| 11 | `11_SECURITY_DEPLOYMENT_OPERATIONS.md` | Production security, CI/CD, backups, alerts, rollback, and runbooks. | Staging rehearsal and restore pass. |
| 12 | `12_PILOT_LAUNCH.md` | Controlled launch with 3–10 sellers and a measured learning loop. | Pilot review decides Phase 2. |
| 13 | `13_PHASE_2_EXPANSION.md` | Gated additional channels, payment gateways, domains, and advanced features. | Each expansion has independent evidence. |

Milestones 01–12 are ordered dependencies. Milestone 13 is a menu of post-pilot tracks, not one large release.

## How to execute the pack

1. Open the current milestone file.
2. Run only its first incomplete prompt.
3. Require the coding agent to inspect existing work before editing.
4. Review the resulting application and checkpoint report.
5. Record `APPROVED`, `CHANGES REQUESTED`, or `BLOCKED` in the milestone status table.
6. Continue only after the current step is approved.
7. Approve the milestone exit gate before starting the next file.

Do not send an entire milestone to a coding agent as one prompt. The prompt boundaries are designed to expose architectural mistakes early.

## Universal execution preamble

Prepend this to every implementation prompt:

> Work only on the named Kreyora milestone step. First inspect the repository, current milestone file, prior checkpoint reports, relevant ADRs, and existing tests. Preserve approved work and unrelated user changes. State assumptions before implementation. Do not invent provider behavior, production credentials, legal claims, or payment confirmation. Use existing architecture and naming conventions. Add or update tests with the implementation. Run the narrowest relevant checks, then the milestone-required checks. Stop after this step and create a checkpoint report using `CHECKPOINT_TEMPLATE.md`; do not begin the next prompt.

## Required output from every prompt

Every step must finish with:

- Implementation summary and exact scope completed.
- Files added, changed, or removed.
- Architecture or data-flow explanation.
- Commands run and results.
- Tests added and their results.
- Screenshots for visible UI changes.
- Migrations/API contract changes, if any.
- Security, tenancy, data, and operational considerations.
- Assumptions, unresolved dependencies, and risks.
- Manual verification instructions.
- Explicit statement that the next prompt was not started.

Store reports as `artifacts/checkpoints/M<NN>-S<NN>.md` or another consistently versioned location approved for the repository.

## Status vocabulary

| Status | Meaning |
|---|---|
| `NOT STARTED` | No implementation work accepted. |
| `IN PROGRESS` | Current prompt is being implemented. |
| `REVIEW` | Work is complete and waiting for verification. |
| `APPROVED` | Evidence satisfies the prompt and may be built upon. |
| `CHANGES REQUESTED` | Corrective work is required in the same step. |
| `BLOCKED` | An external dependency or decision prevents completion. |

## Global quality gates

The following gates apply throughout the program:

| Area | Required evidence |
|---|---|
| Product UX | Mobile and desktop routes work; loading, empty, error, denied, stale, conflict, and success states exist. The approved minimal visual system and motion/reduced-motion rules are demonstrated. |
| Tenant safety | API, job, host, cache, storage, search, and AI paths cannot cross tenants. |
| Inventory | Ledger, reservation, expiry, release, and concurrency tests reconcile. |
| Commerce | The server owns price, availability, delivery, fees, payment status, and order totals. |
| Integrations | Signatures, idempotency, quick acknowledgement, retry, DLQ, replay, and health are demonstrated. |
| AI | Claims are grounded in application tools or approved knowledge; takeover immediately stops automation. |
| Security | Secrets and PII are protected and redacted; privileged operations are authorized and audited. |
| Operations | Immutable release, controlled migration, alerts, backup restore, smoke tests, and rollback are proven. |

## Change-control rule

Any change to a locked technology, state machine, tenant boundary, public API convention, provider, payment policy, or deployment topology requires an ADR. An ADR must include context, decision, alternatives, consequences, migration effect, owner, and approval date.

## Completion definition

Kreyora MVP implementation is complete only when Milestones 01–12 have approved exit gates, no critical launch gate is unresolved, and a pilot seller can complete this path:

`signup → workspace → catalog → storefront publication → social enquiry → safe AI or human reply → stock-safe order → COD/QR processing → fulfilment → dashboard and audit evidence`.
