# Backend Rules

Applies to: C#, .NET, API controllers, domain/application/infrastructure layers, workers, persistence, and migrations.

## Modular-monolith dependency direction

- Domain has no outward dependencies.
- Application depends only on Domain and defines contracts for infrastructure.
- Infrastructure implements Application contracts.
- WebApi/Worker compose modules; they do not contain business logic.
- Modules interact via stable query contracts, domain/integration events, or explicit application orchestrators — never by reaching into another module's repositories.

## API style

- Use traditional `[ApiController]` controllers, not Minimal APIs.
- Controllers are thin dispatchers — inject service interfaces, return results.
- No MediatR or CQRS. Use service interfaces (e.g., `IOrderService`, `ICatalogService`) in the Application layer.
- Service implementations live in Infrastructure and contain business orchestration logic.

## Tenant scoping

- Every tenant-owned entity, query, cache key, job payload, storage path, and provider event must include `TenantId`.
- EF query filters are defense-in-depth, not the only barrier.
- Every service method explicitly verifies tenant ownership of referenced IDs.
- Never trust an arbitrary tenant header; resolve context from authenticated membership, connection identifier, or persisted job tenant.

## Authorization

- Server-side policy-based authorization on every endpoint and service method.
- Roles: Owner, Admin, Operator, Viewer, and audited PlatformSupport.
- Privileged operations require authorization check and audit event.

## Idempotency

- Retryable writes (webhooks, payment callbacks, outbox, stock operations) must use idempotency keys or unique constraints.
- Duplicate requests must not duplicate side effects.

## Audit

- Emit audit events for sensitive changes: identity/role, integrations, bot takeover/release, inventory, payment verification, order state, billing, and support access.
- Audit records include actor, tenant, action, timestamp, and correlation ID.

## Transactions and concurrency

- Use PostgreSQL transactions and concurrency controls for stock reservations, order creation, and payment state transitions.
- Concurrent reservation attempts must not oversell.

## Error handling

- Use RFC 7807 problem responses for all API errors.
- Include machine-readable type, title, status, and detail.

## Configuration and secrets

- Use typed configuration validated at startup.
- Secrets injected via environment; never committed to source.
- Redact secrets and PII in logs and AI traces.

## Migrations

- Database migrations run as a controlled, observable deployment task.
- Use backward-compatible expand/contract migrations.
- Never auto-migrate from every API instance at startup.

## Business logic placement

- No business logic in controllers or AI tools.
- Controllers inject application service interfaces and delegate all business logic to them.
- AI tools call application services — never repositories or DbContext directly.
- Service interfaces are defined in the Application layer; implementations live in Infrastructure.
