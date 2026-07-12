# Context Manifest — Required Reading Order

This document defines the exact files to read for each type of session. Follow the order specified; earlier files provide context needed by later ones.

## New session (any task)

| Order | File | Why |
|---:|---|---|
| 1 | `docs/context/CURRENT_WORK.md` | Current milestone, step, status, and next permitted action |
| 2 | `docs/context/CONTEXT_MANIFEST.md` | This file — reading order for the session type |
| 3 | `design_files/project_a_milestones/00_MASTER_INDEX.md` | Milestone sequence, source hierarchy, architecture invariants, execution rules |
| 4 | Active milestone file (per `CURRENT_WORK.md`) | Step details, acceptance criteria, review checkpoint requirements |
| 5 | `design_files/plan.md` Sections 10–11 | Authoritative architecture and implementation roadmap |
| 6 | `docs/decisions/ADR_INDEX.md` + accepted ADRs | Approved architectural changes since baseline |
| 7 | Latest checkpoint in `artifacts/checkpoints/` | Last approved state, remaining work, known issues |
| 8 | Git status and recent commits | Uncommitted changes, branch state |

## Frontend work

Read the new-session files above, plus:

| Order | File | Why |
|---:|---|---|
| 9 | `.claude/rules/frontend.md` | TypeScript, accessibility, data-access, and UX-state rules |
| 10 | `design_files/project_a_milestones/FRONTEND_DESIGN_DIRECTION.md` | Visual and motion design system requirements |
| 11 | `.claude/rules/testing.md` | Test requirements for frontend features |

## Backend work

Read the new-session files above, plus:

| Order | File | Why |
|---:|---|---|
| 9 | `.claude/rules/backend.md` | Modular-monolith, tenant scoping, authorization, idempotency rules |
| 10 | `design_files/plan.md` Section 10.3–10.5 | Module boundaries, domain model, tenant/identity design |
| 11 | `.claude/rules/testing.md` | Test requirements including isolation and concurrency tests |

## Social integration work

Read the new-session files above, plus:

| Order | File | Why |
|---:|---|---|
| 9 | `design_files/plan.md` Section 10.6 | Channel integration and event reliability design |
| 10 | `.claude/rules/backend.md` | Idempotency, webhook, and tenant rules |
| 11 | `.claude/rules/testing.md` | Provider failure, replay, and duplicate-delivery tests |

## AI work

Read the new-session files above, plus:

| Order | File | Why |
|---:|---|---|
| 9 | `design_files/plan.md` Section 10.7 | AI orchestration, knowledge, tool design, and safety constraints |
| 10 | `.claude/rules/backend.md` | No business logic in AI tools; tenant scoping |
| 11 | `.claude/rules/testing.md` | AI grounding, takeover, and adversarial tests |

## Deployment work

Read the new-session files above, plus:

| Order | File | Why |
|---:|---|---|
| 9 | `design_files/Divided Plans/Deployment_Strategy_and_Infrastructure_Plan.md` | Deployment topology, CI/CD, and environment strategy |
| 10 | `design_files/plan.md` Section 10.12 and 11.5 | Observability, release procedure, and environment rules |

## Security review

Read the new-session files above, plus:

| Order | File | Why |
|---:|---|---|
| 9 | `.claude/agents/security-reviewer.md` | Security review procedure and focus areas |
| 10 | `design_files/plan.md` Section 10.11 | Security, compliance, and data governance requirements |
| 11 | `.claude/rules/backend.md` | Tenant, auth, secret, and audit rules |

## Step review (checkpoint approval)

Read the new-session files above, plus:

| Order | File | Why |
|---:|---|---|
| 9 | `.claude/agents/code-reviewer.md` | Code review procedure and criteria |
| 10 | `.claude/agents/security-reviewer.md` | Security review procedure |
| 11 | `.claude/agents/test-reviewer.md` | Test review procedure |
| 12 | `artifacts/checkpoints/CHECKPOINT_TEMPLATE.md` | Expected checkpoint structure |
| 13 | `.claude/rules/documentation-and-checkpoints.md` | Documentation and checkpoint standards |
