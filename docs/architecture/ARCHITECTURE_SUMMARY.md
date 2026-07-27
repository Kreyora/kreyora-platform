# Architecture Summary

> Navigation document. `docs/plan/plan.md` remains the authoritative architecture reference, especially Sections 10 and 11.

## System surfaces

| Surface | Technology | Purpose |
|---|---|---|
| Seller workspace | Next.js + TypeScript | Authenticated seller dashboard, catalog, orders, inbox, settings |
| Public storefront | Next.js + TypeScript | Tenant-branded product pages, cart, checkout, order lookup |
| API | ASP.NET Core .NET 10 | REST endpoints, webhooks, background jobs |
| Worker | Hangfire (co-hosted or separate) | Async jobs: reservation expiry, outbox, notifications, integration events |

## Backend pattern

- **Controllers:** Traditional `[ApiController]` classes. Thin dispatchers — inject service interfaces, return results.
- **Service layer:** Application-layer service interfaces (e.g., `IOrderService`, `ICatalogService`) define business operations. Implementations in Infrastructure orchestrate domain logic, repositories, and external providers.
- **No MediatR/CQRS:** Business operations use direct service calls, not command/query handler pipelines. See ADR-001.
- **Dependency direction:** `Domain → Application → Infrastructure → WebApi` (locked).

## Module boundaries

Modules own their domain rules. Cross-module communication uses stable query contracts, domain/integration events, or explicit application orchestrators — never direct repository access.

| Module | Owns | See plan.md |
|---|---|---|
| Tenancy & Identity | Tenant, User, Membership, Role, audit context | §10.5 |
| Catalog | Product, Variant, media refs, collections, price | §10.3, §10.4 |
| Inventory | Stock ledger, balance, reservations, allocation | §10.4 |
| Storefront | Store profile, domain config, published projection, checkout session | §10.8 |
| Customers | Customer profile, channel identities, addresses, consent | §10.3 |
| Conversations | Conversation, Message, assignment, labels, automation state | §10.4, §10.6 |
| Integrations | Channel connections, webhook events, provider capabilities | §10.6 |
| AI Assistant | Config, knowledge sources, action trace, evaluation | §10.7 |
| Orders | Order, items, totals, delivery snapshot, fulfilment workflow | §10.4 |
| Payments | Payment method, attempt, transaction, proof, refund, settlement | §10.9 |
| Billing | Plan, entitlement, usage event, subscription | §10.9 |
| Notifications | Request, delivery log, templates | §10.3 |
| Reporting | Immutable metric events, read models | §10.3 |

## Data ownership

- PostgreSQL with EF Core. Schema-per-application, shared tables with mandatory `TenantId`.
- Every tenant-owned table has `TenantId` and composite indexes for common reads.
- Append-only records: StockMovement, UsageEvent, AuditEvent, WebhookEvent, payment transactions, outbox.
- Financial/order snapshots are immutable — later catalog changes cannot rewrite commercial history.
- See `docs/plan/plan.md` §10.10 for table/index details.

## Tenant boundary

- `TenantId` on every tenant-owned entity, query, cache key, job payload, storage path, and provider event.
- Context resolved from: authenticated membership (seller API), connection identifier (webhooks), persisted tenant ID (jobs/outbox), verified hostname (storefront).
- EF query filters as defense-in-depth; explicit ownership verification on every command.
- Object storage paths use tenant prefix; cache keys include tenant.
- See `docs/plan/plan.md` §10.5.

## Primary state machines

| Entity | States | See plan.md |
|---|---|---|
| Order | draft → awaiting_customer → pending_confirmation → confirmed → processing → fulfilled; cancelled (before fulfilment) | §10.4 |
| PaymentStatus | not_required, pending, awaiting_verification, authorized, paid, failed, refunded, partially_refunded | §10.4 |
| FulfilmentStatus | unfulfilled, ready, dispatched, delivered, failed, cancelled | §10.4 |
| Reservation | active → committed \| released \| expired | §10.4 |
| Conversation | new → bot_active ↔ human_assigned → awaiting_customer → checkout_in_progress → order_created → resolved; closed, spam | §10.4 |
| Store domain | requested → dns_pending → verified → provisioning_tls → active \| failed | §10.8 |

## Integration event flow

```
Provider webhook → signature verification → persist immutable WebhookEvent
→ acknowledge promptly → worker normalizes → upsert customer/conversation/message
→ domain event → AI invocation (if automation active + entitlement permits)
→ outbound response via outbox → provider send worker → delivery result/retry
```

See `docs/plan/plan.md` §10.6 for `IChannelProvider` contract and reliability design.

## AI tool/RAG boundary

- **Tools (authoritative):** SearchProducts, CheckInventory, GetPrice, GetShippingInfo, GetOrderStatus (read); QuoteCart, CreateOrderDraft, ReserveInventory, ReleaseReservation, CreateCheckoutLink, EscalateToHuman (controlled write).
- **RAG (supplementary):** Seller-controlled FAQ, shipping, returns, brand guidance documents only.
- Catalog, inventory, pricing, delivery, payment, and order data always come from tools, never RAG.
- Tool loops have iteration, cost, and time budgets.
- Human takeover immediately stops AI with an audit event.
- See `docs/plan/plan.md` §10.7.

## Deployment topology

### Local development
.NET Aspire AppHost orchestrates: API + Hangfire, Next.js, PostgreSQL, optional Redis.

### Pilot production
One VPS with Docker Compose: Caddy, Next.js, ASP.NET Core API + Hangfire, PostgreSQL. Cloudflare DNS/CDN/TLS and R2 object storage.

### Growth
Separate API/worker hosts, managed PostgreSQL, Redis, expanded CDN.

See `docs/plan/divided/Deployment_Strategy_and_Infrastructure_Plan.md` for full topology.
