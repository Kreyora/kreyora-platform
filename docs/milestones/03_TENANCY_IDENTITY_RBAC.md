# Milestone 03 — Tenancy, Identity, RBAC, and Audit

## Objective

Implement the security and ownership boundary for every later Kreyora feature. Replace mock seller authentication and workspace selection with real identity, tenant membership, policy authorization, trusted tenant resolution, and immutable audit events.

## Dependencies

- Milestone 02 exit gate approved.
- Approved roles: `Owner`, `Admin`, `Operator`, `Viewer`, and audited `PlatformSupport`.

## Implementation design

The authenticated user selects or enters a tenant through a membership the server verifies. Never trust an arbitrary tenant header. Public storefront tenant resolution later comes from a validated host/slug mapping. Background jobs carry a validated tenant identifier and establish tenant context explicitly.

Every tenant-owned row contains `TenantId`; EF query filters are defense in depth, not the only enforcement mechanism. Application use cases authorize the actor and scope all reads/writes. Raw SQL, projections, jobs, storage paths, cache keys, and audit queries require explicit tenant-aware tests.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Tenant, user, membership, and role domain | `APPROVED` |
| 02 | Identity and secure browser session flow (SMTP password-reset amendment) | `APPROVED` |
| 03 | Trusted tenant context across API and jobs | `APPROVED` |
| 04 | Policy RBAC and audit event pipeline | `APPROVED` |
| 05 | Frontend authentication/workspace integration | `REVIEW` |
| 06 | Isolation and authorization test campaign | `NOT STARTED` |

## Prompt 01 — Tenant, user, membership, and role domain

> Implement Tenant, ApplicationUser, Membership, role definitions, membership lifecycle, tenant status, and required domain invariants. Configure ASP.NET Core Identity persistence and EF mappings with normalized unique constraints. An owner may create a tenant; membership changes must prevent removal of the last active owner. Define onboarding/readiness placeholders without implementing later feature checks. Add migrations, domain tests, persistence tests, and seed only safe development roles/users through explicit development tooling.

**Review checkpoint:** approve identity/tenant schema, ownership invariants, and migration.

## Prompt 02 — Identity and secure browser session flow

> Implement registration or invite-based onboarding according to an ADR, sign-in, sign-out, current-user, SMTP-delivered password reset, session expiration, and secure browser authentication for the Next.js seller workspace. Password-reset tokens and links must never be returned to browser/API callers or logs. Document the cookie/token choice, CSRF posture, CORS policy, account enumeration protections, rate limits, email-provider setup, and secret requirements. Do not add social login unless separately approved. Add API and browser tests for success, failure, lockout/rate behavior, expiration, CSRF, token expiry/reuse, safe SMTP failure, and sign-out.

**Review checkpoint:** approve the auth ADR, browser security controls, and end-to-end session evidence.

## Prompt 03 — Trusted tenant context across API and jobs

> Implement tenant context creation from verified membership for authenticated seller APIs. Reject missing, inactive, or unauthorized workspace selection. Add explicit tenant context propagation for Hangfire jobs, outbox processing, storage path builders, and future cache/search keys. Ensure context is cleared between requests/jobs. Add tenant-aware repository/query helpers without allowing callers to opt out casually. Test cross-request leakage, forged tenant selection, inactive membership, job context, raw query/projection behavior, and context cleanup.

**Review checkpoint:** approve all tenant-resolution entry points and isolation tests.

## Prompt 04 — Policy RBAC and audit event pipeline

> Implement policy-based authorization for Owner, Admin, Operator, Viewer, and PlatformSupport. Define a permission matrix for membership management, settings, catalog, inventory, orders, payments, conversations, integrations, AI, billing, reporting, and audit access; feature modules may initially expose only test endpoints. Implement append-only AuditEvent creation with tenant, actor, effective support actor if applicable, action, target, time, reason, correlation ID, and redacted metadata. PlatformSupport must have no default tenant access and every granted support session must be time-bounded and audited. Add policy and audit integration tests.

**Review checkpoint:** approve permission matrix, support-access safeguards, and audit shape.

## Prompt 05 — Frontend authentication/workspace integration

> Replace the Milestone 01 mock identity and workspace adapters with real API adapters while retaining explicit demo mode. Connect sign-in, sign-out, current user, workspace selection, team membership, role-aware navigation, denied states, and audit activity. Do not expose controls solely through client-side role checks; treat server denial as authoritative. Add loading/session-expired/error recovery and end-to-end tests for each role. Preserve later-feature screens on fixtures until their backend milestones.

**Review checkpoint:** approve real auth UX, role behavior, session recovery, and demo/real separation.

## Prompt 06 — Isolation and authorization test campaign

> Perform the milestone tenant-isolation and authorization verification. Create at least two tenants with overlapping-looking data and users in every role. Exercise REST endpoints, EF queries, projections, raw SQL paths, audit reads, Hangfire jobs, outbox handlers, host-independent storage path builders, and generated frontend clients. Add negative tests for forged identifiers, object-reference attacks, role downgrade, deleted memberships, support access expiry, and context reuse. Fix only issues in this milestone, then produce an isolation matrix mapping every entry point to evidence.

**Review checkpoint:** approve the isolation matrix with zero unresolved critical/high findings.

## Milestone exit gate

- A user can create or join a tenant and enter only authorized workspaces.
- Server-side policies enforce the approved role matrix.
- Tenant context is safe across API, EF, raw query, job, outbox, and storage-key paths.
- Cross-tenant and IDOR tests pass.
- Audit events identify actor, tenant, action, target, time, reason, and correlation ID.
- Frontend identity fixtures remain only in explicit demo mode.

