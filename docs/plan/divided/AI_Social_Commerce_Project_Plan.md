# AI Social Commerce Platform — Product and Architecture Plan

## 1. Product definition

Build a Nepal-focused, multi-tenant social-commerce operating system for sellers who receive customers through Facebook, Instagram, WhatsApp, and later TikTok. The platform turns social enquiries into reliable orders through a catalog-aware AI assistant, a unified seller inbox, a branded online storefront, local-first payment options, inventory control, and order operations.

The product is not merely a chatbot and it is not a marketplace. Every seller gets an isolated workspace and a public branded store while the platform provides the shared infrastructure.

### Core principle

**AI is not the source of truth.** Products, prices, inventory, delivery fees, payment status, and orders are application/database facts. AI can only read those facts through controlled tools or execute explicitly authorized, validated actions.

## 2. Users and jobs

| User | Main job | Success outcome |
|---|---|---|
| Seller owner | Launch and operate a professional social shop without building a website. | Configures store, catalog, delivery, payments, and channels; receives orders. |
| Seller operator | Reply to customers and fulfil orders efficiently. | Uses one inbox, takes over AI when required, processes payments and fulfilment. |
| Customer | Ask in a familiar channel and purchase with confidence. | Receives correct answers and completes a transparent checkout. |
| Platform operator | Support tenants and integrations safely. | Can inspect audit events, retry failed provider events, and never access data by default. |

The customer experience must support Nepali (Devanagari), English, and Romanized Nepali. Common intents include price, availability, size/color, delivery, discount, COD, QR payment, and order status.

## 3. MVP product scope

The MVP proves this full loop:

```text
Create workspace → complete onboarding → publish catalog/storefront
→ connect one verified social channel → receive customer enquiry
→ AI answers from live data or hands off to staff
→ customer places storefront or assisted order
→ COD/QR payment is recorded → seller fulfils order
```

### Included in MVP

- Tenant account, seller workspace, memberships, RBAC, audit events, and onboarding checklist.
- Catalog: products, variants, images, prices, publish state, stock movement history.
- Inventory: concurrency-safe reservations, expiry/release, allocation, and no overselling.
- Public mobile storefront on a platform subdomain, product pages, cart, address, checkout, and order confirmation.
- Seller-defined delivery zones/rules and fee calculation.
- COD and merchant-provided QR/manual payment verification.
- Canonical order records with source, immutable totals, payment state, and fulfilment state.
- One production-validated channel integration, unified inbox, staff reply, human takeover, and provider event replay.
- Constrained AI assistant: catalog search, inventory, price, delivery, order draft/reservation, checkout link, and escalation.
- Seller dashboard: setup status, inbox, catalog, orders, low stock, basic sales/order/chat metrics.
- Entitlement and usage foundations with manual plan assignment and quota visibility.
- Security, backups, observability, tests, deployment controls, and pilot operational runbooks.

### Explicitly deferred

- TikTok chatbot/DM automation until provider capabilities are verified.
- Live eSewa/Khalti gateway processing until KYC, settlement, callbacks, refunds, and legal ownership are validated.
- Custom domains and self-service TLS; start with platform subdomains.
- Subscription collection, carrier APIs, returns portal, coupons/loyalty, marketplace/POS, image/OCR product matching, autonomous campaigns, multi-region HA, and formal SOC 2 claims.

## 4. Storefront and multi-store tenancy

### Tenant hierarchy

```text
Platform
└── Tenant / Seller company
    ├── Users and memberships
    ├── Catalog and inventory
    ├── Store A (MVP: one active store)
    │   ├── {store}.yourdomain.com
    │   ├── store branding and policies
    │   ├── product publication scope
    │   ├── delivery/payment configuration
    │   └── orders and storefront analytics
    └── Store B/C (Phase 2 paid capability)
```

The data model must have a first-class `Store` entity from day one. MVP permits one active store per tenant; Phase 2 can allow multiple stores for one seller. Catalog and inventory are tenant-owned initially, while each store decides which products to publish. This prevents duplicate catalog records and preserves a single stock truth.

### Storefront UX

- Platform URL: `{store-slug}.yourdomain.com`; dashboard URL: `app.yourdomain.com`.
- Store profile: name, logo, colors, social/contact links, policies, delivery coverage, and status.
- Product pages: images, price, variants, stock availability, shareable URLs, and mobile-first layout.
- Cart/checkout: address/contact, delivery fee, payment method, server-side quote, order confirmation.
- Customer totals must always be recalculated by the API. Browser totals, inventory, prices, and tenant identity are never trusted.

### Theme system

MVP uses safe controlled themes, not arbitrary seller code or Shopify-style template editing:

- Logo, store name, colors, banner, approved font pair.
- Selectable card/layout style and homepage section ordering.
- Contact, social links, policy pages, delivery and payment content.

Full custom themes are later work because arbitrary HTML/CSS/JS creates security, support, performance, and upgrade problems.

## 5. Architecture decisions

### Selected stack

| Concern | Use | Why |
|---|---|---|
| Frontend | Next.js + TypeScript | Seller dashboard, public storefront, mobile UX, host-based routing, SEO. |
| Backend | ASP.NET Core .NET 10 modular monolith | Strong transactional domain model and predictable deployment. |
| Data | PostgreSQL + EF Core | Relational source of truth for tenants, inventory, orders, and payments. |
| Identity | ASP.NET Core Identity + Membership/RBAC | Controlled user/tenant model; OIDC-compatible later. |
| Async work | Hangfire with PostgreSQL storage in MVP | Webhook processing, retries, reservation expiry, notifications; no Redis requirement at launch. |
| Cache | Add Redis only when scaling/multi-instance caching requires it | Avoids premature infrastructure cost. |
| Files | S3-compatible storage; recommended Cloudflare R2 | Product images, knowledge documents, and encrypted backup copies. |
| AI | Provider-agnostic application interface | Start with a provider selected by quality/cost/privacy tests; never couple domain rules to SDK. |
| Local orchestration | .NET Aspire + Docker | Starts and observes local API, web, Postgres, jobs, and optional Redis. |
| Production packaging | Docker containers | Portability across VPS, EC2, Container Apps, Railway, or Kubernetes. |

Do **not** use ABP for the MVP. It supplies useful SaaS primitives but adds a large framework, UI assumptions, and conventions while not solving the difficult product work: storefronts, inventory correctness, provider webhooks, payments, and AI safety. Use focused ASP.NET Core libraries and product-specific modules instead.

### Aspire’s role

Aspire is not the business framework and is not a cloud provider. It is the local development/application-topology layer:

```text
Aspire AppHost
├── PostgreSQL
├── API
├── Hangfire worker (initially co-hosted with API)
├── Seller/public Next.js app(s)
└── optional Redis
```

The same Dockerized API and web applications can later deploy to a VPS, AWS EC2, Railway, Render, Azure Container Apps, ECS, or Kubernetes. GitHub Pages cannot host this product because it cannot run the ASP.NET API, workers, webhooks, database, or dynamic tenant storefronts.

## 6. Modular-monolith boundaries

```text
Domain → Application → Infrastructure → WebApi
```

| Module | Owns |
|---|---|
| Tenancy & Identity | Tenant, User, Membership, Role, actor audit context. |
| Storefront | Store profile/readiness, domains, cart/checkout session, public catalog projection. |
| Catalog | Product, variants, media references, categories, publication and canonical price facts. |
| Inventory | Stock ledger, balance, reservations, allocation, adjustments. |
| Customers | Channel identities, customer profile, addresses, consent/retention. |
| Conversations | Conversations, messages, assignment, labels, bot/human ownership. |
| Integrations | Channel connections, encrypted tokens, webhooks, provider health, delivery attempts. |
| AI Assistant | Assistant policy, knowledge, tool calls, traces, evaluations, escalation. |
| Orders | Orders, items, total snapshots, cancellation and fulfilment. |
| Payments | Methods, attempts, QR proof, transactions, verification, refunds/settlement later. |
| Billing | Plans, entitlements, usage events, quota decisions. |
| Notifications | Receipts, notifications, delivery audit. |
| Reporting | Event-based dashboard projections and reports. |

### Required reliability rules

- Every tenant-owned table, cache key, storage path, event, and job payload has `TenantId`.
- Resolve tenants from authenticated membership, verified webhook connection, persisted job context, or trusted host mapping—never a request header alone.
- EF query filters are defense in depth, not the only tenant boundary.
- Provider event and message IDs have unique constraints to stop duplicates.
- Inbound webhook flow is: validate → durably store event → return quickly → process asynchronously → retry/replay safely.
- Orders, inventory, price, delivery, taxes, and fees are server-calculated and immutable once confirmed.

## 7. State machines and commerce rules

```text
Order: draft → awaiting_customer → pending_confirmation → confirmed
       → processing → fulfilled
       ↘ cancelled

Payment: not_required | pending | awaiting_verification | authorized
         | paid | failed | refunded | partially_refunded

Fulfilment: unfulfilled | ready | dispatched | delivered | failed | cancelled

Reservation: active → committed | released | expired

Conversation: new → bot_active ↔ human_assigned → awaiting_customer
              → checkout_in_progress → order_created → resolved
```

- Payment and fulfilment are independent states.
- COD is payment pending until recorded as collected.
- QR/manual transfer is `awaiting_verification` until an authorized seller action.
- A gateway payment later requires signed callback + matching payment attempt + idempotency.
- Human takeover pauses automation immediately; only an authorized explicit release resumes it.

## 8. AI and knowledge requirements

### Read tools

`SearchProducts`, `CheckInventory`, `GetPrice`, `GetShippingInfo`, `GetOrderStatus`.

### Controlled write tools

`QuoteCart`, `CreateOrderDraft`, `ReserveInventory`, `ReleaseReservation`, `CreateCheckoutLink`, `EscalateToHuman`.

Every write tool requires schema validation, tenant/customer context, authorization, idempotency, and an audit trail. RAG is only for approved seller FAQ/policy/brand content. RAG must never answer live stock, price, payment, delivery calculation, or order status without an application tool.

Before automation launches, build an evaluation set for Nepali, English, and Romanized Nepali: availability, variants, delivery/COD, price, unsafe instructions, unavailable products, payment disputes, complaints, and forced escalation.

## 9. Security and operational requirements

- HTTPS, encrypted secrets/tokens, secret rotation, and webhook-signature verification.
- Role/policy authorization, public API rate limits, input/file-size validation, and CSRF-safe browser flows.
- PII minimization, redacted logs/AI traces, retention/export/deletion policy, and audited support access.
- Daily database backups, restore test, health checks, structured logs, traces, metrics, alerts, and incident runbooks.
- Do not claim 99.99% availability, bank-grade security, or SOC 2 until documented controls and operations prove it.

## 10. Phase 2 and Phase 3

**Phase 2:** second/third verified social channels, eSewa/Khalti adapters after operational validation, custom domains, reports, data exports, payment/fee ledger, richer knowledge lifecycle, and multi-store plans.

**Phase 3:** carrier integration, returns, advanced fulfilment, provider-aware AI routing, customer accounts, controlled promotions, and enterprise controls.

## 11. Master reference

This file is the product/architecture division. The full cross-reference, detailed acceptance criteria, and execution roadmap live in [`../plan.md`](../plan.md).
