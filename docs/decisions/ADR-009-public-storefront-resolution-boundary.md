# ADR-009 — Public storefront resolution boundary

- **Status:** Accepted
- **Date:** 2026-09-05
- **Owner:** Project owner
- **Affected milestone:** M05-S05

## Context

M05-S01 through M05-S04 created tenant-owned stores, published catalog facts, quotes, checkout sessions, and canonical orders. Those services currently operate only after a verified seller or system tenant context. Public storefront callers have neither an authenticated membership nor authority to select a tenant through an HTTP header.

The browser must reach exactly one active, purchase-ready Store without being able to forge a tenant ID, host mapping, forwarded-host value, or route slug. The public API also needs a local-development route that does not weaken production host validation.

## Decision

Introduce a dedicated public-storefront request context. It is resolved before a public controller executes and provides only the verified `TenantId`, `StoreId`, and normalized platform slug needed for tenant query filters and existing commerce services. It carries no user, membership, role, or seller permission.

- Production public endpoints resolve only `slug.platformBaseDomain` from `HttpRequest.Host`, after ASP.NET Core forwarded-header processing is restricted to configured trusted proxies. A host must contain exactly one slug label above the configured platform base domain. IP literals, the apex domain, multiple labels, arbitrary ports as identity, and untrusted `X-Forwarded-Host` values do not resolve a Store.
- Development and Testing may expose a separately named, explicitly configured route such as `/public/v1/dev/stores/{slug}/...`. It is disabled outside those environments and is never selected by a request header.
- The resolver performs the initial global Store lookup without tenant query filters, then verifies `Active` status and current purchase readiness before it opens the public tenant scope. All later queries use that scope plus an explicit Store/publication predicate.
- Missing, inactive, unready, malformed, and foreign storefront selections return the same privacy-safe public not-found response. The response exposes no tenant ID, Store ID, readiness blocker, or distinction that can support enumeration.
- Public writes are limited to quote, checkout-session, and COD canonical-order creation. They use existing service contracts, bounded request DTOs, correlation IDs, configured rate limits, request-size limits, and idempotency keys. Merchant QR remains unavailable until M06 establishes real merchant configuration and proof handling.

## Consequences

- Anonymous public commerce can reuse the authoritative M05 services without copying price, inventory, reservation, checkout, or order logic into controllers.
- A verified public scope is not a seller membership. It must not satisfy seller authorization policies, provide seller read APIs, or authorize arbitrary tenant writes.
- Public cache keys must vary by resolved storefront identity and must never include the tenant header. Writes and customer/contact responses are `no-store`; only published read projections and public media receive explicitly bounded cache directives.
- The boundary requires additive Web API options, middleware/endpoint metadata, dedicated public read/projection services, controller contracts, OpenAPI regeneration, and host/tenant-isolation coverage. It does not require a new database table or a custom-domain state machine.

## Alternatives considered

| Alternative | Decision |
|---|---|
| Accept `X-Kreyora-Tenant-Id` or a browser tenant field | Rejected: any caller could select another tenant. |
| Reuse seller tenant middleware without an authenticated identity | Rejected: it conflates anonymous public routing with membership authorization. |
| Resolve every environment from `{slug}` routes | Rejected: production platform-subdomain routing and host-confusion protections would remain unproven. |
| Add custom domains now | Rejected: DNS verification/TLS provisioning is a later state machine and outside M05-S05. |
