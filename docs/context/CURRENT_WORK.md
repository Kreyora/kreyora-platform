# Current Work State

## Active position

- **Milestone:** 03 — Tenancy, Identity, RBAC, and Audit
- **Step:** 02 amendment — SMTP Password Reset Delivery
- **Status:** `REVIEW`
- **Active milestone file:** `docs/milestones/03_TENANCY_IDENTITY_RBAC.md`

## Branch and commit state

- **Branch:** feat/master/m03-s03 (local implementation; no commit, push, or PR created by this step)
- **Last state:** M03-S01 approved; M03-S02 SMTP corrective amendment and M03-S03 tenant-context implementation both await review

## Checkpoint

- **Current checkpoint:** `artifacts/checkpoints/M03-S02.md` (SMTP amendment)
- **Previous checkpoint:** `artifacts/checkpoints/M03-S03.md`

## Blockers

None.

## Current objective

The project-owner-directed M03-S02 corrective amendment is complete and awaiting review. It replaces the initial Development-only reset-token/link exposure with SMTP delivery using MailKit and Identity's official reset tokens:

1. The API always returns the same `202 Accepted` reset-request message and never returns a token or reset URL.
2. The browser recovery screen directs the user to email only; it has no development continuation link.
3. `IEmailSender` / `SmtpEmailSender`, typed SMTP configuration, token expiry, safe failure handling, and Mailpit-controlled SMTP proof are implemented.
4. ADR-004, development/hosted setup instructions, and the M03 plan/checkpoint records document the new required flow.

M03-S03 tenant-context implementation remains complete and awaiting its own review; it was not altered beyond updated regression evidence.

**Evidence:** Formatting, Release build, and all backend tests pass (95 total: 51 unit, 33 integration, 6 architecture, 5 contract); frontend regression suite passes (482 tests); the SMTP integration test delivered a MailKit message to a controlled Mailpit Testcontainers inbox and read it back. See `artifacts/checkpoints/M03-S02.md`.

## Next permitted action

Review `artifacts/checkpoints/M03-S02.md`, `docs/decisions/ADR-004-smtp-password-reset-delivery.md`, the SMTP/Identity tests, and `docs/architecture/LOCAL_EMAIL_DELIVERY.md`. Also review the existing M03-S03 checkpoint before approving later M03 work.

## Next prohibited action

- Starting M03-S04 or any subsequent implementation prompt before this SMTP amendment and M03-S03 are reviewed and approved
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
