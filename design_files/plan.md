# Kreyora — Master Execution Plan
## Build a Saney-class AI Social Commerce Platform for Nepal

**Status:** Complete — Steps 1–5 executed; this file is the master product, architecture, and implementation plan (2026-07-11)  
**Goal:** Analyze existing plans + reverse-engineer [saney.io](https://saney.io), align gaps, then produce a complete build blueprint and implementation sequence.  
**Constraint:** Work is split across messages so each step can use maximum output tokens.

---

## 0. How to use this plan (multi-message workflow)

Reply with one of these commands per turn:

| Command | What I will produce |
|---|---|
| `Execute Step 1` | Full Saney product extraction dossier |
| `Execute Step 2` | Full analysis of our existing plan docs |
| `Execute Step 3` | Alignment matrix: our plan vs Saney (matches / gaps / risks) |
| `Execute Step 4` | Final master product + architecture plan (detailed `plan.md` expansion) |
| `Execute Step 5` | Implementation roadmap, MVP scope, phase prompts, and build order |
| `Execute All Remaining` | Continue from next incomplete step |

Each step will **append or rewrite sections** of this file so the final `plan.md` becomes the single source of truth.

---

## 1. Sources already collected

### 1.1 Local plan documents (workspace)

| File | Role | Extracted to |
|---|---|---|
| `Kreyora.docx` | Master technical blueprint (architecture, entities, prompts 1–10) | `_extracted/Project_A.txt` |
| `Divided Plans/AI_Social_Commerce_Project_Plan.docx` | Vision, features, AI principles, roadmap phases | `_extracted/AI_Social_Commerce_Project_Plan.txt` |
| `Divided Plans/Deployment_Strategy_and_Infrastructure_Plan.docx` | Hosting, sizing, scaling, ops | `_extracted/Deployment_Strategy.txt` |
| `Divided Plans/AI_App_Building_Prompts_Step_By_Step.docx` | 9 step-by-step coding prompts | `_extracted/AI_App_Building_Prompts.txt` |

Note: There is no `ProjectA.md` yet — content lives in `.docx` and was extracted for analysis. Large size of `Kreyora.docx` (~3MB) is mostly formatting/media; text length is ~12k characters.

### 1.2 Saney public product surface (researched)

Public pages reviewed:
- `https://saney.io/` (marketing, features, pricing, how-it-works, tech claims, FAQ)
- `https://saney.io/about`
- `https://saney.io/contact`
- `https://saney.io/help`
- `https://saney.io/privacy`
- `https://saney.io/terms`
- `https://saney.io/signin` / `https://saney.io/signup` (auth UX)
- `https://saney.io/workspaces/*` (auth-gated; integrations require login)

---

## 2. Preliminary findings (short; expanded in Steps 1–3)

### 2.1 What Saney is (product definition)

Saney is a **Nepal-focused social commerce OS** for Meta/TikTok sellers that converts DMs into orders via:

1. **AI chatbots** across Facebook, Instagram, WhatsApp, TikTok  
2. **Built-in online storefront** (products, cart, checkout, custom domain)  
3. **Unified inbox** for multi-channel conversations  
4. **Inventory + order management**  
5. **Local payments** (eSewa, Khalti, QR, COD; Stripe mentioned)  
6. **Analytics/dashboards**  
7. **Subscription SaaS** with free + paid tiers and platform transaction fees  

**Observed stack signals from public claims/privacy:**
- Frontend: **Next.js**
- Database: **Postgres**
- Auth: **Clerk** (Google + email/password)
- AI: **Google Gemini API**
- Payments: eSewa, Khalti, COD, QR, Stripe (claimed)
- Object storage for knowledge files (PDF/CSV uploads)
- Workspace model (`/workspaces/dashboard`, integrations)

**Business model signals:**
- Free forever plan + paid Grow / Scale / Elevate (NPR 2,499 / 3,499 / 5,499 mo)
- Limits on products, AI credits, orders/month, social integrations
- Platform service charge (2.75%–4%) + third-party payment fees (~2%)
- Target market: Instagram/Facebook/TikTok sellers, home-based sellers, creators in Nepal
- Location/legal: Sankhamul, Nepal; governed by Nepal law

### 2.2 What our current plan is (engineering definition)

Our docs define a **.NET 10 modular monolith** social commerce OS with:

- Clean Architecture + DDD + CQRS (MediatR)
- PostgreSQL + EF Core multi-tenancy
- Redis + Hangfire
- Meta Graph channel providers (WhatsApp, IG, FB) first; TikTok less explicit
- AI via tool-calling + RAG (OpenAI/Gemini)
- Core principle: **AI is not source of truth** — inventory/prices/orders live in DB
- Frontend: Next.js or Blazor (not locked)
- Low-cost MVP infra → scale later

### 2.3 Early alignment verdict (high level)

| Area | Alignment | Notes |
|---|---|---|
| Core vision (DM → order OS) | **Strong** | Same product category as Saney |
| Multi-tenant sellers | **Strong** | Explicit in our plan |
| Unified inbox + AI assistant | **Strong** | Core of both |
| Catalog / inventory / orders | **Strong** | Our plan is more rigorous on concurrency |
| Storefront / public shop | **Gap** | Saney heavily markets storefront; our plan is thin here |
| Local payments (eSewa/Khalti/COD/QR) | **Gap** | Critical for Nepal; missing as first-class module |
| Pricing / SaaS billing / quotas | **Gap** | Saney is SaaS-metered; our plan has no plan/quota system |
| TikTok channel | **Partial** | Saney includes it; our plan prioritizes Meta first |
| Custom domain / branded store | **Gap** | Saney onboarding emphasizes domain + SSL |
| Auth productization | **Partial** | We plan Identity/RBAC; Saney uses Clerk |
| Analytics | **Partial** | Mentioned; not designed deeply |
| Deployment cost discipline | **Strong** | Matches Saney-like startup constraints |
| Architecture rigor | **Our plan is stronger** | Tool-calling, locking, idempotency are excellent |

**Bottom line:** Our plan is a **strong backend/AI architecture** for a Saney-class product, but it is **not yet a complete Saney product plan**. Missing product surfaces (storefront, payments, SaaS packaging, onboarding) must be added before build.

---

## 3. Multi-message execution steps (detailed)

### STEP 1 — Saney deep product extraction dossier
**Objective:** Extract every product capability, UX flow, business rule, and technical signal from Saney that should inform our product.

**Deliverables in this step (appended to plan.md):**
1. Full feature inventory (P0/P1/P2)
2. User roles & journeys (seller onboarding, daily ops, customer order path)
3. Channel matrix (FB/IG/WA/TikTok)
4. Storefront requirements reverse-engineered
5. Payment methods & fee model implications
6. Pricing tier limits → system quota model
7. Dashboard/module map (`/workspaces/*`)
8. AI behavior expectations (Nepali + English FAQs, stock/price/COD/delivery)
9. Non-functional requirements inferred (latency, uptime, security claims)
10. Competitive must-have vs differentiator list

**Why separate message:** Max tokens for exhaustive product taxonomy.

---

### STEP 2 — Full audit of our existing plans
**Objective:** Normalize Divided Plans + Kreyora into one engineering inventory.

**Deliverables:**
1. Consolidated vision & principles
2. Architecture decision log (what is fixed vs optional)
3. Entity model completeness review
4. Module map (Domain/Application/Infrastructure/WebApi)
5. AI architecture inventory (tools, RAG, escalation)
6. Prompt matrix quality review (10 master prompts + 9 step prompts)
7. Deployment/ops inventory
8. What is over-specified / under-specified / conflicting
9. Recommended stack lock decisions (Frontend, Auth, AI provider, hosting)

---

### STEP 3 — Alignment matrix (our plan vs Saney)
**Objective:** Decide what to keep, add, change, or defer.

**Deliverables:**
1. Feature-by-feature match table
2. Critical product gaps for Nepal market
3. Architecture gaps vs product needs
4. Risks if we build only what is currently planned
5. “Saney parity MVP” definition
6. “Beyond Saney” differentiators we can win with
7. Explicit out-of-scope list for v1

---

### STEP 4 — Master product + technical plan (final blueprint)
**Objective:** Produce the complete build-ready plan.

**Deliverables:**
1. Product vision & positioning
2. Personas & jobs-to-be-done
3. Full module architecture (backend + frontend apps)
4. Domain model (entities, aggregates, state machines)
5. Multi-tenancy & RBAC model
6. Channel integration architecture
7. AI orchestration design (tools, RAG, confidence, human takeover)
8. Storefront architecture
9. Payments architecture (COD/QR/eSewa/Khalti + fee accounting)
10. SaaS billing/quota model
11. API surface outline
12. Data model outline (tables + key indexes)
13. Security/compliance requirements
14. Observability & reliability requirements
15. Environment & config strategy

---

### STEP 5 — Implementation roadmap & execution prompts
**Objective:** Convert blueprint into ordered build work.

**Deliverables:**
1. MVP vs Phase 2 vs Phase 3 scope cuts
2. Sprint-style build sequence (week-by-week or milestone-based)
3. Repo/monorepo structure recommendation
4. Definition of Done per milestone
5. Test strategy per phase
6. Deployment path (local → staging → production)
7. AI coding prompt packs (refined from existing prompts)
8. Acceptance criteria checklist mapped to Saney parity
9. Launch checklist (Meta app review, payment KYC, legal pages, support)

---

## 4. Working decisions to lock during Steps 2–4

These will be decided explicitly (not assumed forever):

1. **Frontend:** Next.js (recommended for Saney-like UX + Vercel) vs Blazor  
2. **Auth:** ASP.NET Identity only vs Clerk/Auth0 + API trust  
3. **AI provider:** Gemini-first (Saney-like) vs OpenAI-first vs multi-provider router  
4. **Channels for MVP:** WhatsApp-only first vs WhatsApp + Instagram  
5. **Storefront in MVP?** Yes (required for Saney parity) or deferred  
6. **Payments in MVP?** COD+QR first, then eSewa/Khalti  
7. **Billing engine in MVP?** Manual plans first vs full metering  
8. **Hosting:** Azure-first (.NET native) vs Railway/Fly/Render cost path  
9. **Product name / brand:** clone-class competitor vs original brand  
10. **Language UX:** Nepali + English first-class support  

---

## 5. Success criteria for this planning phase

Planning is complete when `plan.md` contains:

- [x] Exhaustive Saney capability map  
- [x] Normalized inventory of our existing plans  
- [x] Explicit alignment verdict with gap list  
- [x] Build-ready architecture for parity + differentiators  
- [x] Ordered implementation roadmap with prompts  
- [x] Clear MVP cut that can ship without overbuilding  

---

## 6. Immediate next action

**Planning complete.** Start implementation with Milestone 0 in Section 11.

Use the authoritative prompt pack in Section 11.7 one milestone at a time; do not skip its acceptance criteria or external validation gates.

---

## 7. Step 1 — Saney deep product extraction dossier

**Research boundary.** This public-surface extraction was completed on 2026-07-11. “Observed” items are stated or visibly represented on Saney’s public site. “Inferred” items are the minimum product capability needed to support that experience and must be validated before being adopted as build scope. Authenticated workspace routes were not used as evidence.

**Primary source:** [Saney public website](https://saney.io/) — product, onboarding, pricing, FAQ, technology and platform-preview copy.

### 7.1 Product thesis and users

Saney presents itself as a Nepal-focused social-commerce operating system for sellers who acquire demand through Facebook, Instagram, WhatsApp and TikTok conversations. Its product is explicitly broader than a chatbot: it combines a multi-channel inbox, catalog-aware AI, storefront, local payments, orders, inventory and analytics in a seller workspace.

The stated target audience is Instagram sellers, Facebook businesses, TikTok shops, home-based sellers, creators, resellers and small/growing online businesses. The core pain is the continual stream of repetitive questions—price, availability, size/colour, delivery, authenticity, discount and COD—arriving through DMs.

**Kreyora product implication:** the customer experience must support both assisted conversion in the discovery channel and a frictionless hand-off to branded checkout. A chat-only system is not competitive parity.

### 7.2 Capability inventory

| Priority | Capability | Observed evidence | Requirement carried forward |
|---|---|---|---|
| P0 | Seller workspace | Public preview shows Dashboard, Products, Orders, Integrations and Conversations. | Authenticated, tenant-isolated workspace with role-aware navigation. |
| P0 | Catalog and variants | Seller uploads photos, price and variants; bot is “synced” to catalog. | Product, variant, media, price, stock and publish state are authoritative domain data. |
| P0 | Inventory | Claims to sync stock across channels and avoid overselling. | Shared stock ledger/reservation rules for storefront, operator and bot-assisted orders. |
| P0 | Unified inbox | Facebook, Instagram, WhatsApp and TikTok in one inbox. | Normalized conversation/message model with channel identity, attachment and delivery state. |
| P0 | AI sales assistant | 24/7 catalog-aware replies; answers price, availability, delivery and COD; can reserve an order. | Tool-using agent; no free-text factual claims about stock, price, fees or order status. |
| P0 | Human takeover | FAQ emphasizes customers can still message a seller. | Per-conversation bot pause/takeover, assignment and complete audit history. |
| P0 | Public storefront | Mobile-friendly store with products, cart and checkout. | Tenant branded shop, cart, checkout and confirmation flow. |
| P0 | Local payments | COD, QR, eSewa, Khalti, bank transfer and Stripe are marketed. | COD + merchant QR first; gateway-provider adapter boundary from day one. |
| P0 | Delivery configuration | Onboarding includes delivery charges. | Persisted delivery methods/rules and order-level fee snapshot. |
| P0 | Orders | Preview distinguishes store orders and bot orders. | One Order aggregate with source attribution; never separate fulfillment truth by channel. |
| P0 | Onboarding | Store → delivery/payments → products/channels → AI → optimize. | Resumable setup checklist and activation-readiness rules. |
| P1 | Analytics and reports | Real-time sales, conversations and customer-behavior analytics; monthly reports in paid tiers. | Operational dashboard first; durable source/channel metrics and scheduled reports later. |
| P1 | Custom domain + SSL | Seller selects a domain; example shows verified/SSL active. | Subdomain first, then DNS verification, TLS lifecycle and tenant-safe routing. |
| P1 | Data export | Public technology copy says sellers can export data. | Tenant export jobs for commerce and communication data, with audit events. |
| P2 | TikTok automation | TikTok is marketed, but public detail is absent. | Preserve a channel provider abstraction; release only capabilities confirmed by API eligibility. |
| P2 | Enterprise controls | Marketing mentions larger teams/custom plans/security controls. | SSO, advanced roles and enterprise retention are later scope unless validated demand requires them. |

### 7.3 Core journeys

#### Seller owner

1. Creates a workspace and establishes store identity.
2. Sets delivery fee/coverage and enables COD, QR and later gateway payments.
3. Adds products, variants, media, price and stock; checks public storefront.
4. Connects social accounts through provider authorization and selects the accounts/pages to manage.
5. Configures bot facts: delivery policy, payments, business hours, returns, language/tone and escalation owner.
6. Activates AI only after required catalog/policy setup is complete.
7. Uses dashboard and inbox to handle escalations, create/confirm orders, fulfill orders and improve catalog/bot settings.
8. Observes usage and upgrades plan as tenant limits are approached.

#### Seller operator (inferred)

Works in inbox and orders without access to billing, domain or provider credentials. Minimum permissions are reply/take-over/release bot, create or update orders, update fulfilment status and limited product operations—all scoped to the tenant.

#### Customer

1. Discovers a product in a social channel or via storefront link.
2. Asks in Nepali, English or Romanized Nepali about an item.
3. Receives a grounded reply, chooses variant/quantity and supplies delivery details.
4. Pays via COD, QR/manual verification, or an online gateway and receives an order reference/receipt.
5. Continues communicating with the seller for fulfilment updates or support.

### 7.4 Channels, inbox and AI

| Channel | Parity capability | Important caveat |
|---|---|---|
| Facebook Messenger | Webhook ingest, outbound response, page connection, normalized conversation and delivery handling. | Permissions and rate limits dictate actual capability. |
| Instagram Direct | Same normalized model, but independently tested account/page linkage and policies. | Do not assume Messenger capability automatically applies. |
| WhatsApp | Business API sessions/templates, opt-in, provider delivery status and shared inbox. | A distinct commercial/policy integration track. |
| TikTok | Integration state and capability flags behind a provider adapter. | Marketing does not prove DM automation availability for this product model. |
| Storefront web | Public browsing, cart, checkout, receipt and live inventory enforcement. | A complete sales channel, not a static product profile. |

**Recommended conversation state:** `new → bot_active ↔ human_assigned → awaiting_customer → checkout_in_progress → order_created → resolved`, with `closed` and `spam` as dispositions. Transport events such as sent/delivered/read/failed remain separate from conversation ownership.

**Required AI guardrails:** identify ambiguity and ask clarifying questions; call catalog/stock/order/payment tools before commerce assertions; reserve stock only for a defined short TTL during committed ordering; disclose or safely transition automation where needed; escalate low-confidence queries, complaints, refunds, policy exceptions and payment disputes. All prompts, retrieved data and tool calls must be tenant scoped and auditable.

Saney’s public examples use familiar Romanized Nepali (“COD hunxa?”) and Kathmandu delivery. Kreyora should treat Nepali (Devanagari), English and Romanized Nepali as first-class product input, while retaining the seller’s configured tone.

### 7.5 Storefront and order requirements

The public onboarding and feature copy imply a mobile-first branded commerce site, with:

- Store profile: branding, contact/social links, policies, delivery coverage and status.
- Product pages: media, price, variants, availability, shareable URLs and publish state.
- Checkout: buyer contact/address, delivery method/fee, payment selection and full order summary.
- Customer order confirmation/reference, payment state and seller-support path.
- A domain lifecycle: platform subdomain → seller-supplied domain → DNS verification → certificate provisioned → active/failed/retry.

Price, delivery fee, discount, tax/VAT, payment fee and stock must be calculated by the same domain service for storefront, agent UI, bot and payment callbacks. Every order must persist immutable line-item and total snapshots; later catalog changes cannot rewrite commercial history.

### 7.6 Payments and commercial model

Saney markets both subscription billing and transaction-linked service charges. Published paid-plan figures (exclusive of VAT) are:

| Plan | Monthly price | Products | AI credits | Orders/month | Social integrations | Service charge |
|---|---:|---:|---:|---:|---:|---:|
| Free | NPR 0 | 25 | Not public | Not public | Not public | Not public |
| Grow | NPR 2,499 | 300 | 200 | 200 | 3 | 3.5% (2% third-party + 1.5% platform) |
| Scale | NPR 3,499 | 800 | 500 | 500 | All | 3.0% (2% + 1%) |
| Elevate | NPR 5,499 | 2,300 | 1,300 | 1,700 | All | 2.75% (2% + 0.75%) |

Model `PaymentMethod`, `PaymentAttempt`, `PaymentTransaction`, `Refund`, `Settlement` and immutable `FeeBreakdown` separately. “Bring your own QR” means a merchant QR/instructions can support a manual-payment flow: display QR, accept proof if applicable, and let the seller verify. It is not a gateway confirmation. COD begins as payment pending and becomes collected only through an explicit operational event. Gateway callbacks require signature verification, provider-reference reconciliation and idempotency.

### 7.7 Plans, quotas and entitlements

Packaging is part of the core product. Kreyora needs a plan/entitlement domain even if v1 subscription collection is manual. Candidate usage metrics: published products, connected accounts, AI credits, created orders per billing period, team seats, custom domains, reports and retention.

Record every usage change as an auditable `UsageEvent` with tenant, metric, quantity, source, idempotency key and timestamp. Warn at 70/90/100%; do not strand existing sellers/customers or disable historical access solely because an ordinary soft limit was exceeded. Version plan definitions so old invoices and order economics remain explainable.

### 7.8 Workspace information architecture

| Module | Seller job | First-release indicators/actions |
|---|---|---|
| Dashboard | See what requires attention and how business is performing. | Open chats, sales, reply time, new orders, low stock and setup progress. |
| Conversations | Sell/support across channels. | Filters, reply, takeover, create order, customer timeline. |
| Products | Maintain trustworthy catalog data. | Product/variant/media/price/stock edit and inventory adjustment history. |
| Orders | Fulfil and track payment. | Source, customer/address, totals, payment/fulfilment state and receipt. |
| Integrations | Connect channels/providers safely. | Connection/reauthorization status, last webhook, errors and capability flags. |
| Storefront | Control public presentation and commerce setup. | Preview, policies, delivery/payments and domain readiness. |
| AI assistant | Control sales behavior. | Languages, tone, trusted knowledge, escalation rules, tool permissions and test console. |
| Billing/settings | Manage tenant access. | Plan/usage, team/RBAC, invoices, audit/export and security settings. |

### 7.9 Reliability and security benchmark

Saney markets an average 2.4-second AI reply, 99.99% inbox uptime, encrypted secrets, scoped tokens, audit logs, SOC-2 controls, typed APIs and data export. These are competitive signals, not promises Kreyora should make before it has the associated controls.

The MVP baseline is: quick webhook acknowledgment plus durable asynchronous processing; idempotent inbound-event storage; outbound retry/dead-letter/replay; provider-safe idempotency; per-tenant rate limits; correlation IDs; structured audit logs; encrypted secret storage; token expiry/scope tracking; RBAC; tenant-isolation tests; backup/restore drills; and health/metrics/alerting. Step 4 must choose measurable response, availability, RPO/RTO and retention targets before any marketing SLA is written.

### 7.10 Parity threshold, differentiation and validation queue

**Parity essentials:** low-friction setup; multi-channel inbox; catalog-aware AI with human takeover; stock-safe orders; mobile storefront; COD/QR plus local-payment path; delivery configuration; seller dashboard; Nepal language UX; plan limits/usage; tenant-safe and observable provider plumbing.

**High-value differentiation:** deterministic/auditable AI tools; reservation/concurrency correctness; transparent confidence/escalation; unified order/payment ledger; seller-controlled knowledge and bot test tools; integration diagnostics/replay; data portability; and shipping only channel capabilities that provider APIs genuinely allow.

**Validate before commitment:** exact free/trial/overage/cancellation rules; definition of an “AI conversation credit”; actual TikTok capabilities; payment settlement ownership/refunds/VAT; carrier integrations; public-store customer account/coupon/returns/tracking scope; and whether public performance/security claims are contractual. These should be treated as discovery questions, not assumed requirements.

### 7.11 Step 1 decisions carried forward

1. Build a social-commerce OS with a public storefront, not only an AI inbox.
2. Make COD, merchant QR and configurable delivery first-class MVP primitives; add eSewa/Khalti through adapters after operational/legal validation.
3. Require onboarding readiness before storefront/AI activation.
4. Keep catalog, prices, inventory and orders deterministic; AI may only propose or execute audited domain actions through tools.
5. Design plan, entitlement and usage foundations now even when paid billing is deferred.
6. Preserve provider abstractions and release channel features only after capability verification.

---

## 8. Step 2 — Audit of the existing plans

**Audit scope.** This section normalizes the four local planning sources, not a future design proposal:

| Source | Scope | Authority after audit |
|---|---|---|
| `Kreyora.docx` | Most detailed technical specification, data model, infrastructure matrix, and ten engineering prompts. | Primary technical baseline. |
| `AI_Social_Commerce_Project_Plan.docx` | Product vision, principles, feature list and five-phase development sequence. | Product intent baseline. |
| `Deployment_Strategy_and_Infrastructure_Plan.docx` | Low-cost deployment strategy, sizing and scale path. | Initial operational posture. |
| `AI_App_Building_Prompts_Step_By_Step.docx` | Nine concise coding prompts. | Delivery aid; must be superseded by the final prompt pack in Step 5. |

**Document-read note.** Text was reviewed from the existing `_extracted/*.txt` source extracts and the original DOCX files were retained unchanged. Render QA could not run in this environment because the document renderer's required `python` executable/runtime is unavailable. This affects only visual-layout review; it does not affect the content audit below.

### 8.1 Consolidated product vision and operating principles

The plans define a multi-tenant, AI-powered social-commerce platform for Instagram, Facebook and WhatsApp sellers. It owns the seller catalog, inventory, customer records, conversations, orders and a controlled AI sales assistant. The intended sequence is modular-monolith MVP first, with boundaries intentionally suitable for later extraction.

The decisive principle is consistent across the master and product plans: **AI is not the source of truth.** Inventory, pricing, orders and business rules live in the application database. The AI can retrieve information and invoke explicit application actions, but cannot invent product availability, price or order facts.

Additional implicit principles that should be made explicit in the final blueprint:

- A seller is the tenant and all business data is tenant-scoped.
- Channel transports are replaceable adapters; business workflow is channel-neutral.
- Inbound webhooks must be acknowledged quickly, made durable, then processed asynchronously and idempotently.
- Inventory and ordering are transactional domain operations, not LLM side effects.
- External models are consumption services; MVP needs no GPU or custom-model training.
- Early infrastructure must be low-cost and portable, but operational controls cannot be postponed indefinitely.

### 8.2 Architecture decision log

| Topic | Existing decision | Status | Step 4 action |
|---|---|---|---|
| Backend | ASP.NET Core / .NET 10 Web API. | Locked. | Retain. |
| Architecture | Modular monolith; Clean Architecture, DDD and CQRS/MediatR. | Locked intent. | Define practical module boundaries and avoid ceremony where it does not protect invariants. |
| Layers | `Domain → Application → Infrastructure → WebApi`. | Locked. | Retain dependency direction and module contracts. |
| Database | PostgreSQL with EF Core. | Locked. | Retain; define migrations, indexes, tenant strategy and transaction boundaries. |
| Multi-tenancy | Seller tenant, EF Core global filter/custom interceptor; optional Finbuckle. | Direction set, implementation open. | Choose one mechanism and document background-job/webhook tenant resolution. |
| Auth/RBAC | ASP.NET Core Identity and role-based access control. | Preferred, not fully specified. | Lock the identity/session/API trust model and role set. |
| Cache/jobs | Redis plus Hangfire. | Preferred. | Validate operational fit and specify queues, retries, idempotency and dashboard exposure. |
| Messaging providers | Meta Graph API for WhatsApp, Instagram and Facebook. | MVP direction. | Define individual provider adapters/capability matrix and explicit platform-review dependencies. |
| TikTok | Not in the existing technical plan. | Absent. | Add only as an adapter/capability placeholder until API eligibility is proven. |
| AI providers | OpenAI or Gemini SDKs; later multi-model routing. | Open. | Choose provider/router policy, data handling and fallback behavior. |
| AI approach | Tool calling, RAG, seller knowledge, memory, confidence and escalation. | Locked product behavior. | Specify guardrails, evaluation and human-control flows. |
| Frontend | Next.js **or** Blazor. | Unresolved. | Must be locked before implementation; Next.js is the current product-fit recommendation. |
| Object storage | Cloud object storage for product assets and knowledge files. | Direction set. | Select provider, media lifecycle and signed-access policy. |
| Deployment | Docker, CI/CD, low-cost managed cloud then scalable containers. | Direction set. | Select initial host, regions, environments, IaC/config and migration procedure. |

### 8.3 Existing domain inventory and completeness review

| Existing entity/concept | Purpose already covered | Gaps required for a complete product |
|---|---|---|
| Seller | Tenant/business root. | Workspace/store profile, tenant lifecycle, subscription/plan, settings, domain ownership and localized policy configuration. |
| SocialChannel | Encrypted external channel credentials. | Provider/account identity, capability set, authorization/refresh state, webhook health, error/retry status and token expiry. |
| Product / ProductVariant | Catalog and multi-attribute items. | Media, categories/collections, publish state, SKU/barcode, price history, tax/discount policy and SEO/storefront fields. |
| Inventory / InventoryReservation | Balance plus concurrency-safe hold. | Stock movement ledger, warehouse/location (future), reservation expiry/release, adjustment reason and fulfillment allocation. |
| Customer | Profile gathered from social chats. | Channel identities, contact consent, address book, deduplication/merge policy and privacy-retention lifecycle. |
| Conversation / Message | Unified inbox/message backlog. | Channel thread mapping, delivery/read status, media, assignment, bot/human ownership, labels, status/SLA and consent context. |
| Order / OrderItem | Checkout transaction and order state machine. | Address, delivery method/fee, source attribution, immutable financial snapshots, payment/fulfilment split states, cancellation/return/refund. |
| KnowledgeDocument | Seller RAG context. | Source/upload/version/chunk/embedding lifecycle, approval/publish state, tenant isolation and deletion/retention. |
| AIActionLog | Tool-call/audit trace and confidence. | Prompt/model version, retrieval context identifiers, input/output redaction, latency/cost, tool result and escalation outcome. |
| — | — | Missing first-class concepts: User/Membership/Role; Storefront/Domain; PaymentMethod/Attempt/Transaction/Settlement; DeliveryRule; Plan/Entitlement/UsageEvent; AuditEvent; WebhookEvent/Outbox; Notification/Receipt; ProductMedia; StockMovement; integration connection state. |

**Aggregate guidance.** The current plan correctly makes inventory reservation and order creation central. Step 4 should establish `Catalog`, `Inventory`, `Order`, `Conversation`, `Integration`, `Storefront`, `Payments`, `Billing`, and `Knowledge/AI` as explicit modules/aggregate areas. This is clearer than treating the listed entities as one flat EF model.

### 8.4 Module and implementation inventory

The master plan’s project structure is a sound starting point:

```text
src/
├── Domain/          entities, value objects, domain events and invariants
├── Application/     use cases/CQRS, validation, contracts and AI tool boundary
├── Infrastructure/  EF Core/Postgres, Identity, Redis, Hangfire, providers
└── WebApi/          HTTP API, webhook receivers, middleware and composition root
```

What it covers well:

- Tenant-aware persistence, audit hooks and entity boundaries.
- Catalog, inventory reservation and order-creation workflows.
- Provider abstraction (`ISocialChannelProvider`) and Meta-oriented channel implementation.
- Webhook signature validation, durable event persistence and Hangfire processing.
- Tool-calling orchestration, RAG and action logging.
- Test project expectations, Docker, CI/CD, health endpoints and logging.

What is under-specified:

- Frontend application topology, design system, public storefront and customer checkout.
- Module ownership/dependency rules inside the broad Application/Infrastructure projects.
- API versioning, error contract, pagination, authorization policy and OpenAPI/client strategy.
- Outbox/event dispatch, webhooks replay/dead-letter handling, schedule/retry semantics and operational runbooks.
- Object-storage media pipeline, asset authorization, image transformations and malware/file limits.
- Payment, delivery, notification/email and SaaS-billing modules.
- Tenant identity resolution for custom domains, jobs and externally initiated webhooks.

### 8.5 AI architecture audit

The proposed AI design is unusually solid where it matters: it explicitly protects source-of-truth commerce data. It uses external providers rather than premature custom training; tool calls map to application commands/queries; RAG serves merchant FAQ/shipping/refund context; confidence scoring supports escalation; and `AIActionLog` enables review.

**Existing tools:** `SearchProducts`, `CheckInventory`, `GetPrice`, `GetShippingInfo`, `CreateOrder`, `ReserveInventory`, `EscalateToHuman`.

**Required refinements:** distinguish safe read tools from write tools; require explicit structured validation for every write; add `quote_cart`, `get_order_status`, `create_checkout_link` and controlled `release_reservation`; record model/prompt/knowledge versions and cost/latency; set a maximum tool-loop budget; define conversation memory retention/summarization; prohibit cross-tenant retrieval; redaction/minimization before a provider call; and create offline evaluation cases in Nepali, English and Romanized Nepali. Product image/link/screenshot identification is desirable but has no acceptance criteria or media pipeline yet, so it belongs after the deterministic text/catalog MVP.

### 8.6 Deployment and operations audit

The deployment plan correctly starts with low cost: a Vercel-like frontend host, small container/VM backend, managed PostgreSQL, small Redis, object storage and external AI APIs. It uses Docker for portability and a reasonable growth path: load-balanced API replicas, stronger database/read replicas, more workers and model routing. Stated MVP sizing—1–2 vCPU and 2–4 GB RAM for both API and database—is a useful starting estimate, not a capacity commitment.

**Already required:** monitoring, error tracking, logs, backups, secret management, CI/CD, health checks, containerized deployment, rate limiting, encrypted social credentials, webhook validation, message idempotency and inventory concurrency control.

**Operational gaps:** environments (local/dev/staging/prod), infrastructure ownership/IaC, backup retention and restore testing, observability stack/alerts, secret rotation, database migration deployment/rollback policy, Redis durability, object-storage lifecycle, provider outage behavior, incident/runbook ownership, data-retention/deletion, disaster recovery targets and cost guardrails. “Automatic migrations during deployment” from Prompt 10 is particularly risky without a controlled migration gate and backward-compatible rollout strategy.

### 8.7 Prompt-matrix review

The master document contains ten prompts, while the divided prompt document contains nine. They are complementary rather than contradictory: the master version is implementation-specific (including the channel, webhook and inventory details), while the divided version is a compact sequencing guide. Step 5 should replace both with one authoritative prompt pack rather than asking builders to choose.

| Existing phase | Strength | Gap before use as an implementation contract |
|---|---|---|
| Foundation / tenant core | Correctly begins with architecture, tenant isolation, Identity/RBAC and migrations. | Need product roles, auth flow, configuration/secret policy, error/API conventions and a testable definition of done. |
| Catalog / inventory / order | Strong emphasis on reservations, locks and database truth. | Need stock movement, expiry, price/fee snapshots, storefront checkout and payment/delivery states. |
| Social/webhooks | Correct abstraction, encrypted tokens, signature validation, durable ingest and unique provider message IDs. | Need provider capability matrix, event/outbox/replay model, error states and platform approval plan. |
| AI / RAG | Tool calling and audit history align tightly with product safety. | Need prompt/versioning, evaluation, privacy, tool authorization and user-visible handoff behavior. |
| Frontend | Divided plan correctly names seller dashboard pages. | Missing public storefront, checkout, onboarding, navigation/data-fetching strategy and frontend/backend contract. |
| Testing | Calls for unit, integration, API, security and load tests; master calls out tenant/concurrency tests. | Add provider contract/webhook replay, payment callback, authorization, AI-tool safety and end-to-end storefront tests. |
| Deployment | Docker/CI/CD/health/logging are correctly requested. | Need staging, reversible migrations, alerts, backup restore and production-release checklist. |

### 8.8 Conflicts, ambiguity and scope risks

1. **Frontend remains unresolved.** “Next.js or Blazor” is a decision, not a plan. It blocks public storefront architecture and the coding prompts.
2. **Identity is only partially chosen.** ASP.NET Core Identity is named, but tenant membership, session/token strategy and potential external-auth boundary are not.
3. **The product roadmap under-scopes storefront, payment, delivery and billing.** Existing Phases 1–5 can create a technically sound inbox AI but not a Saney-class commerce product.
4. **Channel scope is optimistic.** WhatsApp first then Instagram/Facebook is sensible; platform approval/API capability must be treated as an external dependency. TikTok is absent.
5. **AI image identification is premature for MVP.** It adds vision/OCR/evaluation/media complexity before the catalog and order path are proven.
6. **“CQRS/DDD/Clean Architecture” needs proportionality.** Enforce invariants and module boundaries, but avoid layer abstractions or MediatR use where they provide no product value.
7. **Automated production migrations need controls.** Unconditional migration-on-startup is unsafe once zero-downtime deployments or multiple API instances exist.
8. **SaaS economics are missing.** No plan, quota, metering, subscription, fee ledger or VAT/commercial ownership is defined.

### 8.9 Recommended stack locks for the final blueprint

These are recommendations, pending explicit confirmation in Step 4; they are not retroactive changes to the source docs.

| Decision | Recommendation | Reason |
|---|---|---|
| Frontend | Next.js (TypeScript) with a separate seller app and public storefront surface. | Best fit for mobile/SEO storefront, domain routing and a polished social-commerce UX. |
| Backend | Retain .NET 10 modular monolith. | Existing plan is coherent and especially strong for transactional correctness. |
| Identity | ASP.NET Core Identity with tenant membership/RBAC; OIDC-compatible boundary for later social login/SSO. | Avoids premature external auth dependency while preserving a future integration seam. |
| AI | Provider abstraction, Gemini-first or OpenAI-first selected by cost/quality/privacy test; do not couple domain to an SDK. | Keeps external models replaceable and supports controlled routing later. |
| MVP channels | WhatsApp plus Instagram only after capability verification; use storefront links as universal conversion fallback. | Limits provider-review risk while validating the core sales loop. |
| Payments | COD + merchant QR in MVP; eSewa/Khalti adapters after KYC/API settlement validation. | Ships locally useful payment modes without betting core flow on unverified gateway onboarding. |
| Billing | Entitlement/usage tracking in MVP, manual plan assignment/payment initially. | Prevents later data-model rework while delaying non-core subscription collection. |
| Hosting | Dockerized API/worker, managed Postgres, Redis and object storage; choose one cloud/region in Step 4. | Fits low-cost posture and maintains portability. |

### 8.10 Step 2 output: normalized baseline

The existing plans form a **strong backend and AI safety foundation**. Their most mature areas are tenant isolation, relational source-of-truth data, concurrency-safe reservations, webhook idempotency, provider abstraction and tool-calling/RAG patterns. They are not yet a build-ready full product blueprint because public storefront, checkout, payments, delivery, SaaS entitlements, product onboarding, frontend architecture, provider-operational reality and production runbooks have not been designed to the same depth.

The next step must therefore compare the baseline against the Step 1 product map and distinguish: retain as-is, extend, defer, or explicitly reject for MVP.

---

## 9. Step 3 — Alignment matrix: Kreyora baseline vs. Saney-class product

**Decision framing.** “Saney parity” means that a Nepal social seller can move from signup to a credible live store, accept a social enquiry, turn it into a safe order, and operate the result from one workspace. It does not mean copying a competitor’s brand, unverified features, implementation, metrics, or commercial terms.

### 9.1 Feature-by-feature alignment matrix

| Product capability | Existing Kreyora state | Alignment | Decision | Main implementation consequence |
|---|---|---|---|---|
| Multi-tenant seller workspace | Seller tenant, EF filters/interceptor, RBAC proposed. | Strong | Retain and complete. | Add membership, settings, onboarding and tenant resolution for jobs/domains. |
| Catalog, variants and media | Products/variants modeled; image identification mentioned. | Strong core / partial surface | Extend. | Add media, publishing, categories, storefront fields and immutable price history/snapshots. |
| Inventory and reservations | Explicit inventory/reservation entities, locks and concurrency tests. | Strong | Retain as a differentiator. | Add stock ledger, expiry/release and shared allocation for every order source. |
| Orders | Order/OrderItem and state machine planned. | Strong core / incomplete commerce lifecycle | Extend. | Separate payment/fulfilment states; add address, delivery, source and financial snapshots. |
| Unified social inbox | Conversation/message model, channel abstraction and webhook plan. | Strong | Retain and complete. | Add ownership, message transport state, customer identity mapping, filters and operator UX. |
| WhatsApp / Instagram / Facebook | Meta provider abstraction and staged rollout planned. | Strong architecture / external dependency | Retain with gates. | Test/provider-certify each adapter; do not promise capability before approval. |
| TikTok | Saney markets it; Kreyora does not plan it. | Gap | Defer, preserve seam. | Capability-based provider interface; no MVP commitment. |
| AI sales assistant | Tools, RAG, confidence, escalation and audit log planned. | Strong | Retain and harden. | Tool authorization, evaluation, multilingual behavior, prompt/version/cost traceability. |
| Human takeover | Explicit in product plan. | Strong | Retain. | Conversation assignment, bot pause/release, escalation queue and audit. |
| Public storefront | Only implied by “frontend”; no domain model/flow. | Critical gap | Add to MVP. | Separate public surface, tenant routing, product pages, cart, checkout and confirmation. |
| Custom domain / SSL | Not planned. | Gap | Phase 1.5 / post-MVP. | Start subdomains; then DNS/TLS lifecycle and host-based tenant resolution. |
| Delivery configuration | Shipping-info tool exists, but delivery domain does not. | Critical gap | Add to MVP. | Delivery zones/rules, method/fee/ETA calculation and order snapshots. |
| COD and merchant QR | Not planned. | Critical gap | Add to MVP. | Payment method/state model and manual verification workflow. |
| eSewa / Khalti | Not planned. | High-value gap | Phase 2 after validation. | Provider adapters, signed callbacks, reconciliation, refunds and settlement ownership. |
| Subscription, quotas and usage | Not planned. | Product packaging gap | Add foundations to MVP. | Plan/entitlement/usage events; manual billing initially. |
| Transaction fees / fee ledger | Not planned. | Commercial gap | Design now, activate only when business model is chosen. | Immutable fee calculation/versioning and VAT treatment; legal/accounting validation. |
| Analytics | Mentioned, dashboard/reports unstated in depth. | Partial | MVP operational dashboard; extend later. | Durable metric events and attribution by channel/order source. |
| Seller onboarding | Not planned as a product flow. | Critical usability gap | Add to MVP. | Readiness checklist, setup states and activation gates. |
| Email receipts/notifications | Not planned. | Partial gap | Include confirmation baseline. | Notification/outbox, templates, provider adapter and delivery log. |
| Data export | Not planned. | Gap | Phase 2. | Tenant-safe export job and audit record. |
| Reliability/security | Encryption, idempotency, locks, rate limits, health checks planned. | Strong baseline / incomplete operation | Retain and extend. | Outbox/DLQ/replay, backup restore, monitoring/alerts, retention and runbooks. |

### 9.2 What we should retain unchanged

1. **Modular .NET 10 monolith with Postgres as commerce truth.** It matches the product’s transactional needs and leaves a reasonable extraction path without paying microservice cost before product-market fit.
2. **Multi-tenant isolation as an architectural invariant.** EF query filtering alone is insufficient, but the intent and first mechanism are correct.
3. **Concurrency-safe inventory reservations.** This is stronger than a typical chatbot-first design and is essential when bot, storefront and staff can sell the same variant.
4. **Durable, idempotent webhook intake.** Persist then process in a worker; do not allow duplicate provider events to create duplicate messages/orders.
5. **Channel-provider abstraction.** Separate external transport quirks from the conversation/order domain.
6. **Tool-calling rather than AI-owned state.** The existing tool list correctly protects price, availability and ordering from hallucination.
7. **RAG for merchant policy/FAQ material, not stock/order truth.** This is the correct boundary.
8. **Human escalation and AI action audit logs.** Both are product safety features as well as support/debugging tools.

### 9.3 Gaps to add, ordered by business risk

| Rank | Gap | Why it is critical | Required first cut |
|---:|---|---|---|
| 1 | Public storefront and checkout | Without it, Kreyora is an inbox assistant rather than a commerce operating system. | Subdomain shop, published catalogue, cart, address, checkout and confirmation. |
| 2 | Delivery, COD and QR | These are the locally credible ways to complete orders before payment-gateway work. | Delivery rules; COD; merchant QR/manual verification; payment status. |
| 3 | Product onboarding/readiness | Sellers cannot safely activate an AI/store if catalog, delivery and policies are incomplete. | Resumable checklist; publish/activate validation; guided defaults. |
| 4 | Full order economics and lifecycle | Current order model lacks payment, delivery and source truth. | Separate order, payment and fulfilment states; immutable totals/fees; cancellation. |
| 5 | Frontend architecture | “Next.js or Blazor” is unresolved despite seller and buyer apps being central. | Lock Next.js/TypeScript, seller workspace and public-shop surfaces. |
| 6 | SaaS entitlements/usage | Competitor packaging makes limits visible; retrofitting makes all modules harder. | Versioned plan definitions, manual assignment, usage events and limit warnings. |
| 7 | Provider-operational model | APIs, credentials, retries and approval states determine whether channel promises are real. | Connection state, capability flags, webhook replay/DLQ and provider onboarding checklist. |
| 8 | Production operations | A message/order product cannot rely only on generic hosting advice. | Environments, alerts, backups/restores, secrets, release/migration and incident runbooks. |
| 9 | Analytics event model | Dashboard/report claims cannot be added faithfully from raw transactional tables later. | Event/source attribution and basic operational metrics. |
| 10 | Custom domains | Useful brand/SEO conversion feature, but not required to validate the selling loop. | Platform subdomain in MVP; custom domains after core order flow. |

### 9.4 Architecture gaps exposed by product requirements

The current architecture is complete for a controlled chat-to-order backend but not for a multi-channel commerce product. The following extensions are mandatory:

- **Storefront module:** public tenant resolution, store profile/theme settings, product publication, cart/checkout and domain lifecycle.
- **Payments module:** method availability, initiation/manual proof/callback, payment state machine, reconciliation and financial snapshots.
- **Delivery module:** zone/rule calculation and address capture; provider/carrier integration is intentionally later.
- **Billing module:** plan, entitlement, usage event and quota evaluation independent of payment collection.
- **Integration runtime:** `WebhookEvent`, idempotency key, outbox, retry/DLQ/replay and connection-health model.
- **Notifications module:** order/receipt notifications, provider boundary and delivery audit.
- **Media module:** product and knowledge-file storage, signed access, validation and lifecycle policy.
- **Identity/tenant context model:** user membership/roles and reliable tenant resolution for API, worker, webhook and host-name entry points.
- **Public/private frontend split:** shared API contracts but distinct authentication, caching and SEO/security concerns.

### 9.5 Risk register if we build only the current plans

| Risk | Probability | Impact | Mitigation decision |
|---|---|---|---|
| Build a technically impressive inbox with no complete purchase journey. | High | Critical | Add storefront, checkout, delivery and local payment primitives to MVP. |
| AI creates orders that cannot be paid/fulfilled or financially reconciled. | High | Critical | Model payment, delivery, fee and fulfilment states before implementation. |
| Provider API/approval limits block announced channels. | Medium–high | High | Ship channel adapters behind verified capability gates; storefront link is the fallback conversion path. |
| Tenant data leaks through workers, webhooks or custom domains. | Medium | Critical | Treat tenant context as required data on events/jobs and add isolation tests at each boundary. |
| Overselling across bot/store/operator flows. | Medium | High | Use one reservation/allocation service and immutable stock ledger. |
| Payment dispute or manual QR fraud has no operational record. | Medium | High | Add payment attempt/proof/verification/audit model; retain seller confirmation authority. |
| Frontend choice or public-store strategy changes mid-build. | High | High | Lock frontend and two-surface topology before Step 5 prompts. |
| Cost/reliability degrades under webhook/LLM bursts. | Medium | High | Queue, rate limit, model budgets, observability and worker scaling plan. |
| Plan limits are retrofitted inconsistently. | Medium | Medium | Establish entitlement/usage event foundation in MVP. |
| Scope balloons into integrations and advanced AI before sales loop works. | High | High | Adopt the explicit MVP cut below and phase-gate all optional providers/features. |

### 9.6 Saney-parity MVP definition

The MVP is complete when a Nepal seller can reliably operate this loop:

```text
Create workspace → finish setup checklist → publish catalog/storefront
→ connect one verified social channel → receive chat
→ AI answers from live catalog/policies or hands off to staff
→ customer completes storefront or assisted order
→ COD / QR payment is recorded → seller fulfils and tracks order
```

**MVP scope**

- Tenant account, seller workspace, membership/RBAC and onboarding progress.
- Seller profile/policies, catalog, variants, images, stock movements and reservation expiry.
- Public mobile storefront on platform subdomain with product pages, cart, address and checkout.
- Delivery zones/rules, COD and merchant QR/manual-payment verification.
- Canonical orders with line items, source, totals, payment and fulfilment state; basic cancellation.
- One production-ready channel adapter (recommend WhatsApp or Instagram after capability validation), normalized inbox and manual staff reply.
- AI assistant for supported intents only: catalog/variant search, stock, price, delivery/COD, order draft/reservation and escalation; Nepali/English/Romanized-Nepali test cases.
- Seller dashboard for setup, inbox, catalog, orders, low stock and basic revenue/order/reply-time metrics.
- Entitlement/usage data model and plan-limit visibility; manual plan assignment is acceptable.
- Idempotent webhooks, encrypted credentials, audit log, worker retries/DLQ/replay, tests, backups and observable deployment.

**MVP acceptance conditions**

1. No cross-tenant data access in API, worker, webhook, storage or domain routing paths.
2. A duplicate inbound webhook cannot duplicate a message, reservation, payment or order.
3. A concurrent storefront/bot/staff purchase attempt cannot oversell a single variant.
4. AI cannot report stock/price/delivery/order facts without the corresponding application query or command audit entry.
5. Checkout totals, fees and payment/delivery choices persist unchanged on the completed order.
6. A seller can take over an AI conversation and the assistant stops sending until released.
7. A failed provider/worker operation is visible and safely replayable.

### 9.7 Beyond-parity differentiators

| Differentiator | Customer value | Earliest sensible phase |
|---|---|---|
| Auditable deterministic AI actions | Sellers can trust and investigate bot behavior. | MVP foundation. |
| Strong reservation/stock ledger | Fewer viral-post oversells and fewer manual reconciliations. | MVP foundation. |
| Explainable handoff/confidence | Staff know why AI escalated and what it already verified. | MVP. |
| Integration diagnostics/replay | Faster recovery than opaque “bot stopped working” experiences. | MVP foundation / Phase 2 UI. |
| Seller-controlled knowledge lifecycle | Safer policy updates with approval/versioning rather than opaque training. | Phase 2. |
| Unified order and payment ledger | Accurate profitability/settlement view across sources. | Phase 2. |
| Provider-agnostic AI routing | Cost and quality resilience; model choice can evolve. | Phase 2 after evaluation baseline. |
| Data export/portability | Builds seller trust and avoids lock-in. | Phase 2. |

### 9.8 Explicitly out of scope for v1

- TikTok DM/chatbot integration until API capability, permissions and commercial eligibility are verified.
- eSewa/Khalti live gateway processing until KYC, settlement, webhook and refund responsibilities are validated; keep adapter interfaces.
- Automated subscription charging, prorations, invoices, dunning and sophisticated revenue recognition.
- Custom domain self-service and managed DNS/TLS; platform subdomain is sufficient for MVP.
- Carrier/courier API integrations, live tracking and route optimization.
- Marketplace/multi-vendor catalog, dropshipping, POS, accounting/ERP and advanced warehouse management.
- Coupons, loyalty, affiliate programs, returns portal and advanced promotions.
- Customer accounts, saved payment methods, wishlists and advanced storefront search/SEO experiments.
- Custom AI model training, autonomous discounting, proactive campaigns, unbounded agent autonomy and image/OCR product identification.
- Multi-region high availability, formal SOC 2 certification or a 99.99% SLA.

### 9.9 Alignment verdict and Step 3 decisions

**Verdict:** Kreyora is a strong, safer-than-average technical core for a Saney-class product, but it is only approximately **60% aligned** with the full product surface. It is roughly **85% aligned with the transactional chat/AI backend**, and materially under-specified in storefront commerce, local payments/delivery, SaaS packaging, onboarding and production operations. The percentage is a planning heuristic, not a measured product score.

**Proceeding decisions:** retain the .NET transactional core; add storefront/checkout, delivery, COD/QR, onboarding, expanded order model, frontend lock and entitlement foundations to MVP; protect provider/payment integrations behind validation gates; defer TikTok, custom domains, live gateways and advanced intelligence. Step 4 must now turn this scope into one internally consistent blueprint with precise modules, state machines, data model, APIs and operating controls.

---

## 10. Step 4 — Master product and technical blueprint

### 10.1 Product definition, positioning and scope

**Working product definition:** Kreyora is a Nepal-focused social-commerce operating system for independent sellers. It turns social enquiries into trustworthy orders through a catalog-aware AI assistant, one seller inbox, a branded public storefront, local-first payment choices and operational order/inventory control.

**Positioning:** “The reliable commerce layer behind a seller’s social DMs.” The product should not be sold as a replacement for human customer relationships or as an autonomous sales agent. It makes a small seller more responsive, organized and credible while keeping them in control.

**MVP outcome:** a seller can independently launch a platform-subdomain shop, load a catalog, configure delivery and COD/QR payment, connect one validated social channel, receive and handle a conversation, let the assistant answer safe catalog/policy questions, create a stock-safe order, and fulfil it from a unified workspace.

**Primary personas**

| Persona | Job to be done | Product success signal |
|---|---|---|
| Seller owner | Start and operate a professional social-commerce business without building a website or manually answering every DM. | Reaches a live, correctly configured storefront and completes orders without technical help. |
| Seller operator | Resolve conversations and fulfil orders quickly while preserving accurate stock/payment status. | Can identify the next action, take over AI, and complete an order without switching systems. |
| Customer | Ask in a familiar channel and confidently buy with clear price, delivery and payment choices. | Receives accurate response and completes checkout with minimal friction. |
| Platform operator | Support tenants and providers without leaking data or mutating seller commerce records ad hoc. | Can audit/replay failed integration events and investigate actions with tenant-scoped evidence. |

**MVP boundary:** only one externally validated messaging provider needs to be live. The rest of the channel model, storefront, orders, payments and seller operations must be channel-neutral from day one.

### 10.2 Locked stack and solution topology

These choices are locked for the implementation roadmap unless a validation gate fails:

| Concern | Decision | Rationale |
|---|---|---|
| Seller/public frontend | Next.js + TypeScript, deployed as two route surfaces in one frontend codebase or two apps in one monorepo. | Best fit for mobile storefronts, SEO, domain routing and fast product UX. |
| API/backend | ASP.NET Core .NET 10 modular monolith. | Strong transactional model and existing plan continuity. |
| Data | PostgreSQL + EF Core; schema-per-application, shared tables with mandatory `TenantId`. | Operationally simple at MVP; retains relational correctness. |
| Async/cache | Redis plus Hangfire workers; database outbox for integration/notification publication. | Separates short-lived state from durable event delivery. |
| Files | S3-compatible object storage with private originals and signed, scoped asset delivery. | Suitable for product media and seller knowledge files. |
| Identity | ASP.NET Core Identity, tenant memberships and policy-based RBAC; OIDC-compatible edge. | Full control now, future external auth/SSO compatibility. |
| AI | Provider-agnostic application interface; select initial provider by documented Nepal-language quality, latency, cost and data terms. | Avoids coupling commerce logic to a model SDK. |
| Deployment | Dockerized API and worker, managed PostgreSQL/Redis/object storage, CDN/frontend host, separate staging and production. | Low-cost portability with a clear scale path. |

**Repository topology**

```text
apps/
  seller-web/                 # authenticated seller workspace (Next.js)
  storefront-web/             # public tenant storefront (Next.js; may initially share app)
services/
  api/src/
    Domain/                   # aggregates, value objects, invariants, domain events
    Application/              # commands/queries, policies, provider-neutral contracts
    Infrastructure/           # EF/Identity/Redis/Hangfire/storage/providers
    WebApi/                   # REST, webhooks, auth, tenancy and composition
  worker/                     # optional host if jobs are not co-hosted initially
packages/
  api-contracts/              # generated OpenAPI client/types or shared schemas
  ui/                         # only genuinely shared design primitives
infra/                        # Docker, environment templates, deployment/IaC
tests/                        # unit, integration, contract and end-to-end suites
```

Start with a single API host plus Hangfire worker process only if resource pressure makes isolation necessary. Do not split domain modules into network services before measured ownership/scale pressure exists.

### 10.3 Modular backend architecture

Modules own their domain rules and application use cases. Cross-module reads use stable query contracts; cross-module side effects use domain/integration events or explicit application orchestrators. Modules must not reach directly into another module’s EF repositories.

| Module | Owns | Key responsibilities |
|---|---|---|
| Tenancy & Identity | Tenant, User, Membership, Role, audit actor context. | Authentication, authorization policies, tenant resolution and lifecycle. |
| Storefront | Store profile, product publication, domain and cart/checkout session. | Public routing, store readiness, storefront catalog projection and checkout initiation. |
| Catalog | Product, Variant, media references, collections and price definition. | Catalog CRUD, publish validation and canonical product facts. |
| Inventory | Stock ledger, balance, reservations and allocation. | Availability, atomic reserve/release/commit and adjustment audit. |
| Customers | Customer profile, channel identities, addresses and consent. | Identity linking/deduplication and PII lifecycle. |
| Conversations | Conversation, message, assignment, labels and interaction state. | Unified inbox, staff replies, automation/handover state and customer timeline. |
| Integrations | Channel connections, webhook events and provider capabilities. | Credential lifecycle, inbound normalization, outbound delivery, replay and connection health. |
| AI assistant | Assistant configuration, knowledge sources, action trace and evaluation. | Safe orchestration, retrieval, tool authorization, confidence and escalation. |
| Orders | Order, items, totals, delivery snapshot and fulfilment workflow. | Transactional checkout, order state invariants and cancellation. |
| Payments | Payment method, attempt, transaction, proof, refund and settlement. | Manual/gateway flows, callback idempotency and payment state. |
| Billing | Plan, entitlement, usage event and subscription record. | Feature access, quota evaluation, usage snapshots and manual plan assignment. |
| Notifications | Notification request/delivery log and templates. | Receipts, operational notices and provider abstraction. |
| Reporting | Immutable metric events and read models. | Dashboard projections and later reports without mutating commerce truth. |

### 10.4 Domain model, ownership and state machines

#### Core aggregate rules

| Aggregate | Root and key children | Invariants |
|---|---|---|
| Catalog | `Product` → `ProductVariant`, media references. | Only published, price-valid variants can appear in a purchasable quote; all records have `TenantId`. |
| Inventory | `InventoryItem` → `StockMovement`, `InventoryReservation`. | Available = on-hand − committed reservations; reservations expire/release idempotently; only a transaction can allocate stock. |
| Order | `Order` → `OrderItem`, address/delivery/financial snapshots. | Totals are derived and frozen at confirmation; items identify a variant/version; payment and fulfilment are independent. |
| Conversation | `Conversation` → `Message`, assignment/automation state. | A conversation has one current owner mode; inbound provider ID is unique per connection; AI cannot send after human takeover. |
| Payment | `PaymentAttempt` → provider transaction/proof/refund references. | Callback/reference idempotency; a successful payment amount cannot exceed order amount without an explicit adjustment/refund path. |
| Storefront | `Store` → domain configuration and checkout session. | Store cannot be publicly purchasable until catalog, delivery and payment readiness pass. |
| Integration | `ChannelConnection` → `WebhookEvent`/delivery attempt. | Secrets encrypted; each external account maps to one tenant connection; capability checks precede every action. |
| Billing | `Subscription` → entitlement snapshot/usage events. | Feature/quota evaluation is deterministic against versioned plan rules. |

**Order state:** `draft → awaiting_customer → pending_confirmation → confirmed → processing → fulfilled`, with `cancelled` allowed before fulfilment and `returned` reserved for Phase 2. `PaymentStatus` is separate: `not_required | pending | awaiting_verification | authorized | paid | failed | refunded | partially_refunded`. `FulfilmentStatus` is separate: `unfulfilled | ready | dispatched | delivered | failed | cancelled`.

**Reservation state:** `active → committed | released | expired`. A reservation includes tenant, variant, quantity, source, order/cart/conversation reference, expiry and idempotency key. Creation and commitment lock or concurrency-check the inventory row; expiry is an idempotent worker job.

**Payment paths:**

- COD: order becomes confirmed with payment pending; staff records collection on delivery/settlement.
- Merchant QR/manual transfer: order becomes `awaiting_verification`; customer proof is optional/configurable; staff accepts or rejects with an audit record.
- Gateway (Phase 2): create attempt → redirect/initiation → signed callback → provider reconciliation → paid/failed; callbacks never mutate an order without a matching attempt/reference.

**Conversation state:** `new → bot_active ↔ human_assigned → awaiting_customer → checkout_in_progress → order_created → resolved`; `closed` and `spam` are dispositions. Message transport receipt states are independent. Takeover sets automation to paused and emits an audit event; only an authorized explicit release can resume it.

### 10.5 Multi-tenancy, identity and authorization

`TenantId` is mandatory on every tenant-owned table, unique index and event/job payload. Resolve context by: authenticated membership for seller API calls; connection identifier for inbound webhooks; persisted tenant ID on background jobs/outbox events; and verified hostname mapping for storefront traffic. Never infer a tenant from an untrusted request header.

Use global EF query filters as a defense-in-depth convenience, not the only barrier. Every command verifies tenant ownership of referenced IDs; raw SQL/projections require explicit `TenantId`; object-storage paths use a tenant prefix and signed policy; cache keys include tenant; logs/traces include tenant only in approved, non-sensitive form.

Initial roles: `Owner` (billing, integrations, domains, full control), `Admin` (operations/configuration except ownership/billing), `Operator` (inbox/orders/catalog bounded by policy), `Viewer` (read-only), and `PlatformSupport` (time-bound audited support access; never default cross-tenant query access).

### 10.6 Channel integration and event reliability design

Define `IChannelProvider` around capabilities, not a misleading lowest common denominator:

```text
ValidateWebhook(request) -> validation result
NormalizeInbound(raw event) -> normalized inbound event(s)
SendMessage(connection, outbound message) -> delivery result
GetConnectionCapabilities(connection) -> capability set
RefreshOrValidateConnection(connection) -> connection status
```

Inbound flow: provider webhook → signature/verification + lightweight schema validation → persist immutable `WebhookEvent` with provider event ID/hash and tenant/connection → return success promptly → worker normalizes event → idempotently upserts customer/conversation/message → emits domain event → invokes AI only if automation is active and entitlement permits → queues outbound response through outbox → provider send worker records delivery result/retry.

Use unique constraints for provider event and message IDs, an outbox for externally visible sends, exponential retry with bounded attempts, dead-letter/replay controls, correlation IDs and an integration-health page. Retain raw payloads only for the shortest defensible period and protect/redact them because they may contain customer PII.

### 10.7 AI orchestration and knowledge design

The assistant is a constrained application orchestrator:

1. Load tenant assistant policy, conversation state and approved language/tone.
2. Classify intent and retrieve only tenant-approved knowledge passages where policy/FAQ context is needed.
3. Offer read tools; require schema-validated write tools and transaction outcomes for reservations/order drafts.
4. Enforce maximum tool iterations, time/cost budgets and capability/entitlement checks.
5. Generate a concise customer response or escalate; persist a redacted trace with model/prompt/knowledge/tool versions, latency, token/cost band and outcome.

| Tool | Access | Rule |
|---|---|---|
| SearchProducts, CheckInventory, GetPrice, GetShippingInfo, GetOrderStatus | Read | Return current application data; do not expose data from another tenant/customer. |
| QuoteCart, CreateOrderDraft, ReserveInventory, ReleaseReservation, CreateCheckoutLink | Controlled write | Require validated variant/quantity/customer context, idempotency and explicit tool result. |
| EscalateToHuman | Write | Pauses automation or creates queue assignment with reason/confidence. |

RAG is only for seller-controlled documents such as FAQ, shipping, returns and brand guidance. Catalog, inventory, pricing, delivery calculation, payment status and order status always come from tools. Knowledge documents have source, version, chunk/embedding metadata, approval state and deletion lifecycle. Build a small offline evaluation set before enabling automation: price/stock, size ambiguity, delivery/COD, refusal/escalation, code-mixed Nepali/English, malicious instructions and unavailable-product cases.

### 10.8 Storefront, checkout and public-domain design

The first storefront is a platform-managed subdomain: `{slug}.project-domain`. Each store contains brand/profile, policy content, delivery/payment availability and a published-catalog projection. Custom domains are a later state machine: `requested → dns_pending → verified → provisioning_tls → active | failed`, with a retryable verification record.

Public pages: store home, collection/category (minimal first), product detail, cart, checkout, order confirmation and basic order lookup/support path. The public app reads a cacheable published-catalog projection but calls the API for cart price, availability, delivery quote and order creation. Never trust browser totals, product price, stock or tenant identity.

Checkout sequence: validate store readiness → validate cart variants/published state → create short reservation/quote → capture customer/contact/address → calculate delivery/tax/fees → select available method → create canonical order + immutable snapshots → initiate/record payment method → send confirmation. Expired/failed checkout releases its reservation safely.

### 10.9 Payments, delivery, billing and fee accounting

**Delivery:** start with seller-defined zones/rules, flat or threshold-based fee, optional ETA text and COD availability. Persist delivery address and calculated rule/fee snapshot on the order. Carrier APIs are not a prerequisite.

**Payments:** model availability per store/order context. The MVP supports COD and merchant QR/manual transfer. Enforce no “paid” state without explicit seller verification or gateway evidence. For later eSewa/Khalti integrations, use provider adapters with signed callback handling, idempotency, reconciliation and documented merchant/platform settlement responsibility.

**Billing/quotas:** plans contain versioned feature flags and metric limits; a subscription records the selected plan and billing period; `UsageEvent` tracks quantity/source/idempotency. MVP plan changes are executed by staff/owner workflow, not automated card billing. Usage at 70/90/100% produces notifications; the exact hard-stop policy is feature-specific and must never corrupt existing commerce data.

**Fees/taxes:** all order financial data is versioned and immutable: merchandise subtotal, discount, delivery, tax/VAT, provider fee, platform fee, total payable and currency (NPR). Do not activate a transaction-fee model or issue tax representations until business/legal/accounting ownership is confirmed.

### 10.10 API surface and data persistence outline

Use versioned REST endpoints with OpenAPI, cursor pagination for collections, RFC 7807-style problem responses, idempotency keys for externally retried writes and authorization policies per endpoint. Example surface:

| Area | Representative endpoints |
|---|---|
| Identity / tenancy | `/v1/me`, `/v1/workspace`, `/v1/members`, `/v1/onboarding` |
| Catalog / inventory | `/v1/products`, `/v1/products/{id}`, `/v1/variants/{id}/inventory`, `/v1/stock-movements` |
| Conversations | `/v1/conversations`, `/v1/conversations/{id}/messages`, `/takeover`, `/release-automation` |
| Orders / payments | `/v1/orders`, `/v1/orders/{id}`, `/cancel`, `/payments/manual-verify` |
| Storefront administration | `/v1/store`, `/v1/store/readiness`, `/v1/domains`, `/v1/delivery-rules`, `/v1/payment-methods` |
| Public storefront | `/public/v1/stores/{slug}`, `/products`, `/checkout/quote`, `/checkout/orders` |
| Integrations | `/v1/integrations`, `/connect`, `/reauthorize`, `/health`; provider-specific `/webhooks/...` |
| AI / knowledge | `/v1/assistant`, `/v1/knowledge-documents`, `/v1/assistant/test` |
| Billing / reporting | `/v1/billing/plan`, `/usage`, `/v1/dashboard`, `/v1/analytics` |

Key tables/indexes: every tenant table has `TenantId` and composite indexes aligned to common reads—e.g. `(TenantId, Slug)` for products, `(TenantId, Status, UpdatedAt)` for orders/conversations, `(TenantId, VariantId)` for inventory, and unique `(ConnectionId, ProviderMessageId)` / `(ConnectionId, ProviderEventId)` for provider idempotency. Use append-only `StockMovement`, `UsageEvent`, `AuditEvent`, `WebhookEvent`, payment-transaction and outbox records. Keep financial/order snapshots in the order table/items rather than recalculating from live catalog rules.

### 10.11 Security, compliance and data governance

- HTTPS everywhere; encrypted provider tokens/secrets using a managed key system; secret rotation and access audit.
- Policy-based RBAC, secure session/token handling, CSRF posture for browser endpoints, rate limits and abuse limits for public checkout/AI.
- Webhook signature verification, replay-window protection, payload-size/type limits and allowlisted provider endpoints where practical.
- PII minimization: classify customer contacts/addresses, restrict support access, define retention/deletion/export workflows and redact sensitive values in logs/AI traces.
- Tenant isolation tests across EF, raw queries, cache, jobs, storage, search/vector retrieval and hostname resolution.
- Audit immutable business/security actions: identity/role changes, integrations, bot takeover/release, inventory changes, payment verification, order state changes, billing changes and support access.
- Publish accurate terms, privacy, returns/refund and payment-policy content before customer launch; obtain local legal/accounting advice for KYC, consumer protection, data practices and VAT.

### 10.12 Observability, reliability and environment strategy

**Metrics:** API latency/error rate, webhook acknowledge/processing lag, duplicate-event rejection, outbox backlog, provider delivery success, AI latency/cost/tool failure/escalation, reservation conflicts/expiry, checkout conversion/failure, payment verification lag, background-job retry/DLQ depth, database/Redis health and tenant usage.

**Minimum service objectives to validate in staging:** fast webhook acknowledgement independent of LLM latency; no silent message loss; replayable failed integration events; daily database backups plus restore test; protected production migrations; alert routing for webhook/worker/payment failures. Do not advertise numerical SLA or “bank-grade/SOC 2” claims until evidence and operations support them.

| Environment | Purpose | Rules |
|---|---|---|
| Local | Developer flow. | Docker dependencies, seeded tenant, mock providers and no production secrets. |
| Staging | Integration/acceptance gate. | Isolated database/storage/providers, migration rehearsal, synthetic or approved test data. |
| Production | Seller/customer traffic. | Separate secrets, least privilege, backups/alerts, controlled releases and audit retention. |

Configuration uses typed options validated at startup, environment-secret injection rather than committed secret files, explicit feature flags/capability gates, and documented config ownership. Database migrations run as a controlled, observable deployment job—not automatically and concurrently from every API instance.

### 10.13 Build-ready architecture decisions and validation gates

**Approved for build now:** Next.js + .NET 10 modular monolith; Postgres/EF; Redis/Hangfire/outbox; shared-tenant schema with strict isolation; catalog/inventory/order/conversation core; platform-subdomain storefront; seller-defined delivery; COD/merchant QR; manual billing with entitlements; one verified channel; provider-agnostic AI tools/RAG; staged Docker deployment.

**Must validate before enabling:** channel provider approval/capability; AI provider contract and language evaluation; object storage/region; payment-gateway KYC/settlement/refund operations; production domain/certificate provider; VAT/legal/commercial model; retention policy and support process.

**Deferred by design:** TikTok, custom domains, live eSewa/Khalti, subscription collection, carrier integrations, advanced promotions/returns, image/OCR identification, autonomous campaigns, multi-region HA and formal compliance certifications.

This blueprint is the authoritative product/technical design for Step 5. New work that conflicts with it must be recorded as a decision change in this section rather than silently changing a coding prompt.

---

## 11. Step 5 — Implementation roadmap, acceptance gates and authoritative build prompts

### 11.1 Release scope and sequencing rules

The delivery sequence is deliberately **vertical**: every milestone proves a usable slice while preserving the final architecture. Do not build all entities, integrations or AI features before a seller can complete a real storefront order.

| Release | Objective | Included | Explicitly excluded |
|---|---|---|---|
| Foundation | Establish safe delivery rails. | Repo, environments, tenancy/identity, observability, deployment pipeline and test harness. | Channels, AI, checkout and payment. |
| MVP Alpha | Prove deterministic commerce. | Seller onboarding, catalog, inventory, platform-subdomain storefront, cart, checkout, delivery, COD/QR and orders. | Live social channels, automated AI, gateway payments and custom domains. |
| MVP Beta | Prove the social-to-order loop. | One validated channel, unified inbox, human reply/takeover, constrained AI, RAG, order creation from conversation and operational dashboard. | Unvalidated channels, TikTok, carrier integrations and subscription collection. |
| MVP Launch | Safely onboard pilot sellers. | Entitlements/usage, alerts/runbooks, receipt notifications, backup restore, security/abuse review and launch readiness. | Numeric SLA claims and advanced analytics. |
| Phase 2 | Expand validated demand. | Second/third channels, eSewa/Khalti after KYC validation, custom domains, reports, exports, payment/fee ledger and richer knowledge lifecycle. | Marketplace, POS, autonomous campaigns and multi-region HA. |
| Phase 3 | Scale and differentiate. | Advanced fulfillment/returns, provider-aware AI routing, carrier integrations, customer accounts, controlled promotions and enterprise controls. | Features without demonstrated seller demand. |

**Sequencing guardrails**

1. No AI write tool exists before the underlying command, authorization, idempotency and audit trail are production-tested.
2. No live provider is marketed until sandbox and production credentials, webhook verification, policy compliance and failure handling are proven.
3. No payment method is “paid” from browser input or text alone; it needs a verified manual action or a signed provider event.
4. No custom domain, payment gateway or multi-channel work interrupts the first working subdomain storefront/order loop.
5. Every milestone ends with an acceptance demonstration using a fresh tenant and test data, then a regression suite run.

### 11.2 Milestone roadmap

Time estimates are intentionally omitted: team size, provider approval and payment onboarding are larger schedule determinants than coding effort. Complete milestones in order; some validation work can run in parallel but cannot be skipped.

| # | Milestone | Deliverable | Definition of done |
|---:|---|---|---|
| 0 | Product and provider readiness | Decision record, pilot-seller profile, provider/payment validation checklist, environment owner. | Frontend/identity/hosting choices are recorded; one social-provider candidate and COD/QR operating policy are validated with real stakeholders. |
| 1 | Repository and delivery foundation | Monorepo, .NET solution, Next.js app(s), Docker/dev dependencies, CI, staging shell. | A clean checkout builds/lints/tests; configuration is typed; no secrets committed; health/readiness and structured logs work. |
| 2 | Tenant identity and workspace | Identity, memberships/RBAC, tenant context, audit primitives, seller workspace shell. | Owner can create a tenant; unauthorized tenant data is inaccessible through API and UI; worker/job tenant tests pass. |
| 3 | Catalog and inventory | Product/variant/media, stock ledger, reservations and seller catalog UI. | Concurrent reservations cannot oversell; every adjustment/reservation is auditable; only valid published variants are purchasable. |
| 4 | Storefront and checkout | Platform-subdomain public store, cart, quote, address, delivery, COD/QR and canonical orders. | A new tenant can configure, publish and complete an order; order snapshots cannot change after confirmation; expired checkout releases stock. |
| 5 | Order operations and notifications | Seller order workspace, fulfilment transitions, manual QR verification, confirmations/receipts. | Seller can process/cancel an order with recorded reason/actor; no invalid payment/fulfilment transition is possible. |
| 6 | Integration runtime and inbox | Provider-neutral connection/event/outbox model plus one validated channel adapter and inbox. | Duplicate inbound events do not duplicate records; failures are observable/replayable; staff reply works end to end. |
| 7 | Constrained AI assistant | Assistant config, knowledge, tool runner, handoff and evaluation suite. | AI answers only from tools/approved knowledge, never replies after takeover, and all traces are auditable/redacted. |
| 8 | Dashboard, entitlements and launch operations | Basic metrics, usage limits, onboarding completion, alerts/runbooks, backup restore and pilot controls. | Pilot tenant’s sales/chat/usage are visible; limit behavior is defined; staging rehearsal, restore test and incident drill pass. |
| 9 | Pilot launch and learning loop | 3–10 selected sellers, support cadence and measured feedback. | Pilot success criteria are reviewed; defects are triaged; Phase 2 is shaped by observed demand rather than assumption. |

### 11.3 Milestone acceptance checklist

| Area | Mandatory checks before MVP Beta/Launch |
|---|---|
| Tenant safety | API, job, cache, storage and hostname tests show no cross-tenant access; support access is audited. |
| Catalog/inventory | Product/variant mutation is authorized; stock movements reconcile; reservation expiration and concurrent purchase tests pass. |
| Orders/payments | Order totals/delivery/payment snapshots are immutable; COD/QR status needs explicit authorized action; cancellation releases appropriate stock. |
| Storefront | Tenant is resolved from trusted host/slug; browser cannot alter price/stock/fees; checkout succeeds on mobile viewport. |
| Webhooks | Signature is checked; event/message uniqueness holds; ack is independent of AI latency; retry/DLQ/replay is demonstrated. |
| AI | Tool schemas validate; read vs write permissions are enforced; cross-tenant knowledge is impossible; handoff is immediate; adversarial/evaluation cases pass. |
| Security | Secrets are encrypted/not logged; role checks and rate limits work; PII has documented retention/export/deletion handling. |
| Operations | Staging deployment/migration/release procedure is rehearsed; alerts fire; backup restore succeeds; on-call/support owner is named. |

### 11.4 Test strategy

| Level | Focus | Examples |
|---|---|---|
| Unit | Domain invariants and pure application policies. | Order state transition, fee calculation, reservation expiry, plan quota evaluation, tenant authorization policy. |
| Integration | Database, transactions, EF filters, outbox and worker behavior. | Simultaneous stock reservations; unique provider IDs; tenant-scoped raw/projection query; payment callback idempotency. |
| Provider contract | Each channel/payment adapter against sandbox fixtures or contract tests. | Webhook signature/normalization, outbound request mapping, retryable vs terminal error classification. |
| API | Auth, input validation, error contract, pagination/idempotency. | Cross-tenant forbidden results, duplicate POST idempotency key, invalid state transition problem response. |
| End-to-end | Seller and customer critical paths. | Onboard → publish product → checkout COD/QR → seller fulfils; inbound chat → bot/human takeover → order draft. |
| AI evaluation | Grounding, safety, language and handoff. | Nepali/English/Romanized-Nepali price/stock/delivery questions; unavailable variant; prompt injection; refund complaint. |
| Load/failure | Measured capacity and recovery. | Webhook burst, provider timeout, worker restart, Redis outage posture, reservation contention and replay backlog. |
| Security/operations | Abuse and deploy safety. | Rate-limit, secret scanning, dependency/vulnerability review, backup restore and migration rollback/forward rehearsal. |

Run unit/lint/type checks on every pull request; integration/provider/API suites on merge to staging; end-to-end plus migration and security gates before production release. Use anonymized/synthetic data outside production.

### 11.5 Deployment path and production release procedure

```text
Developer machine
  → pull request (lint/unit/build)
  → staging deploy (integration/API/E2E + migration rehearsal)
  → approval + controlled production migration
  → production API/worker release
  → smoke test + metric/alert watch
```

1. Build immutable frontend/API/worker artifacts; produce SBOM or dependency inventory where practical.
2. Run database migrations as a separate, observable release task. Use backward-compatible expand/contract migrations; never rely on every API instance migrating itself at startup.
3. Deploy API and workers with independent health/readiness checks; verify background processing does not start against an incompatible schema.
4. Smoke-test an isolated production-like tenant: seller sign-in, public product, checkout quote, order creation and provider health endpoint.
5. Observe errors, webhook lag, outbox/DLQ and checkout failure rate during the release window. Roll back application artifacts if required; forward-fix data migrations according to the documented migration plan.

**Minimum launch runbooks:** provider webhook failure/replay, outbound-message failure, stuck reservation, manual QR dispute, payment callback mismatch, inventory mismatch, tenant-access incident, database restore, secret/token rotation, and production rollback.

### 11.6 Pilot and launch checklist

**Before pilot sellers**

- Define the pilot segment, support contact, working hours, feedback loop and success metrics: setup completion, first product published, first order, chat-to-order completion, AI escalation rate, order failure rate and operator time saved.
- Validate channel access/approval and merchant eligibility with the actual seller account—not a generic integration assumption.
- Test delivery wording/prices, COD policy, QR verification instructions, return/cancellation policy and customer communications in Nepali/English.
- Prepare terms, privacy notice, seller agreement and support escalation path; obtain local advice for payment, KYC, consumer and VAT obligations.
- Seed a demo tenant and execute the launch acceptance checklist on a clean tenant.

**Before public launch**

- Complete security, dependency, rate-limit and PII/logging review.
- Confirm backups, restore drill, monitoring/alerts, incident ownership and support/admin access procedure.
- Verify payment and channel provider production credentials, webhook URLs, secret rotation dates and support contacts.
- Confirm no marketing claim overstates supported channels, payment methods, uptime, security certification or automation capabilities.
- Establish a defect severity process and product feedback cadence; make it easy to disable a provider connection or AI automation per tenant.

### 11.7 Authoritative build prompt pack

Use one prompt at a time after its prerequisites are met. Each prompt assumes the blueprint in Sections 10–11 is authoritative; generated code must preserve existing work, use the established repository conventions, and add/adjust tests. Do not ask the coding agent to generate unbounded scaffolding or choose product scope independently.

#### Prompt 0 — Repository and engineering baseline

> Implement Milestone 1 of the Kreyora blueprint. Create the monorepo topology in Section 10.2: Next.js TypeScript seller/public web surfaces, .NET 10 Domain/Application/Infrastructure/WebApi solution, test projects, Docker Compose development dependencies (PostgreSQL, Redis, object-storage emulator if chosen), environment templates and CI checks. Add typed configuration validation, structured logging with correlation IDs, liveness/readiness endpoints, OpenAPI, consistent problem responses, formatting/linting and a minimal smoke test. Do not add business modules or production secrets. Document local start/test commands and verify a clean checkout builds, lints and tests.

#### Prompt 1 — Tenant identity, RBAC and audit foundation

> Implement Milestone 2. Use ASP.NET Core Identity with Tenant, Membership and role/policy-based authorization (`Owner`, `Admin`, `Operator`, `Viewer`, audited `PlatformSupport`). Enforce tenant context from authenticated membership; create explicit contracts for webhook/job/storefront contexts but do not trust an arbitrary tenant header. Add EF tenant filtering as defense in depth, SaveChanges tenant enforcement, audit-event primitives and integration tests proving no cross-tenant API/database/job access. Create the Next.js seller workspace shell and protected sign-in/session flow. Include migrations, API docs and tests.

#### Prompt 2 — Catalog, media and inventory correctness

> Implement Milestone 3. Create Catalog and Inventory modules with Product, ProductVariant, product media references, publication state, inventory balance, append-only StockMovement and expiring InventoryReservation. Implement commands/queries for seller product management, stock adjustment, reserve/release/commit inventory and published catalog reads. Use PostgreSQL transactions/concurrency controls so concurrent reservation attempts cannot oversell. Every aggregate is tenant-scoped and audited. Add seller catalog/inventory pages and media upload authorization contract (not a public bucket). Write unit/integration tests for isolation, stock reconciliation, duplicate idempotency keys, expiry and high-contention reservation.

#### Prompt 3 — Storefront, delivery, checkout and canonical orders

> Implement Milestone 4. Build a platform-subdomain/slug tenant storefront with published product pages, mobile cart and checkout. Add Store profile/readiness, DeliveryRule, customer contact/address capture, COD and merchant QR payment-method configuration, quote generation and canonical Order/OrderItem snapshots. Separate OrderStatus, PaymentStatus and FulfilmentStatus exactly as Section 10.4 specifies. Recalculate price, stock, delivery and fees server-side; create short reservations during checkout and release them safely on expiry/failure. Build public API endpoints and Next.js pages. Add E2E and integration tests showing a new tenant can publish, order and not alter totals/stock from the browser.

#### Prompt 4 — Seller order operations and notifications

> Implement Milestone 5. Add seller order dashboard/detail workflows, allowed fulfilment/cancellation transitions, manual QR payment verification/rejection, COD collection recording, order event audit and receipt/confirmation notification outbox. Ensure payment state cannot be set to paid without authorized manual verification or later provider evidence; cancellation and reservation/allocation behavior must be transactional and tested. Implement notification provider abstraction with a safe local/test implementation and delivery log. Add operator authorization, API tests and E2E seller workflow coverage.

#### Prompt 5 — Integration runtime, one channel and unified inbox

> Implement Milestone 6. Create provider-neutral Integration and Conversation modules: encrypted ChannelConnection secrets, capabilities, immutable WebhookEvent, normalized inbound events, Conversation/Message, outbound message outbox/delivery attempts, retries, DLQ/replay and connection health. Implement `IChannelProvider` per Section 10.6, then implement exactly one provider adapter only after its current documented sandbox/production requirements are supplied. Webhook endpoints must validate signatures, persist idempotently, acknowledge quickly and process asynchronously. Build inbox list/detail/reply/takeover UI. Add provider contract fixtures and tests for duplicate events/messages, provider failure, replay and tenant isolation.

#### Prompt 6 — Constrained AI assistant and seller knowledge

> Implement Milestone 7. Add tenant assistant configuration, approved KnowledgeDocument lifecycle, retrieval interface, AI provider abstraction, tool registry and redacted AIActionLog. Implement read tools (product, inventory, price, delivery, order status) and schema-validated controlled writes (quote, order draft, reserve/release, checkout link, escalation). Catalog/price/inventory/order data must always be retrieved through application tools, never RAG. Enforce tool-loop/cost/time budgets, entitlement checks, conversation automation state and immediate human takeover. Create an offline evaluation harness with Nepali, English and Romanized-Nepali cases, unavailable products, ambiguity, prompt injection and escalation; tests must prove no message is sent after takeover.

#### Prompt 7 — Dashboard, onboarding, entitlements and operational controls

> Implement Milestone 8. Add a resumable seller onboarding/readiness checklist that gates storefront/AI activation. Implement versioned Plan, Entitlement and UsageEvent models with manual plan assignment, feature checks and 70/90/100% usage notifications; do not implement subscription collection. Add operational dashboard projections for setup status, orders/revenue, open chats, reply time, low stock and usage. Instrument metrics/tracing for webhooks, jobs, AI, checkout and integration health. Add admin-safe replay/diagnostics, backup/restore documentation, alert/runbook documentation and staging acceptance scripts. Include tests for deterministic quota enforcement and full fresh-tenant onboarding to first order.

#### Prompt 8 — Production hardening and pilot release

> Implement the launch gate, not new features. Audit the current implementation against Sections 10.11–10.12 and 11.3–11.6. Add missing security headers/rate limits/input constraints, secret/log redaction, retention/export/delete policy hooks, migration release controls, backup restore test, alert definitions, failure runbooks, CI staging gates, load/failure tests and feature kill switches for provider connections/AI automation. Execute and document a clean-tenant end-to-end smoke path: onboarding, publish, COD/QR order, staff order processing, inbound provider test event, human takeover and AI evaluation. Report any unmet external validation gates rather than faking compliance or provider capability.

### 11.8 Completion criteria for the planning phase

Planning is complete because this file now contains: the public-product extraction (Step 1), normalized source-plan audit (Step 2), explicit alignment/MVP decision (Step 3), build-ready architecture (Step 4), and ordered implementation/prompt/launch plan (Step 5).

**First implementation action:** create a short decision record for the selected initial social provider, initial AI provider, hosting/region and payment operating policy, then execute Prompt 0. Do not start a later prompt until the previous milestone’s Definition of Done is demonstrably satisfied.

---

## Appendix A — Current document map (for reference)

```
Kreyora/
├── Kreyora.docx                          # master architecture + 10 prompts
├── Divided Plans/
│   ├── AI_Social_Commerce_Project_Plan.docx
│   ├── Deployment_Strategy_and_Infrastructure_Plan.docx
│   └── AI_App_Building_Prompts_Step_By_Step.docx
├── _extracted/                             # text extracts for analysis
│   ├── Project_A.txt
│   ├── AI_Social_Commerce_Project_Plan.txt
│   ├── Deployment_Strategy.txt
│   └── AI_App_Building_Prompts.txt
└── plan.md                                 # THIS FILE (living master plan)
```

## Appendix B — Saney pricing snapshot (for quota design later)

| Plan | Price (monthly) | Products | AI credits | Orders/mo | Social integrations | Service charge |
|---|---:|---:|---:|---:|---:|---:|
| Free | NPR 0 | 25 | Not publicly specified | Not publicly specified | Not publicly specified | Not publicly specified |
| Grow | NPR 2,499 | 300 | 200 | 200 | 3 | 3.5% (2% + 1.5%) |
| Scale | NPR 3,499 | 800 | 500 | 500 | All | 3% (2% + 1%) |
| Elevate | NPR 5,499 | 2300 | 1300 | 1700 | All | 2.75% (2% + 0.75%) |

Yearly option: **2 months free**.  
All service charges exclusive of VAT.

## Appendix C — Our planned AI tools (must remain DB-backed)

- `SearchProducts`
- `CheckInventory`
- `GetPrice`
- `GetShippingInfo`
- `CreateOrder`
- `ReserveInventory`
- `EscalateToHuman`

## Appendix D — Our planned core entities

Seller, SocialChannel, Product, ProductVariant, Inventory, InventoryReservation, Customer, Conversation, Message, Order, OrderItem, KnowledgeDocument, AIActionLog

**Entities added to the authoritative blueprint (Section 10):**
Storefront/StoreSettings, Domain, PaymentMethod, PaymentAttempt, PaymentTransaction, Settlement, Subscription/Plan/Entitlement/UsageEvent, DeliveryRule, Membership, MediaAsset, WebhookEvent, OutboxEvent, Notification, AuditEvent, StockMovement and Refund (Phase 2).

---

**END OF EXECUTION FRAMEWORK — PLANNING COMPLETE**  
Next action: record Milestone 0 decisions, then execute Prompt 0 in Section 11.7.
