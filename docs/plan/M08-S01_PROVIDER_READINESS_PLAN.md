# M08-S01 — Provider Readiness Evidence and Adapter Contract Plan

## 1. Identity & Context

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 01 — Provider readiness evidence and adapter contract plan
- **Author:** Phase 1 (Architect); revised 2026-09-22 after plan verification
- **Date:** 2026-09-22
- **Status:** `PLANNING` — Awaiting human approval before implementation

### Owner constraints (2026-09-22, binding on this step)

- Provider access today: **none confirmed yet** (no developer/test account).
- Account/app owner: `me` (personal; company ownership unresolved).
- Docs/credentials: **public official documentation only**; no credentials available. Never paste secrets into chat or Git.
- Priority candidates: **WhatsApp, Instagram, Messenger** (TikTok explicitly deferred; Telegram/Viber evaluated only if cheap — see §3).
- Decision preference: evaluate and stop with a recommendation; **no provider is selected until access is confirmed and the owner makes the final choice**. ADR-014 (if drafted) stays `Proposed`, never `Accepted`, in this step.

## 2. Objective

Evaluate the three priority candidates against the accepted capability template using **current official provider documentation only**. Record evidence for inbound/outbound features, conversation windows/templates, webhook signatures, verification challenge, token/scopes/refresh, rate limits, media, delivery receipts, sandbox limitations, app review, production restrictions, data retention, and costs. Either draft ADR-014 as `Proposed` with a ranked recommendation, or report blocking gaps and classify the milestone `BLOCKED` per the hard gate. Map only verified capabilities onto `IChannelProvider`; list unsupported/unknown features with fallback UX. **Do not write the adapter until an ADR is accepted.**

## 3. Candidate scope

- **In scope:** WhatsApp Business Platform (Cloud API), Messenger Platform, Instagram Messaging (Messenger API for Instagram / Graph API).
- **Deferred:** TikTok (owner-deferred; would need a channel-model ADR since `ChannelType` has no TikTok value), Telegram Bot API, Viber Bot API (out of priority scope; may be noted in one line as alternatives, not full matrix rows).

## 4. Hard gate (milestone §Dependencies)

Milestone 08 requires: M07 exit approved (done) + chosen provider in an **accepted** ADR + docs, app/account ownership, sandbox access, review requirements, retention terms, rate limits, signing rules, token lifecycle, outbound policy all available. **None of the account/sandbox items are available today.** Therefore Phase 2 for S01 must end in exactly one of:

- (a) Evidence matrix complete on public docs + ADR-014 drafted as **`Proposed`** + ranked recommendation + named blocking gaps (test app, sandbox number/Page/Professional account, review prerequisites), milestone stays open pending owner access confirmation; or
- (b) Public docs insufficient for a safe recommendation → complete only Prompt 01, classify milestone **`BLOCKED`** with enumerated gaps, no ADR, no imitation adapter.

Phase 2 must state which outcome was reached in the checkpoint. No S02 work under either outcome.

## 5. Evidence matrix (to fill in Phase 2 — every cell mandatory)

Each cell must resolve to `verified` (official doc URL + version/access date + quote or location), `unsupported` (official doc URL stating absence), or `unknown` (blocking gap, marked `[UNRESOLVED]`). **Uncited provider facts are fabrication — prohibited.**

| Dimension | WhatsApp Cloud API | Messenger Platform | Instagram Messaging |
|---|---|---|---|
| API / docs version + access date | TBD | TBD | TBD |
| App/account ownership + sandbox path | TBD | TBD | TBD |
| Webhook verification challenge mechanics | TBD | TBD | TBD |
| Request signature scheme + replay-window rules | TBD | TBD | TBD |
| Token types / scopes / expiry / refresh / revoke | TBD | TBD | TBD |
| Inbound text (limits) | TBD | TBD | TBD |
| Inbound media (types, size caps) | TBD | TBD | TBD |
| Reactions | TBD | TBD | TBD |
| Delivery / read receipts | TBD | TBD | TBD |
| Deleted / unsupported-type / retry-duplicate semantics | TBD | TBD | TBD |
| Outbound text / media / link preview | TBD | TBD | TBD |
| Templates + conversation-window rules | TBD | TBD | TBD |
| Rate limits (throughput, caps) | TBD | TBD | TBD |
| Sandbox limitations | TBD | TBD | TBD |
| App review / production prerequisites | TBD | TBD | TBD |
| Data-retention terms | TBD | TBD | TBD |
| Costs / billing model | TBD | TBD | TBD |

## 6. Working hypotheses (marked, not facts)

- `[ASSUMPTION]` Instagram DMs are the primary conversion channel for Nepalese boutique/fashion sellers ("DM for price/order").
- `[ASSUMPTION]` Meta API calls carry no per-conversation fee burden comparable to WhatsApp conversation billing for bootstrap sellers — to be verified against current pricing docs in Phase 2.
- `[ASSUMPTION]` Meta Developer Mode permits full webhook + reply testing with test accounts without production App Review — to be verified against current docs in Phase 2.
- Prior draft's specifics (API `v21.0`, char limits, error `100`/subcode `2018001`, Viber pricing) are **discarded as unverified** until re-sourced per §5.

## 7. Capability mapping onto `IChannelProvider`

- For each `verified` cell, state which contract member it satisfies: `ValidateWebhookAsync`, `NormalizeInboundAsync`, `SendMessageAsync`, `ValidateOrRefreshConnectionAsync`, `Capabilities` (`services/api/src/Kreyora.Application/Integrations/IChannelProvider.cs:5-26`).
- Reference live preset `ChannelCapabilities.InstagramGraphApi()` (`services/api/src/Kreyora.Domain/Integrations/ChannelCapabilities.cs:31-43`) and correct it only where evidence contradicts it.
- Unsupported/unknown items each get a fallback-UX entry (denial reason, degraded display) as input to M08-S06 — not built here.

## 8. Fallback UX candidates (each needs citation or `[UNRESOLVED]` in Phase 2)

1. **24-hour window enforcement** — `[UNRESOLVED]`: exact error code/subcode, `HUMAN_AGENT`-equivalent extension mechanics and duration per candidate.
2. **Missing delivery receipts** — `[UNRESOLVED]`: per-candidate receipt semantics; UI must never show a misleading intermediate state.
3. **Media delivery** — `[UNRESOLVED]`: provider fetch requirements (public HTTPS vs signed URL); R2 signed-URL flow must stay within the existing storage invariant, no new storage ADR assumed.

## 9. Implementation Scope for Step 01

### Allowed Scope

1. Create `docs/decisions/ADR-014-*.md` as **`Proposed`** (only under outcome (a)) + register in `docs/decisions/ADR_INDEX.md` as `Proposed`. Never mark accepted.
2. Create `docs/architecture/PROVIDER_READINESS_EVALUATION.md` with the §5 matrix, all cells resolved.
3. Verify/correct `ChannelCapabilities` presets against evidence (no behavior change beyond evidence).
4. Create `services/api/src/Kreyora.Application/Integrations/Instagram/InstagramContracts.cs` (only under outcome (a)): credential models (no secrets), window-status and capability-restriction models.
5. Unit + contract tests validating capability mappings and provider invariants only (no live network; no secrets or personal payloads in snapshots).
6. Checkpoint `artifacts/checkpoints/M08-S01.md` with status `REVIEW`, declaring outcome (a) or (b).

### Prohibited Scope

- Accepting ADR-014, writing any network adapter (`*ChannelProvider` with live calls), OAuth/token endpoints, conversation-entity migrations.
- External network calls, developer-app creation, sandbox-credential use.
- Storing secrets, tokens, or personal payloads anywhere in the repo.
- TikTok/Telegram/Viber adapter scope; channel-model changes.
- Modifying accepted ADRs (ADR-001–ADR-013).
- Starting M08-S02 under either outcome.

## 10. Quality Gates

- `dotnet build services/api/Kreyora.slnx --configuration Release --disable-build-servers /m:1`
- `dotnet test services/api/Kreyora.slnx --configuration Release --no-build`
- `dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --no-build`
- `pnpm ci:frontend`
- `git diff --check`
- Plan-level gate: zero uncited provider facts; every matrix cell resolved or `[UNRESOLVED]` with named gap.
