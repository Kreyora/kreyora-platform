# ADR-001 — Service-Layer Pattern with Traditional Controllers (No MediatR/CQRS)

- **Status:** `Accepted`
- **Date:** 2026-07-27
- **Owner:** Project owner
- **Reviewers:** —
- **Affected milestones:** 02 through 13 (all backend milestones)

## Context

The original plan (`docs/plan/plan.md` §8.2) specified Clean Architecture + DDD + CQRS with MediatR as the backend pattern. Section 8.8 already cautioned against unnecessary ceremony: "Enforce invariants and module boundaries, but avoid layer abstractions or MediatR use where they provide no product value."

During pre-M02 architecture review, the project owner decided to simplify the backend pattern by removing MediatR/CQRS entirely. The reasoning:

1. MediatR adds a layer of indirection (Command/Query classes, Handler classes, Response classes) that increases the number of files and concepts per feature without providing proportional value for this product's scale.
2. The project is a modular monolith with a single team — cross-cutting concerns (validation, authorization, logging) can be handled by middleware, filters, and service method decorators rather than MediatR pipeline behaviors.
3. Traditional service interfaces are simpler to understand, navigate, and debug.
4. The `Domain → Application → Infrastructure → WebApi` dependency direction is preserved regardless of whether MediatR is used.

## Decision

Use a **service-layer pattern with traditional `[ApiController]` controllers** instead of MediatR/CQRS.

Specifically:
- **Application layer** defines service interfaces (e.g., `IOrderService`, `ICatalogService`) and DTOs/request-response models.
- **Infrastructure layer** provides service implementations that orchestrate domain logic, repositories, and external providers.
- **WebApi layer** uses traditional `[ApiController]` classes that inject service interfaces and delegate all business logic to them. Controllers are thin dispatchers.
- **No MediatR package**, no `IRequest`/`IRequestHandler`, no command/query classes, no pipeline behaviors.
- Cross-cutting concerns (validation, authorization, logging, correlation) are handled by ASP.NET middleware, action filters, and FluentValidation.
- Domain events may still be used for cross-module side effects — dispatched through a simple domain event dispatcher, not MediatR.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Full MediatR/CQRS | Consistent dispatch pattern, pipeline behaviors for cross-cutting, clear command/query separation | High ceremony (3+ files per operation), harder navigation, MediatR dependency, overkill for monolith scale | Rejected — ceremony exceeds value for this product's scope and team size |
| Proportional MediatR (commands only for writes) | Reduced ceremony for reads, pipeline for writes | Inconsistent patterns across read/write paths, still adds MediatR dependency | Rejected — half-adoption creates inconsistency worse than either full approach |
| Service layer with Minimal APIs | Less boilerplate than controllers | Endpoint groups are less conventional, harder to organize for large API surface | Rejected — project owner prefers traditional controllers for familiarity and organization |

## Consequences

- **Product impact:** None. The API surface and behavior are identical regardless of internal dispatch pattern.
- **Architecture impact:** Application layer contains service interfaces instead of command/query types. Infrastructure contains service implementations. The `Domain → Application → Infrastructure → WebApi` dependency direction is unchanged.
- **Security/privacy impact:** None. Authorization is enforced at controller endpoints via policy attributes and verified in service methods.
- **Cost/operations impact:** Fewer NuGet packages (no MediatR). Slightly fewer files per feature.
- **Migration or rollback impact:** This decision is made before any backend code exists (M01 is frontend-only). No migration needed.

## Validation evidence

- All milestone prompt references to "commands/queries" have been updated to "service methods" across `plan.md` and milestone files 04, 05, 06, 08.
- `CLAUDE.md` architecture invariants updated.
- `.claude/rules/backend.md` updated with service-layer and controller rules.
- `ARCHITECTURE_SUMMARY.md` and `PROJECT_CONTEXT.md` updated.
- `ADR_INDEX.md` updated with this ADR.
- `00_MASTER_INDEX.md` architecture invariants updated.

## Supersession conditions

Revisit this ADR if:
- The team grows and needs stronger pipeline-based cross-cutting (e.g., automatic validation, audit, retry on every handler).
- The architecture moves to microservices where MediatR-style dispatch provides clearer boundaries.
- A specific module demonstrably benefits from command/query separation (in which case, consider a per-module ADR rather than reverting globally).
