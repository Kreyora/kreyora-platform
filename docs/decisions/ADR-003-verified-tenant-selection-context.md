# ADR-003 — Verified tenant selection context

- **Status:** Accepted
- **Date:** 2026-08-01
- **Owner:** Project owner
- **Reviewers:** Codex
- **Affected milestones:** M03-S03 through M03-S06

## Context

Seller users can belong to more than one workspace and may use those workspaces in separate browser tabs. `docs/plan/plan.md` §10.5 requires seller tenant context to come from authenticated membership and prohibits trusting an arbitrary tenant header. M03-S03 therefore needs a per-request selection mechanism that is convenient for the browser but cannot become authority by itself.

## Decision

Tenant-scoped seller endpoints require `X-Kreyora-Tenant-Id`. The header is a selection hint only: the API resolves it on every request against the authenticated Identity user, an active membership, and an active tenant before creating the scoped `TenantContext`. Missing or malformed selection returns `400`; a foreign, revoked, suspended, or inactive workspace returns `403` without revealing tenant data.

`TenantContext` is scoped and disposed after every request and durable operation. Background jobs and tenant outbox processing use a persisted tenant ID and resolve an active tenant before execution; they never accept a request header.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Verified request header | Independent workspace selection per browser tab; no session mutation | Every tenant-scoped request must include the header | Chosen because membership verification preserves the trust boundary. |
| Session-stored active workspace | Simple client requests | Switching one tab changes other tabs; hidden mutable server state | Rejected for multi-workspace workflows. |
| Tenant in seller URL | Explicit/shareable route state | Larger route contract and IDOR surface; does not remove membership verification | Deferred unless product navigation later requires canonical workspace URLs. |

## Consequences

- **Product impact:** Future seller clients list active workspaces, then include the selected tenant ID on tenant-scoped API requests.
- **Architecture impact:** Tenant-owned persistence reads/writes use the scoped context; EF filters are defense in depth and raw SQL must still include explicit tenant predicates.
- **Security/privacy impact:** A forged header cannot establish access. Context is cleared after request/job completion to prevent reuse.
- **Cost/operations impact:** No provider or infrastructure cost. Future Hangfire handlers must use the supplied tenant job runner.
- **Migration or rollback impact:** `outbox_messages.tenant_id` is non-null. Legacy M02 rows receive an empty value during the expand migration and are intentionally inaccessible through tenant processing; no synthetic system tenant is created.

## Validation evidence

- Unit tests cover nested context cleanup and tenant-prefixed key generation.
- PostgreSQL integration tests cover active/inactive membership resolution, query filtering, raw SQL projections, write enforcement, jobs, and outbox context cleanup.
- Middleware tests cover missing and forged selection plus request-scope cleanup.

## Supersession conditions

Revisit when seller workspace navigation moves to canonical tenant URLs, when an external OIDC gateway supplies a trusted tenant claim, or when a separate service boundary requires a signed internal tenant-context credential.
