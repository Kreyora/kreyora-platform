# AI App Building Prompts — Step-by-Step Execution Plan

## How to use these prompts

This is the authoritative implementation prompt pack for the planned platform. Use one prompt at a time, only after its prerequisites and Definition of Done are satisfied. Preserve existing work, use the architecture decisions in the companion plans, add tests with every feature, and report unmet external dependencies rather than inventing provider/payment behavior.

## Build order at a glance

```text
0 Decisions/provider readiness
1 Repository + local Aspire foundation
2 Tenancy, Identity, RBAC
3 Catalog + inventory correctness
4 Storefront + checkout + COD/QR
5 Order operations + notifications
6 One channel + unified inbox
7 Constrained AI + RAG + takeover
8 Dashboard, quotas, hardening, pilot
```

## Prompt 0 — Finalize build decisions

> Before generating application code, create a short architecture decision record for Kreyora. Lock: initial social provider (only after API/approval validation), initial AI provider after quality/cost/privacy tests, first VPS region/size, object storage account, primary domain, COD/merchant-QR operating process, and pilot seller profile. Confirm .NET 10, Next.js TypeScript, PostgreSQL/EF Core, ASP.NET Core Identity, Docker, .NET Aspire local orchestration, Hangfire/PostgreSQL job storage, Cloudflare DNS/R2, and GitHub Actions/GHCR. Record every unresolved external dependency as a launch gate. Do not create unverified integrations or production credentials.

**Done when:** choices, owners, provider evidence, cost estimate, and launch gates are written; no coding work depends on an unrecorded assumption.

## Prompt 1 — Repository and local engineering foundation

> Implement the foundation for Kreyora as a monorepo. Create a .NET 10 solution with Domain, Application, Infrastructure, WebApi, Worker (optional host), ServiceDefaults, AppHost, and test projects. Create Next.js TypeScript seller/public app surfaces under `web/`. Use .NET Aspire AppHost to orchestrate API, PostgreSQL, optional Redis, worker, and web apps locally. Add Dockerfiles, Docker Compose production templates, typed configuration validation, structured logging/correlation IDs, OpenAPI, RFC 7807 problem responses, health/readiness endpoints, formatting/linting, CI skeleton, and local environment templates. Do not add business modules, live providers, or secrets.

**Done when:** a clean checkout builds, lints, tests, runs locally through Aspire, and exposes health checks with no committed secrets.

## Prompt 2 — Multi-tenancy, Identity, RBAC, and audit foundation

> Implement Tenant, User, Membership, roles (`Owner`, `Admin`, `Operator`, `Viewer`, audited `PlatformSupport`), policy-based authorization, authenticated tenant context, and audit-event primitives using ASP.NET Core Identity and EF Core. Enforce `TenantId` on every tenant-owned entity, query, cache key, job payload, storage path, and provider event. Use EF query filters only as defense in depth. Build protected Next.js seller-workspace authentication/session shell. Add migrations and tests proving API, database, raw query/projection, and background job paths cannot access another tenant’s data. Never trust an arbitrary tenant header.

**Done when:** an owner creates a tenant; membership roles work; unauthorized tenant access fails; audit records identify actor, tenant, action, time and correlation ID.

## Prompt 3 — Catalog, media, and inventory correctness

> Implement Catalog and Inventory modules. Add Product, ProductVariant, product-media references, publication state, canonical price facts, append-only StockMovement, Inventory balance, and expiring InventoryReservation. Build seller APIs/UI for products, variants, stock adjustments, media authorization, publish/unpublish, reserve/release/commit inventory, and low-stock view. Use PostgreSQL transactions/concurrency control so concurrent reservations cannot oversell. Require tenant scope, idempotency, and audit events for all writes. Do not build image/OCR product matching yet.

**Done when:** published variants are the only purchasable variants; stock ledger reconciles; duplicate requests do not duplicate moves; expiry safely releases stock; high-contention integration tests pass.

## Prompt 4 — Storefront, checkout, delivery, COD, and merchant QR

> Implement the Storefront, Delivery, Orders, and MVP Payments modules. Add `Store` with platform-subdomain/slug routing, controlled theme settings, readiness checks, published-catalog projection, cart, checkout quote, customer contact/address, DeliveryRule, COD configuration, merchant QR/manual-payment configuration, canonical Order/OrderItem financial snapshots, and short checkout reservations. Keep OrderStatus, PaymentStatus, and FulfilmentStatus separate. Recalculate price, availability, delivery, tax/fees, and totals server-side. Browser input cannot set price, stock, payment status, or tenant identity. Build mobile Next.js product/cart/checkout/confirmation pages and seller setup UI.

**Done when:** a fresh tenant can configure and publish a storefront, complete COD/QR checkout, preserve immutable totals, and safely release expired/failed checkout reservations.

## Prompt 5 — Seller order operations and notifications

> Implement seller order list/detail workflows; allowed confirmation, cancellation, payment verification, COD collection, and fulfilment transitions; QR proof/verification audit; receipt/confirmation notification outbox; and notification provider abstraction with a safe development implementation. Payment cannot be marked paid without an authorized manual verification or later signed provider callback. Cancellation/reservation/allocation behavior must be transactional. Add role checks, state-transition tests, APIs and end-to-end seller fulfilment coverage.

**Done when:** a seller can safely process, cancel, verify, and fulfil an order with actor/reason/time audit records and notification delivery status.

## Prompt 6 — Provider runtime, one validated channel, and inbox

> Implement Integration and Conversation modules with encrypted ChannelConnection secrets, capability flags, immutable WebhookEvent, normalized inbound events, Conversation/Message, outbound-message outbox, delivery attempts, bounded retries, DLQ/replay and connection-health diagnostics. Define `IChannelProvider` with webhook validation, inbound normalization, outbound messaging, capability discovery, and connection validation/refresh. Implement exactly one provider adapter only after its documented sandbox and production requirements are supplied. Webhooks validate signatures, persist idempotently, acknowledge quickly, then process asynchronously. Build inbox list/detail, staff reply, assignment, and human takeover UI.

**Done when:** duplicate provider events/messages cannot duplicate records; staff can reply; a provider failure is visible/replayable; takeover immediately pauses automation; provider and tenant isolation tests pass.

## Prompt 7 — Constrained AI assistant and knowledge base

> Implement tenant assistant policy, approved KnowledgeDocument lifecycle, retrieval interface, AI provider abstraction, safe tool registry, and redacted AIActionLog. Implement read tools for product, inventory, price, delivery, and order status. Implement controlled write tools for quote, order draft, reserve/release, checkout link, and escalation with schema validation, tenant/customer context, authorization and idempotency. Use RAG only for approved seller FAQ/policy/brand material; catalog, prices, inventory, payment and order facts always come from application tools. Enforce tool-loop, latency, cost and entitlement budgets. Build offline evaluation cases for Nepali, English, Romanized Nepali, ambiguity, unavailable stock, prompt injection, complaints and escalation.

**Done when:** every AI commerce claim is grounded in an application query/tool trace; no bot reply is sent after takeover; cross-tenant retrieval is impossible; evaluation and audit tests pass.

## Prompt 8 — Onboarding, dashboard, quotas, deployment hardening, and pilot

> Implement the launch gate. Add a resumable seller onboarding/readiness checklist that gates storefront/AI activation. Add versioned Plan, Entitlement, Subscription, and UsageEvent models with manual plan assignment, feature checks, quota usage, and 70/90/100% notifications; do not implement subscription collection. Build dashboard projections for setup progress, orders/revenue, open chats, reply time, low stock, integration health, and usage. Add metrics/tracing for webhook, worker, AI, checkout, and provider performance. Add Docker Compose deployment artifacts, GitHub Actions CI/development/production workflows, GHCR image publishing, migration controls, backup/restore test, health alerts, runbooks, feature kill switches, and staging/production smoke scripts.

**Done when:** a clean tenant completes onboarding to first order; quotas are deterministic; a deployment uses immutable images; backup restore and rollback are demonstrated; pilot launch checklist passes.

## Mandatory quality gates

| Area | Required evidence |
|---|---|
| Tenant safety | API, job, storage, cache, and host-routing isolation tests. |
| Inventory | Concurrency/reservation/expiry/reconciliation tests. |
| Commerce | Immutable order totals, correct payment/fulfilment transitions, browser tampering tests. |
| Webhooks | Signature, idempotency, retry/DLQ/replay tests. |
| AI | Tool authorization/grounding, language, prompt-injection, and handover evaluation tests. |
| Operations | Staging deploy, migration rehearsal, backup restore, alert, and rollback evidence. |

## Release sequence

1. Foundation
2. Deterministic storefront and orders (MVP Alpha)
3. One social channel plus safe AI (MVP Beta)
4. Pilot hardening and launch
5. Phase 2: extra channels, live payment gateways, custom domains, exports/reports, multi-store capability

## Master reference

This file is the implementation division. The product and deployment decisions are in the companion Markdown files and the detailed source of truth remains [`../plan.md`](../plan.md).
