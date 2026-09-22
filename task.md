# Handoff: Milestone 08 Step 01 — Provider Readiness Evidence and Adapter Contract Plan

## 1. Overview

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 01 — Provider readiness evidence and adapter contract plan
- **Phase:** Phase 2 (Builder) Complete — REVIEW
- **Governing Plan:** `docs/plan/M08-S01_PROVIDER_READINESS_PLAN.md`
- **Active Milestone File:** `docs/milestones/08_FIRST_SOCIAL_CHANNEL_AND_INBOX.md`
- **Previous Checkpoint:** `artifacts/checkpoints/M07-EXIT.md` (APPROVED)
- **Status:** `PLANNING`

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Create ADR-014 (First Social Channel Adapter Selection)**
  - Create `docs/decisions/ADR-014-*.md` as `Proposed` only (never `Accepted` in this step), pending owner access confirmation and final choice.
  - Update `docs/decisions/ADR_INDEX.md` registering ADR-014 as `Proposed`.
  - If public docs are insufficient for a safe recommendation, skip ADR-014 and classify the milestone `BLOCKED` with enumerated gaps instead.

- [x] **Task 2: Produce Provider Readiness Evaluation Matrix Document**
  - Create `docs/architecture/PROVIDER_READINESS_EVALUATION.md` documenting the evidence matrix for the 3 priority candidates (WhatsApp, Messenger, Instagram).
  - Every cell must be `verified` (official doc URL + version/access date), `unsupported` (official source), or `unknown` (`[UNRESOLVED]` gap). No uncited provider facts.

- [x] **Task 3: Refine Domain Capability Models & Preset**
  - Verify `ChannelCapabilities` presets in `Kreyora.Domain.Integrations` against cited evidence; correct only where evidence contradicts.
  - No behavior change beyond evidence.

- [x] **Task 4: Implement Application Contracts & Fallback UX (outcome (a) only)**
  - Create `InstagramContracts.cs` in `Kreyora.Application.Integrations.Instagram` with credential models (no secrets), window status, and fallback UX rules — only if the evidence supports a recommendation.
  - Each fallback-UX rule needs a citation or `[UNRESOLVED]` marker.

- [x] **Task 5: Implement Unit and Contract Tests (outcome (a) only)**
  - Add `InstagramCapabilityTests.cs` in `Kreyora.UnitTests`.
  - Add `InstagramProviderContractTests.cs` in `Kreyora.ContractTests`.
  - Tests assert only cited evidence; no live network, no secrets or personal payloads in snapshots.

- [x] **Task 6: Quality Gates & Review Checkpoint**
  - Run full backend build and test suite (`536+` tests passing).
  - Check 0 pending EF model changes.
  - Run frontend CI (`pnpm ci:frontend`).
  - `git diff --check` clean.
  - Create review checkpoint `artifacts/checkpoints/M08-S01.md` with status `REVIEW`, declaring outcome (a) recommendation or (b) `BLOCKED`.
  - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/08_FIRST_SOCIAL_CHANNEL_AND_INBOX.md`.
