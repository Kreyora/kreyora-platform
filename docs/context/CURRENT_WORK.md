# Current Work State

## Active position

- **Milestone:** 03 - Tenancy, Identity, RBAC, and Audit
- **Step:** M03-S05 - Real Seller Authentication, Workspace, Team, and Audit UI
- **Status:** `REVIEW`
- **Active milestone file:** `docs/milestones/03_TENANCY_IDENTITY_RBAC.md`

## Branch and checkpoint state

- **Branch:** `feat/master/m03-s04` with uncommitted M03-S05 implementation changes.
- **Current checkpoint:** `artifacts/checkpoints/M03-S05.md`
- **Previous checkpoint:** `artifacts/checkpoints/M03-S04.md`
- **Last approved state:** M03-S01 through M03-S04, including the Scalar and RBAC corrective addenda, are approved and merged.

## Current objective

M03-S05 connects completed identity, workspace, membership, permission, and audit APIs to the seller browser. Workspace selection is per browser tab in `sessionStorage`; the server verifies the tenant header and all permissions on every tenant-scoped request. Other product screens remain fixture-backed.

## Next permitted action

Review M03-S05 real-mode behavior and its checkpoint. After approval, plan M03-S06 only.

## Next prohibited action

- Starting M03-S06 or a later prompt before M03-S05 is reviewed and approved.
- Invitations, external providers, or product-business logic outside M03-S05.
- Committing, pushing, deploying, or using production secrets without explicit authorization.

## Update history

| Date | Change | By |
|---|---|---|
| 2026-08-02 | Project owner approved and merged M03-S04. M03-S05 completed with real seller identity, workspace, membership, permission, and audit UI. Status -> REVIEW. | Codex |
| 2026-08-02 | M03-S04 completed live policy RBAC, append-only audit events, and Owner-issued read-only PlatformSupport access. | Codex |
| 2026-08-02 | Project owner approved and merged M03-S02 (including SMTP amendment) and M03-S03. | Project owner |
| 2026-08-01 | User approved M02 completion; M03-S01 implemented and moved to review. | Codex |
