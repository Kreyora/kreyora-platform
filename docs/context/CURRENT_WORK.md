# Current Work State

## Active position

- **Milestone:** 04 - Catalog, Inventory, and Media
- **Step:** M04-S01 - Catalog and Variant Domain
- **Status:** `REVIEW`
- **Active milestone file:** `docs/milestones/04_CATALOG_INVENTORY_MEDIA.md`

## Branch and checkpoint state

- **Branch:** `feat/master/m03-s04` with uncommitted M03-S06 verification changes.
- **Current checkpoint:** `artifacts/checkpoints/M04-S01.md`
- **Previous checkpoint:** `artifacts/checkpoints/M03-S06.md`
- **Last approved state:** M03-S01 through M03-S06 are approved and merged.

## Current objective

M04-S01 establishes the tenant-scoped catalog and variant domain: canonical NPR prices, publication rules, tenant-scoped slug/SKU uniqueness, optimistic concurrency, authorization, idempotent creates, and audit evidence. Inventory, media, APIs, and frontend integration remain out of scope.

## Next permitted action

Review the M04-S01 implementation and either approve it or request changes.

## Next prohibited action

- Starting M04-S02 or any later Milestone 04 step before M04-S01 is approved.
- Adding inventory, media, HTTP API, generated-client, frontend-integration, storefront, or provider work during M04-S01.
- Committing, pushing, deploying, or using production secrets without explicit authorization.

## Update history

| Date | Change | By |
|---|---|---|
| 2026-09-04 | M04-S01 catalog/variant implementation completed and is ready for review. | Codex |
| 2026-09-04 | M04-S01 implementation started after the approved catalog/variant plan. | Codex |
| 2026-09-04 | Project owner approved the M03 exit gate; M04-S01 catalog/variant implementation plan created. | Project owner / Codex |
| 2026-08-03 | Project owner approved M03-S06. | Project owner |
| 2026-08-02 | Project owner approved and merged M03-S05. M03-S06 isolation and authorization campaign completed. Status -> REVIEW. | Codex |
| 2026-08-02 | M03-S05 connected real seller identity, workspace, membership, permission, and audit UI. | Codex |
| 2026-08-02 | M03-S04 completed live policy RBAC, append-only audit events, and Owner-issued read-only PlatformSupport access. | Codex |
| 2026-08-02 | Project owner approved and merged M03-S02 (including SMTP amendment) and M03-S03. | Project owner |
