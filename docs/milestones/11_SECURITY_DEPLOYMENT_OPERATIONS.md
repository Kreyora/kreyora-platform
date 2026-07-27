# Milestone 11 — Security, Deployment, Reliability, and Operations

## Objective

Prepare the implemented MVP for a controlled pilot. Harden security and privacy, implement observable deployment and rollback, configure production-shaped infrastructure, prove backup restoration, exercise failure handling, and create usable operating runbooks.

This milestone adds no product features.

## Dependencies

- Milestone 10 exit gate approved.
- Hosting provider/region, primary domain, object storage, backup destination, alert recipients, and environment owners are accepted by ADR.
- Initial recommendation: Docker Compose on a single appropriately sized VPS, Caddy, PostgreSQL, API/worker/frontend containers, Cloudflare DNS/R2, GitHub Actions/GHCR, and no Redis dependency.

## Implementation design

Environments are isolated: local, staging where available, and production. Images are built once and promoted by immutable digest. Application secrets live on the target or an approved secret manager, not in GitHub workflow logs or images. Production migrations run once as a controlled job after a backup/rehearsal and before compatible application rollout.

Recovery must address database loss, deployment failure, webhook backlog, provider outage, token expiry, notification failure, stuck reservation, disputed manual payment, and AI disablement.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Security/privacy hardening review and fixes | `NOT STARTED` |
| 02 | Metrics, tracing, logs, dashboards, and alerts | `NOT STARTED` |
| 03 | Production Compose, proxy, DNS/storage, and secrets | `NOT STARTED` |
| 04 | CI/CD image build, development/staging, and production promotion | `NOT STARTED` |
| 05 | Controlled migrations, backup/restore, and rollback | `NOT STARTED` |
| 06 | Load, abuse, dependency-failure, and kill-switch tests | `NOT STARTED` |
| 07 | Runbooks and full staging release rehearsal | `NOT STARTED` |

## Prompt 01 — Security/privacy hardening review and fixes

> Audit the implemented MVP against its threat boundaries: authentication/session/CSRF, tenant/IDOR, role/support access, public storefront abuse, uploads, webhooks/replay, provider secrets, AI prompt/tool/retrieval isolation, PII, logs, manual payments, jobs/outbox, dependencies, and deployment configuration. Create a ranked finding list with evidence. Fix in-scope critical/high findings and agreed medium findings, adding tests for every fix. Add security headers, rate/input limits, request/body/file constraints, secret/log redaction, retention/export/delete hooks, and least-privilege checks where missing. Do not claim formal compliance or security certification.

**Review checkpoint:** accept the finding disposition with no unresolved launch-blocking issue.

## Prompt 02 — Metrics, tracing, logs, dashboards, and alerts

> Complete OpenTelemetry-compatible traces, structured logs, and metrics for API latency/errors, tenant-safe correlation, webhook acknowledgement/lag, duplicates, outbox/backlog, job retry/DLQ, provider delivery/health, AI latency/cost/tool failure/escalation, reservation conflicts/expiry, checkout failures, manual payment-verification lag, database/storage/disk health, backups, and usage. Define actionable alert thresholds through ADR or initial operating assumptions, alert routing, silence/escalation rules, and redacted dashboards. Test that representative failures create the expected signal without exposing PII/secrets.

**Review checkpoint:** approve signal coverage, alert ownership, redaction, and tested notifications.

## Prompt 03 — Production Compose, proxy, DNS/storage, and secrets

> Implement the accepted production topology as versioned infrastructure/configuration: pinned API, worker if separate, frontend, PostgreSQL, and Caddy/reverse-proxy Compose services; persistent volumes; least-privilege users; internal networks; health/readiness; resource guidance; log rotation; TLS/DNS instructions; Cloudflare R2 product-media/backup configuration; and protected application environment files or approved secret injection. Keep Redis optional and absent unless an accepted ADR requires it. Add validation scripts that detect placeholder/default secrets, unsafe ports, writable containers where unnecessary, and missing health checks. Do not deploy production yet.

**Review checkpoint:** approve topology, network exposure, persistence, secret ownership, and configuration validation.

## Prompt 04 — CI/CD image build, development/staging, and production promotion

> Implement GitHub Actions workflows for CI, immutable image build/publish to GHCR, deploy to development/staging from the accepted branch, promote the exact tested digest to protected production, manual production approval, post-deploy health/smoke checks, and manual rollback dispatch. Use GitHub environments only for deployment connectivity; keep application secrets on targets or the approved secret manager. Prevent concurrent target deployments and record deployment metadata. Test workflow syntax and exercise a non-production deployment with a known digest.

**Review checkpoint:** approve branch/environment protection, image provenance, deployment concurrency, and non-production evidence.

## Prompt 05 — Controlled migrations, backup/restore, and rollback

> Implement a release procedure that checks compatibility, verifies/takes an encrypted PostgreSQL backup, runs one controlled migration job, deploys compatible images, verifies readiness and smoke paths, observes errors, and retains the prior digest. Define expand/contract migration rules and data rollback limitations. Implement scheduled encrypted backups to the accepted destination with retention and failure alerts. Restore a backup into an isolated environment, verify integrity and critical flows, then execute an application rollback rehearsal. Record recovery time and data-loss observations without advertising unsupported objectives.

**Review checkpoint:** approve migration control, successful restore, rollback evidence, retention, and recovery observations.

## Prompt 06 — Load, abuse, dependency-failure, and kill-switch tests

> Run production-shaped tests for public browsing/checkout, inventory contention, webhook bursts/duplicates, worker backlog, provider latency/rate limits/outage, AI latency/cost/provider failure, database interruption, storage failure, disk pressure, notification failure, token expiry, and malicious input/upload patterns. Verify bounded resources, no cross-tenant leakage, no silent loss, safe degradation, recovery, and alerting. Implement and test kill switches for new checkouts if necessary, provider connections/outbound automation, AI automation, and nonessential notifications while keeping seller access to existing truth. Fix launch-blocking defects.

**Review checkpoint:** approve capacity observations, degradation behavior, alerts, and kill-switch controls.

## Prompt 07 — Runbooks and full staging release rehearsal

> Write concise operator runbooks for deployment, rollback, database restore, webhook replay/DLQ, stuck reservation, provider outage/token loss, outbound-message failure, AI disablement, manual-payment dispute evidence, disk pressure, secret rotation, data export/deletion, and support access. Assign owner/escalation fields and exact diagnostic commands that avoid secrets. Execute a full staging rehearsal using an immutable image: backup, migration, deploy, health, fresh-tenant smoke path, sandbox/simulator inbound message, takeover, AI evaluation check, order processing, alert test, rollback, and post-rollback smoke. Record every unmet external gate.

**Review checkpoint:** approve runbook usability and the complete staging rehearsal report.

## Milestone exit gate

- No unresolved critical/high launch-blocking security issue remains.
- Production images are immutable, traceable, and promoted with approval.
- Secrets, networks, storage, backups, and target access follow accepted controls.
- Migration, staging deployment, health/smoke, rollback, alert, and restore are demonstrated.
- Failure and load behavior is bounded and observable.
- Kill switches and operator runbooks are tested.
- Unsupported SLA/compliance/security claims are absent.

