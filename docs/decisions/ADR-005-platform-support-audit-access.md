# ADR-005: Owner-issued read-only PlatformSupport audit access

- **Status:** Accepted
- **Date:** 2026-08-02
- **Owner:** Project owner
- **Reviewers:** Codex
- **Affected milestones:** M03-S04 through M03-S06

## Context

M03 requires audited PlatformSupport without creating a hidden cross-tenant role. Seller workspace authority is normally established by an active tenant membership (ADR-003), while PlatformSupport is a global Identity role and must not gain a tenant membership or default workspace access.

## Decision

Only an active tenant Owner may create or revoke a `SupportAccessGrant` for a user who currently has the global `PlatformSupport` Identity role. A grant requires a reason, expires within eight hours, cannot overlap an active grant for the same support user and tenant, and can be revoked at any time.

The resolver validates the global role, active tenant, unexpired/unrevoked grant, and absence of a tenant membership for that support user on every request. It establishes a marked read-only support context only for audit-history access. PlatformSupport has no default tenant access and cannot write tenant-owned data. Grant creation, revocation, and support audit-history access are appended to the tenant audit trail.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Tenant membership with `PlatformSupport` role | Reuses membership flow | Permanently broadens tenant access and violates the support boundary | Rejected. |
| Global support access to every tenant | Fast incident response | Unacceptable tenant/privacy exposure | Rejected. |
| Owner-issued, time-bounded read-only grant | Explicit consent, live validation, revocation, auditability | Owners must create grants for support work | Chosen. |

## Consequences

- **Product impact:** Support staff can inspect audit evidence only after an Owner explicitly grants time-bounded access.
- **Architecture impact:** Support context is distinct from membership context and all authorization uses the same permission matrix.
- **Security/privacy impact:** No cookie claim or client header conveys support authority; tenant-owned writes are blocked in support context.
- **Cost/operations impact:** No provider or infrastructure dependency is added.
- **Migration or rollback impact:** `support_access_grants` and append-only `audit_events` are introduced by `AddPolicyRbacAndAuditEvents`; rollback drops only those new tables.

## Validation evidence

- Unit tests cover the role matrix, Owner boundary, support default denial, eight-hour limit, and metadata redaction.
- PostgreSQL/Testcontainers coverage verifies migration application, grants, live support resolution, audit pagination, and append-only enforcement.
- Authenticated endpoint tests verify unauthenticated and insufficient-role denial plus verified context resolution.

## Supersession conditions

Revisit only if a separately approved support operations model requires documented break-glass access, delegated support organizations, or a distinct service boundary with signed internal tenant context.
