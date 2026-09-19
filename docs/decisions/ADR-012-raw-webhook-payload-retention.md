# ADR-012 — Raw Provider Webhook Payload Retention and Privacy Policy

- **Status:** `Accepted`
- **Date:** 2026-09-20
- **Owner:** Platform Architect
- **Reviewers:** Project Owner, Engineering Team
- **Affected milestones:** Milestone 07, Milestone 08, Milestone 11

## Context

Inbound social webhooks carry raw message payloads, headers, signatures, and customer contact information (phone numbers, full names, profile URLs, message text, and media URLs). 

Retaining the raw payload is necessary for:
1. Fast, durable ingestion and cryptographic signature verification.
2. Safe event replay during transient processing failures or code deployments.
3. Troubleshooting integration issues and provider discrepancies.

However, indefinite storage of raw webhook payloads poses significant privacy, data compliance (e.g. Nepal Individual Privacy Act, 2075), and storage bloat risks. We must define an explicit retention and PII redaction policy for the `WebhookEvent` entity.

## Decision

We adopt a dual-phase retention policy: **30-day raw payload retention with automated body purge/redaction while preserving event metadata indefinitely.**

1. **Phase 1: Ingestion & Short-Term Retention (0 to 30 Days):**
   - The raw webhook body, HTTP headers, provider event ID, and timestamp are stored immutably in `WebhookEvent`.
   - Access to raw bodies is restricted to authorized administrative/support roles (`Owner`, audited `PlatformSupport`) and never returned over general APIs or printed to logs.
   - Replay capabilities are fully supported during this 30-day window.
2. **Phase 2: Automated Purge / Redaction (After 30 Days):**
   - A scheduled Hangfire maintenance job scans for `WebhookEvent` records older than 30 days where `RawPayload` is not yet purged.
   - The job sets `RawPayload` to `"[PURGED]"` and flags `IsPurged = true` with a `PurgedAt` timestamp.
   - Crucial non-PII audit metadata (e.g., `EventId`, `TenantId`, `ConnectionId`, `ProviderEventId`, `OccurredAt`, `ProcessingStatus`, `CorrelationId`) is preserved indefinitely for reconciliation and compliance.
3. **Emergency Manual Purge:**
   - In response to customer data deletion requests (right to erasure), operators can trigger immediate redaction of specific customer webhook records.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Immediate Redaction at Ingress | Zero unredacted PII stored in the database. | Destroys raw payload needed for cryptographic verification and diagnostic replay; parsing errors could cause permanent message loss before normalization. | Rejected. High operational risk during provider schema changes or ingest bugs. |
| Indefinite Raw Storage | Maximum historical replayability. | High privacy risk; non-compliant with data minimization principles; database table bloat from large JSON payloads. | Rejected. Unacceptable compliance and storage risk for multi-tenant production operations. |
| **30-Day Retention with Automated Purge (Chosen)** | Provides full replay and diagnostic window for operational incidents while enforcing strict privacy minimization and bounded table growth. | Requires a scheduled background job to execute purging. | Accepted. Balances operational resilience with rigorous privacy governance. |

## Consequences

- **Product impact:** Merchants have a 30-day window to inspect and replay raw integration events.
- **Architecture impact:** `WebhookEvent` includes `RawPayload`, `IsPurged`, and `PurgedAt` fields. Hangfire maintenance worker performs periodic batch purging.
- **Security/privacy impact:** Customer PII in raw payloads is systematically eliminated after 30 days. Logs and diagnostic APIs always redact PII.
- **Cost/operations impact:** Keeps PostgreSQL storage size bounded and predictable.
- **Migration or rollback impact:** Fully compatible with future storage optimizations (e.g., offloading raw payloads to private S3 with lifecycle rules if payload volume exceeds PostgreSQL row thresholds).

## Validation evidence

- Unit and integration tests verify that raw payloads can be queried and replayed within the 30-day window.
- Background maintenance job tests verify that records older than 30 days have `RawPayload` replaced with `[PURGED]` and `IsPurged = true`.

## Supersession conditions

- Legal requirements mandating shorter (e.g. 7 days) or longer (e.g. 90 days) retention periods in specific operating jurisdictions.

