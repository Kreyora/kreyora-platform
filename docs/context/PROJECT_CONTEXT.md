# Kreyora — Project Context

> Derived from `design_files/plan.md`, `design_files/Divided Plans/AI_Social_Commerce_Project_Plan.md`, and `design_files/project_a_milestones/00_MASTER_INDEX.md`. This is a navigation summary, not a replacement for the authoritative sources.

## Product definition

Kreyora is a Nepal-focused, multi-tenant social-commerce operating system for independent sellers. It converts social-channel enquiries (Facebook, Instagram, WhatsApp, and later TikTok) into reliable orders through a catalog-aware AI assistant, a unified seller inbox, a branded public storefront, local-first payment options, and operational order/inventory control.

**Positioning:** "The reliable commerce layer behind a seller's social DMs." The product makes small sellers more responsive, organized, and credible while keeping them in control.

**Core principle:** AI is not the source of truth. Products, prices, inventory, delivery fees, payment status, and orders are application/database facts. AI reads those facts through controlled tools or executes explicitly authorized, validated actions.

## Primary users

| Persona | Job |
|---|---|
| Seller owner | Launch and operate a professional social shop without building a website or manually answering every DM. |
| Seller operator | Resolve conversations and fulfil orders using one inbox; take over AI when needed. |
| Customer | Ask in a familiar channel and confidently buy with clear price, delivery, and payment choices. |
| Platform operator | Support tenants and integrations safely with audited, tenant-scoped access. |

## MVP workflow

```
signup → workspace → catalog → storefront publication → social enquiry
→ safe AI or human reply → stock-safe order → COD/QR processing
→ fulfilment → dashboard and audit evidence
```

## Selected architecture

- **Frontend:** Next.js + strict TypeScript (seller workspace + public storefront)
- **Backend:** ASP.NET Core .NET 10 modular monolith
- **Data:** PostgreSQL + EF Core with mandatory tenant scoping
- **Identity:** ASP.NET Core Identity, memberships, policy-based RBAC
- **Jobs:** Hangfire with PostgreSQL storage (Redis optional, not MVP)
- **Files:** S3-compatible storage, initially Cloudflare R2
- **Deployment:** Docker, .NET Aspire locally, GitHub Actions/GHCR
- **AI:** Provider-neutral orchestration with tool-calling and tenant-scoped RAG

See `design_files/plan.md` Section 10.2 for the full locked stack.

## Security and AI principles

- Tenant isolation enforced at every layer (API, database, cache, jobs, storage, search).
- Policy-based RBAC with Owner, Admin, Operator, Viewer, and audited PlatformSupport roles.
- Webhook signature verification, idempotency, and replay protection.
- PII minimization, secret encryption, and log/trace redaction.
- AI tools are constrained: read tools for catalog/inventory/price/delivery/order; controlled write tools for quotes/drafts/reservations with validation.
- Human takeover immediately stops AI automation with an audit event.
- No AI-generated commerce facts; catalog, price, stock, payment, and order data come only from application tools.

## MVP boundaries

### Included
- One production-validated social channel adapter
- Platform-subdomain storefront (no custom domains in MVP)
- COD and merchant QR/manual payment verification
- Manual plan assignment with entitlement/usage visibility
- Operational dashboards, audit, alerts, and runbooks

### Explicitly deferred
- TikTok integration (until API eligibility confirmed)
- Custom domains and SSL lifecycle
- eSewa/Khalti gateway payments (until KYC/settlement validated)
- Automated subscription billing
- Carrier integrations
- Advanced promotions/returns
- Image/OCR product matching
- Autonomous campaigns
- Multi-region HA
- Formal compliance certifications

## Current implementation state

- **Phase:** Implementation
- **Active milestone:** 01 — Frontend Showcase
- **Active step:** 01 — Route inventory, design system, and mock architecture
- **Status:** REVIEW (awaiting approval, amendments applied)
- **Repository:** pnpm monorepo. `apps/web/` contains Next.js 16 application with design system (Inter + Noto Sans Devanagari), 20 UI components (6 Radix-based), 13 mock adapters, 41 route placeholders across 4 route groups, 92 passing tests
