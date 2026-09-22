# ADR-014 — First Social Channel: Instagram Messaging (Proposed)

- **Status:** `Proposed`
- **Date:** 2026-09-22
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** Milestone 08

> This ADR is a **proposal only**. It must not be marked `Accepted`, and no adapter code may be written, until the project owner confirms provider access and makes the final choice (M08-S01 owner constraints, 2026-09-22).

## Context

Milestone 08 requires exactly one evidence-backed provider adapter (invariant: one production-validated channel for MVP). M08-S01 evaluated WhatsApp Cloud API, Messenger Platform, and Instagram Messaging against current public official Meta documentation (no account or sandbox access exists yet). Full matrix: `docs/architecture/PROVIDER_READINESS_EVALUATION.md` (accessed 2026-09-22).

Verified facts driving this proposal:

1. Instagram onboarding needs an Instagram professional account + linked Facebook Page + `MODERATE`-capable developer + Meta app — no phone number, no per-message billing (sources S4, S6-scope).
2. Meta publishes per-message template billing only for WhatsApp, including Nepal-specific utility/auth rates — a real cost/operations burden for bootstrap sellers (source S3).
3. Instagram exposes `messaging_seen` read receipts plus `is_echo` / `is_deleted` / `is_unsupported` echo flags, matching the M07 normalized envelope and monotonic `Sent → Read` progression without a misleading `Delivered` state (sources S8, S6).
4. The 24h standard window + 7-day HUMAN_AGENT manual-response extension is shared across Messenger/Instagram and requires App Review + business verification (sources S6, S7) — equal review burden, so it does not discriminate between the Meta candidates.

## Decision (proposed)

Adopt **Instagram Messaging (Messenger API for Instagram)** as the first channel adapter, subject to owner access confirmation and ADR acceptance in a later step.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| WhatsApp Cloud API | Richest receipts (delivered + read), E.164 identity, test-number sandbox in API Setup | Per-message template billing incl. Nepal rates; phone-number operations + portfolio messaging-limit tiers | Deferred. Kept as fallback if IG sandbox proves unworkable. |
| Messenger Platform | Same stack as IG plus delivery receipts; broad reach | Weaker boutique-seller fit `[ASSUMPTION]`; PSID identity less portable for sellers living on IG | Deferred. Second fallback. |
| Telegram / Viber / TikTok | — | Out of owner priority scope; TikTok absent from `ChannelType` | Deferred without evaluation. |

## Consequences

- **Product impact:** First inbox vertical targets IG-DM sellers; UI must handle `Sent → Read` without `Delivered`, invisible GIF/sticker/disappearing inbound, and 24h-window denial with HUMAN_AGENT path.
- **Architecture impact:** Future adapter implements `IChannelProvider` with `ChannelCapabilities.InstagramGraphApi()` (already matching; no change in S01). No adapter code in S01.
- **Security/privacy impact:** PAT lifecycle (non-expiring long-lived PAT), App Secret HMAC verification, ADR-012/013 unchanged. Data-retention terms `[UNRESOLVED]` — must close before production claims.
- **Cost/operations impact:** No per-message API fees documented; App Review + business verification effort outstanding.
- **Migration or rollback impact:** None in S01 (no schema/code behavior change). If owner chooses differently, this ADR is `Rejected` and the evaluation doc is reused.

## Validation evidence

- `docs/architecture/PROVIDER_READINESS_EVALUATION.md` — every matrix cell cited or `[UNRESOLVED]`.
- `InstagramCapabilityTests`, `InstagramProviderContractTests` — preset mapping + no-adapter invariant.
- Acceptance requires: owner-confirmed test app + IG professional account + linked Page, observed sandbox webhook, App Review plan.

## Supersession conditions

- Owner selects a different provider; sandbox evidence contradicts the matrix; Meta publishes materially different pricing/window/permission terms.
