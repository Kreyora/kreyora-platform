# Kreyora — Claude Code Instructions

## Project identity

Kreyora is a Nepal-focused, multi-tenant social-commerce operating system. It converts social-channel enquiries into reliable orders through a catalog-aware AI assistant, unified seller inbox, branded public storefront, local-first payment options, and operational order/inventory control.

## Source hierarchy

When instructions conflict, use this authority order:

1. Accepted Architecture Decision Records (`docs/decisions/`)
2. `design_files/plan.md`, especially Sections 10 and 11
3. The active milestone file under `design_files/project_a_milestones/`
4. `design_files/project_a_milestones/00_MASTER_INDEX.md`
5. Supporting Markdown plans in `design_files/Divided Plans/`
6. `design_files/Kreyora.docx` as the original reference

Never silently resolve a conflict. Record it in an ADR.

## Architecture invariants

These are locked unless an accepted ADR changes them:

- Nepal-focused multi-tenant social-commerce operating system
- Next.js with strict TypeScript for seller and storefront interfaces
- ASP.NET Core .NET 10 modular-monolith backend
- PostgreSQL with EF Core
- ASP.NET Core Identity, tenant memberships, and policy-based RBAC
- Hangfire with PostgreSQL storage for MVP
- Redis is optional and not an MVP dependency
- S3-compatible private object storage, initially Cloudflare R2 unless changed by ADR
- Docker and .NET Aspire for local orchestration
- GitHub Actions and GHCR for controlled delivery
- COD and merchant QR/manual verification for MVP payments
- Exactly one production-validated social-channel adapter for MVP
- Provider-neutral AI orchestration
- AI is never the source of truth for product, price, stock, delivery, payment, or order facts
- Tenant isolation, authorization, idempotency, audit, tests, and observability are mandatory features
- No provider, payment, security, compliance, or production capability may be fabricated

## Mandatory session-start procedure

At the beginning of every implementation task:

1. Read `docs/context/CURRENT_WORK.md`
2. Read `docs/context/CONTEXT_MANIFEST.md`
3. Read `design_files/project_a_milestones/00_MASTER_INDEX.md`
4. Read only the active milestone file indicated by `CURRENT_WORK.md`
5. Read the relevant sections of `design_files/plan.md` (Sections 10–11 for architecture; the active milestone section for step details)
6. Read applicable accepted ADRs listed in `docs/decisions/ADR_INDEX.md`
7. Read the previous checkpoint from `artifacts/checkpoints/`
8. Inspect Git status and recent relevant commits
9. Summarize recovered state before editing any file

## Execution controls

- Execute exactly one milestone prompt step at a time.
- Use plan mode before complex edits.
- Wait for human approval of the implementation plan before coding.
- Preserve existing behavior and unrelated changes.
- Add tests with every feature.
- Never start the next prompt automatically.
- Never commit, push, create a PR, deploy, or contact an external service without explicit authorization.
- Never use production secrets or fabricate provider behavior.
- Record architectural changes as ADRs in `docs/decisions/`.
- Finish every step with a checkpoint report in `artifacts/checkpoints/`.
- Update `docs/context/CURRENT_WORK.md` after every approved state change.
- Treat server/database facts as authoritative over client-side state.
- The main agent is the only editing agent unless an isolated worktree is explicitly approved.

## Required completion evidence

Every implementation step must report:

- Scope completed
- Files changed (added, modified, removed)
- Design and data flow explanation
- Commands and tests run
- Test results
- Screenshots for visible UI changes
- Contract or migration changes
- Security and tenant-isolation impact
- Assumptions and blockers
- Manual verification procedure
- Confirmation that the next prompt was not started

## Key file locations

| Purpose | Path |
|---|---|
| Master plan | `design_files/plan.md` |
| Milestone index | `design_files/project_a_milestones/00_MASTER_INDEX.md` |
| Active milestone | See `docs/context/CURRENT_WORK.md` |
| Design direction | `design_files/project_a_milestones/FRONTEND_DESIGN_DIRECTION.md` |
| Current work state | `docs/context/CURRENT_WORK.md` |
| Context manifest | `docs/context/CONTEXT_MANIFEST.md` |
| Session recovery | `docs/context/SESSION_START.md` |
| Architecture summary | `docs/architecture/ARCHITECTURE_SUMMARY.md` |
| ADR index | `docs/decisions/ADR_INDEX.md` |
| ADR template | `docs/decisions/ADR_TEMPLATE.md` |
| Checkpoint template | `artifacts/checkpoints/CHECKPOINT_TEMPLATE.md` |
| Checkpoints | `artifacts/checkpoints/M<NN>-S<NN>.md` |
| Frontend rules | `.claude/rules/frontend.md` |
| Backend rules | `.claude/rules/backend.md` |
| Testing rules | `.claude/rules/testing.md` |
| Doc/checkpoint rules | `.claude/rules/documentation-and-checkpoints.md` |

Do not import entire plan or milestone files into context. Read them on demand per the session-start procedure.
