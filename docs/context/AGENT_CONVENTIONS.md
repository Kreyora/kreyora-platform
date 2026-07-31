# Agent Conventions — Multi-Agent Workflow

## Purpose

This document defines how any AI coding agent (Claude Code, OpenAI Codex, or any future agent) should operate on the Kreyora repository. It is the single source of truth for agent-agnostic workflow rules.

Each agent has its own configuration location. Shared instruction files may be versioned when they are needed for reliable handoff across agents and machines; machine-local settings remain ignored. The **shared project standards** live in the repository itself.

## Agent config locations

| Agent | Config directory | Gitignored | Root instructions file |
|---|---|---|---|
| Claude Code | `.claude/` | `.claude/local/`, `.claude/settings.local.json` | `CLAUDE.md` |
| OpenAI Codex | `.openai/` | `.openai/local/`, `.openai/settings.local.json` | `.openai/CODEX.md` |

## Shared standards (apply to ALL agents)

These files contain project-wide coding and process standards. They live under `.claude/rules/` for historical reasons but are **not Claude-specific**. Every agent must follow them:

| File | Scope |
|---|---|
| `.claude/rules/frontend.md` | TypeScript, accessibility, data-access, UX-state, visual direction |
| `.claude/rules/backend.md` | Modular-monolith, tenant scoping, API style, authorization, idempotency, audit |
| `.claude/rules/testing.md` | Behavioral verification, required negative tests, test-with-feature rule |
| `.claude/rules/documentation-and-checkpoints.md` | History integrity, evidence standards, ADR/checkpoint requirements, status vocabulary |

## Session-start procedure (all agents)

Before any implementation work, read these files in order:

1. `docs/context/CURRENT_WORK.md` — current milestone, step, status, next permitted action
2. `docs/context/CONTEXT_MANIFEST.md` — reading order for the session type
3. `docs/milestones/00_MASTER_INDEX.md` — milestone sequence, invariants, execution rules
4. Active milestone file (named in `CURRENT_WORK.md`) — step details and acceptance criteria
5. `docs/plan/plan.md` Sections 10–11 — authoritative architecture
6. `docs/decisions/ADR_INDEX.md` + accepted ADRs — approved changes
7. Latest checkpoint in `artifacts/checkpoints/` — last completed state
8. Git status — uncommitted changes, branch state

## Execution rules (all agents)

- One milestone step at a time
- Plan first, get human approval, then implement
- Implement in batches, mark progress
- Add tests with every feature
- Never start the next step without human authorization
- Never commit, push, create PRs, deploy, or contact external services without explicit authorization
- Record architectural changes as ADRs
- Finish every step with a checkpoint in `artifacts/checkpoints/`
- Update `docs/context/CURRENT_WORK.md` after every completed step
- Preserve existing behavior and passing tests

## Required completion evidence (all agents)

Every step must report: scope completed, files changed, design explanation, commands and tests run, test results, contract/migration changes, security/tenant impact, assumptions, blockers, manual verification procedure, and confirmation that the next step was not started.

## Handoff between agents

When switching from one agent to another mid-project:

1. The departing agent must ensure `docs/context/CURRENT_WORK.md` is accurate
2. The arriving agent must follow the session-start procedure above
3. The arriving agent must read the latest checkpoint to understand the last completed state
4. No agent should assume anything about the project state from prior chat history — the repository docs are the source of truth
