# Milestone 02 — Engineering and Backend Foundation

## Objective

Create the production-shaped monorepo and backend foundation without breaking the approved frontend showcase. Establish the local topology, API conventions, persistence baseline, tests, Docker assets, and CI needed by every later milestone.

## Dependencies

- Milestone 01 exit gate approved.
- Frontend client ports and route map are treated as contract inputs, not necessarily final API schemas.

## Implementation design

Use a monorepo containing the existing Next.js frontend surface(s), a .NET 10 solution, PostgreSQL, Aspire AppHost, ServiceDefaults, API, optional dedicated worker host, and test projects. Use a modular-monolith dependency direction of `Domain → Application → Infrastructure → WebApi`.

Hangfire uses PostgreSQL storage initially. Keep a cache abstraction only where a measured use case exists; do not require Redis at MVP. Production packaging uses immutable containers. Migrations run as a controlled job, never automatically from every API instance.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Monorepo and solution topology | `APPROVED` |
| 02 | API conventions, configuration, and observability | `APPROVED` |
| 03 | PostgreSQL, EF Core, migrations, and durable primitives | `APPROVED` |
| 04 | Aspire, Docker, local dependencies, and developer workflow | `APPROVED` |
| 05 | Frontend contract adapters and generated types | `APPROVED` |
| 06 | CI, quality gates, and clean-checkout proof | `APPROVED` |

## Prompt 01 — Monorepo and solution topology

> Inspect the approved frontend repository and reorganize only as necessary into the Kreyora monorepo. Preserve its history and behavior. Create the .NET 10 solution with Domain, Application, Infrastructure, WebApi, ServiceDefaults, AppHost, optional Worker, unit-test, integration-test, architecture-test, and API-contract-test projects. Define project references that enforce the approved dependency direction. Add repository-level formatting, editor, package-management, build, and test commands. Create a concise architecture document and dependency tests. Do not add business modules, databases, authentication, or provider SDKs in this step.

**Review checkpoint:** approve repository tree, dependency direction, commands, and preservation of the frontend demo.

## Prompt 02 — API conventions, configuration, and observability

> Implement backend cross-cutting conventions: typed configuration with startup validation, environment templates without secrets, structured logging, correlation/trace IDs, RFC 7807 problem responses, exception handling, request logging with redaction, API versioning conventions, OpenAPI generation, health/live/readiness endpoints, time/ID abstractions, and dependency-registration boundaries. Add ServiceDefaults telemetry wiring suitable for local Aspire and later OpenTelemetry export. Define frontend-safe error envelopes and pagination conventions. Add tests for configuration failure, problem responses, correlation IDs, redaction, and health behavior. Do not implement business endpoints.

**Review checkpoint:** approve API/error conventions, configuration model, logging/redaction, and observability baseline.

## Prompt 03 — PostgreSQL, EF Core, migrations, and durable primitives

> Add PostgreSQL and EF Core infrastructure without business entities. Implement database configuration, migration assembly, design-time factory, transaction abstraction, database health check, and controlled migration command/job. Add reusable primitives for audit metadata, tenant-owned entities, idempotency records, outbox messages, and inbox/processed-message records, but do not yet implement tenant resolution. Establish UTC timestamp and sortable identifier conventions. Add integration-test containers or the approved equivalent. Test migrations from an empty database, transaction rollback, unique idempotency keys, and outbox persistence.

**Review checkpoint:** approve persistence conventions, migration process, durable-message primitives, and database test strategy.

## Prompt 04 — Aspire, Docker, local dependencies, and developer workflow

> Implement .NET Aspire local orchestration for API, PostgreSQL, frontend, and the optional worker process. Configure Hangfire with PostgreSQL storage and a development dashboard protected from unintended exposure. Add development-only seed hooks without business seed data. Create production-oriented multi-stage Dockerfiles and a Compose template for API, web, worker if separate, PostgreSQL, and reverse-proxy placeholders. Add startup ordering through readiness rather than sleeps, named volumes, resource guidance, and documented local commands. Do not add production credentials or deploy anything.

**Review checkpoint:** demonstrate one-command local startup, health visibility, clean shutdown, and container builds.

## Prompt 05 — Frontend contract adapters and generated types

> Formalize the boundary between the approved frontend client ports and the future REST API. Create an API-contract strategy using OpenAPI-generated TypeScript types/client wrappers or another documented approach that prevents hand-maintained drift. Implement a runtime adapter switch that keeps fixture clients available for demo mode and selects real clients only when configured. Add a minimal non-business `/v1/system/info` or equivalent endpoint to prove authenticated-neutral API connectivity, error mapping, correlation IDs, and generated-client use. Do not replace feature fixtures yet.

**Review checkpoint:** approve mock/real adapter switching, contract generation, error mapping, and absence of component-to-fetch coupling.

## Prompt 06 — CI, quality gates, and clean-checkout proof

> Implement the CI baseline for pull requests and pushes: .NET restore/build/format/test, architecture tests, frontend install/lint/type-check/test/build, OpenAPI drift check, migration validation, secret scanning, dependency review placeholders compatible with the repository, and Docker build validation. Cache safely without hiding dependency drift. Document branch and artifact conventions. Run the complete flow from a clean checkout or equivalent isolated worktree and record timings/results. Do not add deployment in this step.

**Review checkpoint:** approve reproducible clean-checkout evidence and required CI gates.

## Milestone exit gate

- Frontend showcase behavior remains intact.
- Clean checkout restores, builds, lints, tests, and starts through Aspire.
- PostgreSQL migration and integration-test infrastructure work.
- API conventions, typed configuration, OpenAPI contract generation, logs, traces, and health endpoints are tested.
- Docker images build with no committed secrets.
- No business domain or live provider was prematurely implemented.

