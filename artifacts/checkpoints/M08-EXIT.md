# Milestone 08 Exit Gate Checkpoint — First Validated Social Channel and Unified Inbox

## Identification

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** M08 Exit Gate — Milestone Completion Review
- **Date:** 2026-10-06
- **Implementer:** Claude
- **Branch / head:** `master` at `fd5a7ec` (S07 merged in PR #117; review written at `fa91a38` + uncommitted S07)
- **Status:** `APPROVED` (2026-10-06; was `REVIEW`)

## Scope completed

All seven M08 steps are `APPROVED`:

| Step | Outcome | Checkpoint |
|---|---|---|
| S01 | Provider readiness evidence; Instagram Messaging chosen (ADR-014) | `M08-S01.md` |
| S02 | Instagram connection lifecycle: validated connect, live-validated reauthorize, health routing | `M08-S02.md` |
| S03 | Real webhook validation and normalization, plus the corrective build (ADR-015: global account ownership, per-account fan-out, event-level dedup, stale-processing recovery) | `M08-S03.md` |
| S04 | Customer identities, conversations, messages, reactions, labels; erasure service (ADR-016) | `M08-S04.md` |
| S05 | Staff reply through the durable outbox, implicit takeover, assignment, status actions, takeover invariant, Instagram Send API (ADR-017) | `M08-S05.md` |
| S06 | Unified inbox frontend on the real API with demo-mode parity; Hangfire scheduling of processing and delivery (closed an M07 gap); real-backend role E2E | `M08-S06.md` |
| S07 | Readiness fixes, sandbox tooling, Meta documentation verification, **live sandbox session**; production classified `BLOCKED` | `M08-S07.md` |

This review relies on two kinds of evidence:
- the live Meta sandbox session (2026-10-05/06) for the core path;
- automated suites (real PostgreSQL via Testcontainers, contract, architecture, unit, real-backend E2E) for everything else.

Where live evidence is missing, the table says so.

## Source revision and diff hygiene

- `git log -1 --oneline`: `fa91a38 Merge pull request #115 from Kreyora/feat/master/m08-s06`
- Uncommitted: the M08-S07 changes listed in `M08-S07.md` "Files changed", plus the approval records. No unrelated changes.
- `git diff --check`: clean.

## Exit criteria and evidence

| # | Exit criterion (milestone file) | Live evidence | Automated evidence | Status |
|---|---|---|---|---|
| 1 | **Exactly one evidence-backed provider adapter exists** | Instagram adapter used against the owner's Live Meta app (S07 S2–S8) | `InstagramCapabilityTests`, `InstagramChannelProviderTests`, `InstagramGraphClientTests`, `InstagramSendTests`, `ChannelProviderContractTests`. Meta requirements cited in `INSTAGRAM_PRODUCTION_READINESS.md` R1–R14 | **Satisfied** |
| 2 | **A verified sandbox inbound message reaches the correct tenant conversation once** | A real DM was App-Secret signature-verified → 1 webhook event `Processed` → 1 inbound event → 1 conversation in the connection's workspace → 1 message. A duplicate delivery (Meta's echo, forwarded twice) was detected and acknowledged; 0 repeated dedup keys, 0 repeated message IDs (S07 S4/S5) | `InstagramWebhookIntegrationTests`, `InstagramWebhookCorrectiveReproTests` (18), `ConversationIngestionIntegrationTests` (11), `WebhookIngressIntegrationTests`, `Milestone07ReliabilityAndFailureTests` (duplicate storm) | **Satisfied** |
| 3 | **Authorized staff reply works through the durable outbox** | An inbox reply went outbox → delivery job → `POST /me/messages` → `Sent`, and arrived on the customer phone; Meta's echo merged with no extra row (S07 S8) | `ConversationReplyIntegrationTests` (22: authorization, idempotency key, gate, denials, at-most-once on ambiguous sends), `OutboundMessageIntegrationTests`, real-backend E2E (Operator workflow, Viewer 403) | **Satisfied** |
| 4 | **Assignment and human takeover work, and automation cannot send after takeover** | Assignment, label, hand-back and takeover performed and audited (`conversations.takeover` ×2, `assigned`, `released`). **Live automation sending can't be exercised**, because no automation sender exists until M09 | `Automation_IsDeniedAtEnqueue_AfterTakeover`, `Race_TakeoverCancelsQueuedAutomation_ProviderNeverCalled`, `Race_AutomationThatSlippedPastTakeover_IsCancelledAtDelivery`, `Race_TakeoverBetweenGateReadAndMarkSending_DeliveryLosesAndNeverSends`, `InFlightAutomationAtTakeover_IsReportedInAudit_NotHidden`, `Release_ResumesAutomation_AndStatusMachineIsEnforced`; real-backend E2E admin takeover/hand-back | **Satisfied, with limitation L-A** |
| 5 | **Provider errors, token expiry, retries, DLQ, and replay are visible** | Provider refusals were visible live during setup: connect refused with a precise reason (L1); wrongly signed deliveries refused 401 and logged. **Revoke → expiry → reauthorize and live replay were not run** (S07 S11–S13 skipped by the owner) | `TokenExpiredOnSend_MarksConnectionExpired`, `CheckInstagramHealth_MapsExpiredTokenToExpiredStatus`, `CreateInstagramConnection_WithExpiredToken_PersistsNothing`, `ReauthorizeInstagramConnection_ReplacesCredentialsAndAudits`, `ProviderRetry_ThrottleThenSuccess_OneTimelineRow_TwoAttempts`, `ProcessWebhookEvent_RetryExhaustion_TransitionsToDeadLetter`, `ReplayWebhookEvent_DeadLetterEvent_ResetsToReceivedAndReprocesses`, `ReplayMessage_DeadLetteredMessage_ResetsToQueued`; inbox shows failure codes (`deliveryFailureCode`, S06) and links to diagnostics (M07-S06 UI) | **Satisfied by automated evidence, with limitation L-B** |
| 6 | **Production claims match actual approval status** | Production connection classified **`BLOCKED`**: no Advanced Access, Business Verification or App Review; S11–S13 not live. Demo mode is labelled in the UI; nothing claims live readiness | `INSTAGRAM_PRODUCTION_READINESS.md` §2 (P1–P7) and §5; ADR-014 status note | **Satisfied** |

### Limitations recorded for the owner's decision

- **L-A (criterion 4):** "automation cannot send after takeover" is proven by six integration tests covering enqueue, delivery and race paths, not live. **Carry-forward:** M09, the first real automation sender, must re-verify it live against the sandbox.
- **L-B (criterion 5):** token expiry, reauthorization, DLQ and replay are proven by automated tests, plus the M07 simulator scenarios and diagnostics UI. They were not exercised against Meta live.
  - **Option:** run S07 S11–S13 (about 15 minutes; the sandbox database is kept) before approving this gate. That turns L-B into live evidence and lets production move to `CONDITIONALLY READY`.

## Milestone scope check

- **Locked invariants respected:**
  - exactly one channel adapter;
  - provider-neutral contracts (M07) unchanged at the boundary;
  - AI not involved;
  - COD/QR untouched;
  - Hangfire with PostgreSQL;
  - no Redis;
  - no fabricated provider behavior. All provider facts are cited or observed (L1–L4).
- **ADRs in force:** ADR-014 (with status note), ADR-015, ADR-016, ADR-017 (with correction note).
- **Identity:** customer identity links are scoped to one connection and tenant; no cross-channel merging (ADR-016).

## Quality gates (latest full runs, 2026-10-05/06, on the S07 code)

| Gate | Result |
|---|---|
| `dotnet build` (Release) | 0 warnings, 0 errors |
| Backend tests | **773 passed, 0 failed** (unit 462, contract 16, architecture 6, integration 289) |
| EF pending model changes | None |
| `pnpm ci:frontend` | Green: 473 tests, build OK, lint 0 errors |
| Fixture E2E / real-backend E2E | 2 passed / 7 passed |
| Sandbox tool tests | 13 passed |
| `git diff --check` | Clean |

## Known issues and risks carried forward

| Severity | Issue | Owner | Carry to |
|---|---|---|---|
| **High (security, open)** | App Secret and tokens were shared in chat during S07 | Owner | Now: reset the App Secret; remove/regrant app access |
| High | Production connection `BLOCKED` (Business Verification, App Review incl. `pages_read_engagement`, Human Agent optional) | Owner | Long-lead, external |
| Medium | L-A and L-B (above) | Owner / M09 | M09 (L-A); optional S11–S13 (L-B) |
| Medium | Seller onboarding: OAuth connect and the "Allow access to messages" guidance; webhook configured in the main Webhooks product (L2) | Kreyora | M10 |
| Medium | Client-IP forwarding for per-address auth limits; public TLS callback; deployment | Kreyora | M11 |
| Low | Seller pages request data before sign-in finishes (dev overlay); generated OpenAPI enums typed as numbers; Hangfire 15 s queue poll | Kreyora | Later steps |
| Info | Sandbox database `kreyora-sandbox-pg` (stopped) and volume `kreyora_sandbox_pgdata` kept; the Meta app's webhook callback points at a closed tunnel | Owner | Decide: finish S11–S13, or destroy and update/remove the callback |

## Reviewer procedure

1. Read the "Exit criteria and evidence" table and limitations L-A/L-B.
2. Spot-check the live evidence in `M08-S07.md` ("Live evidence") and `INSTAGRAM_PRODUCTION_READINESS.md` §5.
3. Optionally re-run `dotnet test services/api/Kreyora.slnx -c Release` and `pnpm ci:frontend`.
4. Decide:
   - approve with L-A/L-B carried forward; **or**
   - run S11–S13 first and then approve; **or**
   - request changes.

**Manual work for the owner:**
- this review;
- the security follow-ups (App Secret reset, app access);
- the sandbox database decision;
- the long-lead Meta items: Business Verification, then App Review.

## Approval

- **Reviewer:**
- **Decision:** `APPROVED` / `CHANGES REQUESTED` / `BLOCKED`
- **Notes:**
- **Next allowed prompt:** after approval, M09-S01 planning only (AI assistant). Implementation needs plan approval.

The next milestone was not started as part of this review.

## Approval (2026-10-06) — appended

- **Reviewer:** Project owner
- **Decision:** `APPROVED` ("okay approve the m08 exit gate and start the planning")
- **Notes:** All six exit criteria accepted.
  - **L-A carried to M09-S07:** live check that automation is suppressed after takeover, on the reused M08-S07 Instagram sandbox.
  - **L-B accepted:** token expiry, reauthorization and replay rest on automated evidence. Running S07 S11–S13 live is optional.
  - **Production connection stays `BLOCKED`.**
  - **Owner security follow-ups (App Secret reset, app access) deferred by the owner**, who is reusing the sandbox. To be done before any real seller or customer use.
- **Next allowed prompt:** M09-S01 planning (plan only).
