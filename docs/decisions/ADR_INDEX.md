# Architecture Decision Record Index

## How to use

- New ADRs use `docs/decisions/ADR_TEMPLATE.md` as the starting template.
- Number ADRs sequentially: `ADR-001`, `ADR-002`, etc.
- File name format: `ADR-NNN-short-title.md`
- Update this index when creating, accepting, or superseding an ADR.

## Status definitions

| Status | Meaning |
|---|---|
| `Proposed` | Under discussion; not yet binding |
| `Accepted` | Approved and binding on implementation |
| `Superseded` | Replaced by a later ADR (link to successor) |
| `Rejected` | Evaluated and declined (retained for history) |

## Active decisions

No ADRs have been created yet. The baseline architecture is defined in `design_files/plan.md` Sections 10–11 and the invariants listed in `CLAUDE.md`.

## Index

| ADR | Title | Status | Date | Superseded by |
|---|---|---|---|---|
| — | — | — | — | — |

## Baseline architecture (pre-ADR)

The following decisions are documented in `design_files/plan.md` §10.2 and §10.13 and are treated as accepted unless a future ADR changes them:

- Next.js + TypeScript frontend
- ASP.NET Core .NET 10 modular monolith backend
- PostgreSQL + EF Core with mandatory tenant scoping
- ASP.NET Core Identity with policy-based RBAC
- Hangfire + PostgreSQL storage (Redis optional, not MVP)
- S3-compatible storage, initially Cloudflare R2
- Docker + .NET Aspire locally; GitHub Actions/GHCR for delivery
- COD + merchant QR/manual verification for MVP payments
- One production-validated social channel for MVP
- Provider-neutral AI orchestration
