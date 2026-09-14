---
trigger: always_on
description: Kreyora architecture, delivery controls, Graphify protocol, and Architect + Builder workflow.
---

# Kreyora Antigravity Rules

Mark this rule **Always On** in Antigravity. It is the workspace-specific operating contract. Do not create or commit a workspace `.gemini/` folder: Antigravity reads workspace rules from `.agents/rules/`; global personal instructions belong in `~/.gemini/GEMINI.md`.

## Authority, scope, and session start

Kreyora is a Nepal-focused, multi-tenant social-commerce OS. Resolve authority in this order: accepted ADRs in `docs/decisions/` → `docs/plan/plan.md` (especially sections 10–11) → the active milestone → `docs/milestones/00_MASTER_INDEX.md` → `docs/plan/divided/` → historical documents. Never silently resolve a conflict: report it and propose an ADR.

Before proposing or editing work, read `docs/context/CURRENT_WORK.md`, `docs/context/CONTEXT_MANIFEST.md`, the master index, active milestone, relevant plan, `docs/decisions/ADR_INDEX.md`, relevant accepted ADRs, and the newest checkpoint. Inspect `git status` and relevant recent commits. State the active milestone/step, permitted and prohibited scope, blockers, and unrelated dirty work.

Implement exactly one approved step at a time. Do not start the next step merely because the current one is complete. Preserve unrelated changes. Do not commit, push, open PRs, deploy, contact external systems, use production secrets, or claim unverified provider behavior without explicit user authorization. Only one agent edits a shared worktree unless isolated worktrees are explicitly approved.

## Architecture and directory conventions

- Frontend: `apps/web` uses Next.js 16, React 19, TypeScript strict, Tailwind CSS 4, Radix UI, Vitest/Testing Library, Playwright, ESLint/Prettier, and generated OpenAPI TypeScript clients. Routes live in `src/app`; shared UI in `src/components` and `src/design-system`; typed ports in `src/lib/ports`; API/mock/fixture adapters in `src/lib/adapters/{api,mock,fixtures}`; generated API types in `src/lib/api/generated`; common types in `src/lib/types`; tests in `src/__tests__`; browser tests in `e2e`.
- Frontend rules: components consume typed ports, never fixtures directly; server facts (price, inventory, tenancy, authorization, publication, totals) are authoritative; preserve explicit loading/empty/error/forbidden states; use accessible semantic controls, labels, keyboard support, focus management, and 44px touch targets. Do not use `any`, unsafe casts, or client-side authorization.
- Backend: `services/api` targets .NET 10 and uses ASP.NET Core, EF Core/Npgsql/PostgreSQL, ASP.NET Identity/RBAC, Hangfire PostgreSQL, Serilog, MailKit, S3-compatible private storage, API versioning, Scalar/OpenAPI, and .NET Aspire. Projects: `Kreyora.Domain`, `Application`, `Infrastructure`, `WebApi`, `ServiceDefaults`, and `AppHost`.
- Backend rules: Domain has no outward dependencies; Application depends only on Domain/contracts; Infrastructure implements ports; WebApi is thin composition/API boundary. Use `[ApiController]` controllers, not Minimal APIs. Do not introduce MediatR/CQRS, cross-module repositories, business logic in controllers, or direct AI/provider calls outside application boundaries. Use DI and async cancellation tokens.
- Tests: backend `services/api/tests/{Kreyora.UnitTests,Kreyora.ArchitectureTests,Kreyora.ContractTests,Kreyora.IntegrationTests}`. Tenant-owned operations require verified tenant context and policy authorization; enforce tenant isolation, audit/idempotency semantics, safe errors, stock bounds/concurrency, and no cross-tenant leakage. Integration tests use real PostgreSQL/Testcontainers, never SQLite or in-memory substitutes for relational/concurrency behavior.
- Local composition: `docker-compose.yml` provides PostgreSQL 16, migrator, API, web, and Mailpit. Aspire `Kreyora.AppHost` is a local composition/developer experience, not a deployment requirement.

## Architect + Builder protocol

### Phase 1 — Architect (Claude Opus 4.6)

Use Opus only for high-level system design, threat/edge-case analysis, schema and API contracts, migration strategy, ADR choices, acceptance criteria, and an implementation handoff. It may perform bounded read-only inspection and Graphify queries. It must not write full feature implementation, perform iterative terminal/test loops, or make speculative broad edits.

Write the proposed handoff to root `task.md`. For milestone work also create/update the durable scoped plan at `docs/plan/M<NN>-S<NN>_<TOPIC>_PLAN.md`. The handoff must name allowed/prohibited scope, affected files/modules, data/API contract, invariants, tests/commands, rollback/migration considerations, documentation/checkpoint requirements, and explicit approval gate. Stop after the plan and wait for human approval.

### Phase 2 — Builder (Gemini Flash 3.8)

Begin only from an approved `task.md`/step plan. Reinspect status and relevant source, implement the smallest coherent change, and run focused checks iteratively. It may edit code and docs and run builds/tests/lints. It must not replace an approved architecture, silently broaden scope, skip required tests, or change an accepted ADR; return such decisions to Phase 1. Record actual commands/results, defects, and remaining review items in the checkpoint.

At completion update the active milestone and `CURRENT_WORK.md`, then create `artifacts/checkpoints/M<NN>-S<NN>.md`. Use checkpoint status `REVIEW` until human approval. Refresh Graphify only after explicit step/milestone approval (or explicit user request), then record the refresh.

### Model selection and execution mode

Rules cannot select, upgrade, or switch models. In the model selector manually choose **Claude Opus 4.6 (thinking)** for Phase 1; after approval start the Builder turn with available **Gemini 3.8 Flash High**. Selection is sticky for an active turn. Do not assume “latest” switches automatically; confirm version/effort before each phase. Availability depends on the signed-in plan.

For terminal planning, use `agy --mode=plan`; for implementation use `agy --mode=accept-edits` only after approval. In the IDE, use Plan mode for Phase 1 and review the generated plan before enabling edits. A pinned/headless invocation must explicitly pass its intended `--model` and `--effort high`; an unknown pinned model must fail rather than fall back.

### Dev Containers and test environment

There is **no committed `.devcontainer/devcontainer.json`**. Do not claim Dev Containers reproduced tests until a separately approved definition exists. Supported local testing is host Node 22/pnpm 11.13.1/.NET SDK 10.0.103 plus Docker Compose/Testcontainers. In a future Dev Container verify `node`, `pnpm`, `dotnet`, Docker/Testcontainers, and PostgreSQL prerequisites, then run the quality gates below. Never bypass Docker/socket permission or remove non-disposable resources.

## Graphify-first code navigation

`graphify-out/` is generated local knowledge-graph output and is gitignored. Treat live source and accepted docs as authority if graph evidence is stale. Before editing unfamiliar code or an impact-sensitive boundary:

1. Run `graphify query "<feature, module, invariant, or file relationship>" --budget 1200`.
2. Use `graphify explain <node-id>` for a node’s evidence and `graphify path <source> <target>` or `graphify affected <path>` when assessing impact.
3. Inspect cited live files before changing them. State relevant graph findings in the implementation note.

Audit/update commands:

```bash
graphify hook status
graphify update .
graphify query "How does public checkout reach inventory reservations?" --budget 1200
graphify antigravity install
```

Run `graphify update .` only after an approved boundary or explicit request; it is local AST analysis. `graphify antigravity install` creates Graphify’s Antigravity skill/rule/workflow assets and may write a global skill, so ask before running it. Do not install hooks unless explicitly approved; the repository policy governs update cadence.

The tracked workspace MCP connection is `.agents/mcp_config.json`. Use `~/.gemini/config/mcp_config.json` only for a personal global connection. It uses `uv` to supply `graphifyy` and `mcp` without depending on a globally installed Graphify CLI:

```json
{
  "mcpServers": {
    "graphify": {
      "command": "uv",
      "args": ["run", "--with", "graphifyy", "--with", "mcp", "-m", "graphify.serve", "${workspace.path}/graphify-out/graph.json"],
      "cwd": "${workspace.path}"
    }
  }
}
```

The equivalent already-installed-environment command is `python3 -m graphify.serve graphify-out/graph.json`. It serves via stdio and must remain running for the MCP client. Do not add global MCP configuration automatically.

## Commands and quality gates

Run commands from the repository root unless the command changes directory itself.

```bash
# Frontend dependencies and full CI-equivalent gate
pnpm install --frozen-lockfile
pnpm ci:frontend

# Frontend focused work
pnpm dev
pnpm build
pnpm lint
pnpm typecheck
pnpm test
pnpm --filter @kreyora/web test:e2e
pnpm generate:api

# Backend quality gate
dotnet restore services/api/Kreyora.slnx
dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1
dotnet test services/api/Kreyora.slnx --configuration Release --no-build
dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --no-build

# Local runtime
docker compose up --build
dotnet run --project services/api/src/Kreyora.AppHost/Kreyora.AppHost.csproj
dotnet run --project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj
```

Use Node 22 and the root-pinned pnpm `11.13.1`; use the SDK in `global.json` (.NET `10.0.103`). Frontend CI is `pnpm install --frozen-lockfile` then `pnpm ci:frontend`; backend CI restores, builds Release, and runs the solution tests. CI intentionally does **not** enforce `dotnet format`; still write idiomatic, repository-consistent code and run `git diff --check`. Do not invent green results: report skipped checks, warnings, failures, and environment limits exactly.

## API, database, and delivery controls

Keep public/API contracts versioned under `/v1`, preserve RFC 7807-style safe errors, validate client input while recomputing all authoritative values server-side, and never accept tenant identity, prices, totals, stock state, payment state, or publication state from the browser. Tenant filters must apply to every read/write; use idempotency keys and audit records for mutation workflows. Add migrations deliberately and prove pending-model state. Generate the OpenAPI snapshot/client after a contract change and keep generated code mechanical.

For a change, test the smallest affected scope first, then all relevant gates. Add negative tests for authorization, tenancy, tampering, replay, validation, and concurrency when changing a protected workflow. Browser/UI changes require state and accessibility coverage; API/data changes require unit plus real-PostgreSQL integration/contract coverage where appropriate. After Testcontainers work, remove only disposable helper containers/images created by the test run; preserve project containers, images, volumes, and user data.

Documentation is deliverable code: update ADRs only for durable architectural decisions, plans for approved scope, milestones/current work during execution, API docs for contract changes, and a factual checkpoint at review. Do not update Graphify during REVIEW unless explicitly asked; after approval use the governed refresh process above.
