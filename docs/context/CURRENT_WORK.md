# Current Work State

## Active position

- **Milestone:** 03 — Tenancy, Identity, RBAC, and Audit
- **Step:** M02-S05 corrective addendum — Development-only Scalar API Reference
- **Status:** `REVIEW`
- **Active milestone file:** `docs/milestones/03_TENANCY_IDENTITY_RBAC.md`

## Branch and commit state

- **Branch:** feat/master/m03-s03 (local implementation; no commit, push, or PR created by this step)
- **Last state:** M03-S01, M03-S02 (including SMTP), and M03-S03 are approved and merged. The M02-S05 Scalar addendum awaits review.

## Checkpoint

- **Current checkpoint:** `artifacts/checkpoints/M02-S05.md` (Scalar corrective addendum)
- **Previous checkpoint:** `artifacts/checkpoints/M03-S03.md`

## Blockers

None.

## Current objective

The project-owner-directed M02-S05 corrective addendum is complete and awaiting review. It adds Scalar as a Development-only interactive API reference backed by the existing `/openapi/v1.json` document:

1. `/scalar` is mapped only in Development and uses the existing OpenAPI document route.
2. The pinned `Scalar.AspNetCore` package requires no provider, credential, Docker Compose, or frontend product change.
3. Isolated Development-host tests prove the viewer/document are available only in Development without loading local database, Hangfire, Gmail, or User Secrets configuration.
4. `API_CONTRACT_STRATEGY.md` documents the local launch command and viewer URL; `pnpm generate:api` remains the contract-generation path.

M03-S04 remains not started.

**Evidence:** Scalar-specific Development/non-Development endpoint tests pass. The final formatting, build, full backend suite, OpenAPI generation zero-diff, and diff-hygiene evidence is recorded in `artifacts/checkpoints/M02-S05.md`.

## Next permitted action

Review the Scalar addendum in `artifacts/checkpoints/M02-S05.md`, `docs/architecture/API_CONTRACT_STRATEGY.md`, and `ScalarEndpointTests`. After approval, plan M03-S04 only.

## Next prohibited action

- Starting M03-S04 or any subsequent implementation prompt before this Scalar corrective addendum is reviewed and approved
- Policy RBAC, audit events, frontend workspace integration, or broader product business logic
- Committing, pushing, or deploying

## Update history

| Date | Change | By |
|---|---|---|
| 2026-07-12 | Initialized at bootstrap. Milestone 01, Step 01, NOT STARTED. | Bootstrap session |
| 2026-07-12 | M01-S01 initial implementation complete. | M01-S01 session |
| 2026-07-12 | M01-S01 amended (9 amendments). Status → REVIEW. | M01-S01 amendment session |
| 2026-07-12 | M01-S02 complete. 116 tests pass, clean type check. Status → REVIEW. | M01-S02 session |
| 2026-07-12 | M01-S03 complete. 169 tests pass, clean type check. Status → REVIEW. | M01-S03 session |
| 2026-07-12 | M01-S04 complete. 219 tests pass, clean type check. Status → REVIEW. | M01-S04 session |
| 2026-07-12 | M01-S05 complete. 284 tests pass, clean type check. Status → REVIEW. | M01-S05 session |
| 2026-07-12 | M01-S06 complete. 321 tests pass, clean type check. Status → REVIEW. | M01-S06 session |
| 2026-07-12 | M01-S07 complete. 402 tests pass, clean type check. Status → REVIEW. | M01-S07 session |
| 2026-07-12 | M01-S08 complete. Milestone 01 finished. Status → REVIEW. | M01-S08 session |
| 2026-07-27 | M02-S01 complete. 11 tests pass, 0 build errors. Status → COMPLETE. | M02-S01 session |
| 2026-07-27 | M02-S02 complete. 54 tests pass, 0 build errors. Status → COMPLETE. | M02-S02 session |
| 2026-07-27 | M02-S03 complete. 61 tests pass, 0 build errors. Status → COMPLETE. | M02-S03 session |
| 2026-07-27 | M02-S04 complete. 61 backend + 482 frontend tests pass. Both Docker images build. Status → COMPLETE. | M02-S04 session |
| 2026-07-28 | M02-S05 complete. 63 backend + 494 frontend tests pass. Contract adapters wired. Status → COMPLETE. | M02-S05 session |
| 2026-08-01 | M02-S06 implementation complete. CI baseline and isolated clean-copy proof recorded. Status → REVIEW. | Codex |
| 2026-08-01 | M02-S06 amended: pnpm 11.13.0, PR-only quality/security CI, manual-only Docker workflow, and unsupported jobs disabled. Status remains REVIEW. | Codex |
| 2026-08-01 | M02-S06 amended: secret-scan now has read-only pull-request metadata access and disabled PR comments. Status remains REVIEW. | Codex |
| 2026-08-01 | M02-S06 amended: renamed PR workflow and split manual Docker validation into separate backend and frontend workflows. Status remains REVIEW. | Codex |
| 2026-08-01 | User approved M02 completion; M03-S01 implemented and moved to REVIEW. | Codex |
| 2026-08-01 | Added the non-secret .NET User Secrets project identifier and documented persistent local PostgreSQL setup; connection value remains outside Git. | Codex |
| 2026-08-01 | Reconciled the merged M03-S02 implementation as approved and added reconstructed evidence because its original checkpoint was absent. | Codex |
| 2026-08-01 | M03-S03 complete: verified tenant context, durable propagation, outbox migration, and isolation coverage. Status → REVIEW. | Codex |
| 2026-08-02 | M03-S02 amended: registered MVC antiforgery filter services and added a real CSRF registration endpoint regression test. | Codex |
| 2026-08-02 | M03-S02 amended: registered Identity default token providers and added PostgreSQL coverage for Development password reset. | Codex |
| 2026-08-02 | M03-S02 corrected: superseded browser-visible Development reset tokens/links with SMTP delivery, typed secure configuration, MailKit/Mailpit transport proof, ADR-004, and 95-test regression evidence. Status → REVIEW. | Codex |
| 2026-08-02 | M03-S02 CI correction: dedicated test-host SMTP, CORS, authentication, tenant-resolution, and antiforgery configuration removes hidden User Secrets dependencies; all 95 backend tests pass. Status remains REVIEW. | Codex |
| 2026-08-02 | Project owner approved and merged M03-S02 (including SMTP amendment) and M03-S03. | Project owner |
| 2026-08-02 | M02-S05 corrective addendum added Development-only Scalar API reference, isolated route tests, and contract documentation. Status → REVIEW. | Codex |
