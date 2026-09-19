# ADR-010 — Social Connection Ownership and Multi-Tenant Routing

- **Status:** `Accepted`
- **Date:** 2026-09-20
- **Owner:** Platform Architect
- **Reviewers:** Project Owner, Engineering Team
- **Affected milestones:** Milestone 07, Milestone 08, Milestone 09

## Context

Kreyora is a multi-tenant social-commerce operating system where each tenant represents a merchant business organization. A tenant can operate one or more public storefronts (`Store`). As part of Milestone 07 (Social Integration Runtime) and Milestone 08 (First Social Channel and Inbox), merchants connect external social media accounts (e.g., WhatsApp Business, Instagram Professional, Facebook Messenger, Viber Bot, Telegram Bot) to receive customer messages, automate replies via AI, and capture orders.

We must decide at which hierarchy level social connections (`ChannelConnection`) are owned:
1. Strictly at the tenant level.
2. Strictly at the individual store level.
3. At the tenant level with an optional store binding (hybrid).

## Decision

`ChannelConnection` entities are owned by `TenantId` (mandatory) with an optional `StoreId` foreign key binding.

1. **Mandatory Tenant Scoping:** Every connection strictly belongs to a single tenant (`TenantId` is required, indexed, and enforced via EF Core global query filters). No cross-tenant access is permitted.
2. **Optional Store Binding:**
   - When `StoreId` is `null`, the connection is tenant-wide. Inbound inquiries are routed to a shared tenant inbox, and the catalog-aware AI assistant uses tenant-wide storefront availability or prompts the customer if multiple active storefronts exist.
   - When `StoreId` is specified, the connection is bound to that specific storefront. Inbound conversations, catalog context, and orders created from this channel automatically bind to that store.
3. **Webhook Ingress Resolution:** Inbound webhooks resolve the connection via external channel identifier or URL route (`/webhooks/{provider}/{connectionId}`), establishing the verified `TenantId` and optional `StoreId` context.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Strictly Store-owned (`TenantId` + `StoreId` mandatory) | Direct 1:1 mapping between store and social channel. | Merchants with one official WhatsApp number cannot use it across multiple storefronts within the same workspace; requires duplicate connections. | Rejected. Overly restrictive for multi-brand or multi-branch merchants operating under one tenant. |
| Strictly Tenant-owned (no `StoreId`) | Simple connection model; all channels belong to the tenant. | Cannot isolate a dedicated channel (e.g. specialized Instagram account) to a specific storefront; complicates order creation defaults. | Rejected. Fails to support merchants operating multiple distinct brand storefronts with separate social handles. |
| **Tenant-owned with optional Store binding (Chosen)** | Maximum flexibility. Supports both single-store tenants (default) and multi-store tenants with dedicated or shared channels. | Application logic must handle both bound and unbound connection routing. | Accepted. Best alignment with Kreyora's multi-tenant architecture and real-world merchant operations. |

## Consequences

- **Product impact:** Merchants can connect a single WhatsApp number for their entire organization or bind specific Instagram accounts to specific stores.
- **Architecture impact:** `ChannelConnection` entity contains required `TenantId` and nullable `StoreId`. Inbound normalization preserves both.
- **Security/privacy impact:** Strict tenant isolation is preserved; webhook routing resolves tenant context without trusting client headers.
- **Cost/operations impact:** Clean database schema with composite index `(TenantId, Provider, ExternalAccountId)`.
- **Migration or rollback impact:** Additive schema change; existing stores and tenants remain fully compatible.

## Validation evidence

- Unit and integration tests verify that tenant context is mandatory and that store binding is optional.
- Webhook resolution integration tests confirm that inquiries on store-bound connections automatically inherit store context, while tenant-wide connections resolve cleanly.

## Supersession conditions

- Introduction of enterprise multi-subsidiary organizational units requiring hierarchical channel sharing beyond a single tenant.

