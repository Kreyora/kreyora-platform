# M05 Exit Gate — Storefront, Delivery, Checkout, and Canonical Orders Completion Plan

## Goal

Prove that the six Milestone 05 exit-gate criteria are fully satisfied using existing approved evidence (M05-S01 through M05-S07) plus a manual Docker Compose seller-to-COD-checkout walkthrough. No new features, tests, code, or quality-gate re-runs.

## Authority

- Milestone 05 exit gate criteria: `docs/milestones/05_STOREFRONT_CHECKOUT_ORDERS.md` lines 73–81.
- ADR-009: Public storefront resolution boundary (M05 public checkout is COD-only).
- ADR-007: Checkout-session reservation orchestration.
- ADR-008: Commerce-system provenance.
- Invariant matrix: `docs/architecture/STOREFRONT_CHECKOUT_INVARIANT_MATRIX.md`.

## Exit criteria mapping

| # | Criterion | Primary automated evidence | Manual evidence |
|---|---|---|---|
| 1 | Fresh seller → store → publish | PublicStorefrontEndpointTests (seeded fresh per test) | Docker Compose seller walkthrough |
| 2 | COD checkout path | PublicStorefrontEndpointTests full flow + Playwright E2E | Docker Compose public checkout at mobile + desktop |
| 3 | Server owns commerce facts | Invariant matrix: "Browser sends intent only" + "Server owns price, fees, status, publication" | Browser network inspection during manual checkout |
| 4 | Immutable snapshots | Invariant matrix: "Order snapshots are immutable" | — (fully automated) |
| 5 | Reservation safety | Invariant matrix: "Last unit is never oversold" + "Expiry is safe and repeatable" + M04-S06 contention | — (fully automated) |
| 6 | Public routing isolation | Invariant matrix: "Store selection is not caller-controlled" + "Public errors do not aid enumeration" + ADR-009 | — (fully automated) |

## Execution steps

1. Verify clean source state (`git status`, `git diff --check`, `git log -n 1`).
2. Run Docker Compose with seed data; verify all services healthy.
3. Walk authenticated seller path: store admin → readiness → publication.
4. Walk anonymous public path: catalog → product → cart → checkout → COD order → confirmation.
5. Verify at mobile (~375px) and desktop (~1280px) widths.
6. Inspect browser network traffic: no tenant/store IDs or commerce-authority fields in request bodies.
7. Create `artifacts/checkpoints/M05-EXIT.md` with status `REVIEW`.
8. Update `docs/context/CURRENT_WORK.md`.
9. Stop and await project-owner exit-gate approval.

## Prohibited scope

- New code, tests, migrations, or API changes.
- Re-running quality gates (no source changes since M05-S07).
- Merchant QR, public order lookup, seller order UI, M06.
- Commits, pushes, deployments, external contact.

