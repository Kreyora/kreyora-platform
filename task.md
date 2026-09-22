# Handoff: Milestone 08 Step 02 — Connection / Credential Lifecycle (Instagram)

## 1. Overview

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 02 — Connection/OAuth or credential lifecycle
- **Phase:** Phase 2 (Builder) Complete — REVIEW
- **Governing Plan:** `docs/plan/M08-S02_CONNECTION_CREDENTIAL_PLAN.md`
- **Active Milestone File:** `docs/milestones/08_FIRST_SOCIAL_CHANNEL_AND_INBOX.md`
- **Previous Checkpoint:** `artifacts/checkpoints/M08-S01.md` (APPROVED 2026-09-22)
- **Status:** `PLANNING`
- **Precondition for build:** ADR-014 marked `Accepted` by project owner (currently `Proposed`).

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Application contract `IInstagramGraphClient`**
  - New file in `Kreyora.Application.Integrations.Instagram`; typed validate/health results; no HTTP.
- [x] **Task 2: Infrastructure `InstagramGraphClient`**
  - `HttpClient` to versioned Graph API; error mapping (190/10/429/5xx/timeout); token redaction in logs.
- [x] **Task 3: Connection lifecycle in `ChannelConnectionService`**
  - Instagram live-validate-before-persist on create; reauthorize on update; health routing; audit events; RBAC unchanged.
- [x] **Task 4: DTO options + capability/health refresh**
  - `InstagramConnectOptions`; refresh capabilities/health records from real responses; no secret leakage (DTO shape already safe).
- [x] **Task 5: Tests (stubbed HTTP + Testcontainers Postgres)**
  - `InstagramGraphClientTests`, `ChannelConnectionInstagramTests`, integration persist/health/audit/isolation tests. Recorded-safe fixtures only; no real IDs/tokens.
- [x] **Task 6: Quality Gates & Review Checkpoint**
  - Release build 0/0; full suite green with Docker; 0 pending migrations; frontend CI green; `git diff --check`; no-hardcoded-graph-host + no-secret grep proofs.
  - Checkpoint `artifacts/checkpoints/M08-S02.md` with status `REVIEW`; update `CURRENT_WORK.md` + milestone file; owner live-sandbox checklist recorded.
