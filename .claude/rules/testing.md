# Testing Rules

Applies to: all test files and test-related infrastructure.

## Behavioral verification

- Tests must verify behavior and outcomes, not implementation details alone.
- Prefer testing public interfaces and observable side effects over mocking internals.

## Required negative tests

### Tenant isolation
- API, database, cache, job, storage, and hostname paths cannot access another tenant's data.
- Cross-tenant operations must return forbidden/not-found, never leak data.

### Authorization
- Unauthenticated requests are rejected.
- Role-insufficient requests are denied.
- Elevated operations require correct role and tenant membership.

### Idempotency and duplicate delivery
- Duplicate webhook events do not create duplicate records.
- Duplicate payment callbacks do not double-credit.
- Retried stock operations do not double-move.

### Inventory concurrency
- Concurrent reservation attempts for the last unit must not both succeed.
- Expired reservations release stock correctly.
- Stock ledger reconciles after concurrent operations.

### State transitions
- Invalid order state transitions are rejected.
- Invalid payment state transitions are rejected.
- Invalid fulfilment state transitions are rejected.

### Provider failure and replay
- Provider timeouts and errors are handled gracefully.
- Failed webhook events can be replayed.
- Dead-letter queue captures terminal failures.

### AI grounding and takeover
- AI responses only use data from authorized tools or approved knowledge.
- AI cannot send messages after human takeover.
- Cross-tenant knowledge retrieval is impossible.
- Adversarial/prompt-injection inputs are handled safely.

## Test-with-feature rule

- Tests must be added with each relevant feature, not deferred to a later step.
- Every implementation step includes unit, integration, or end-to-end tests as appropriate.
