# M08-S07 — Sandbox / Production-Readiness Verification — Scoped Plan

## Identity

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 07 — Sandbox/production-readiness verification (last M08 step; the M08 exit gate follows)
- **Author:** Claude (planning)
- **Date:** 2026-10-05
- **Status:** `APPROVED` 2026-10-06 (with documented gaps); was `REVIEW` (2026-10-06; live session partly executed: S11–S13 skipped by the owner); was `IN PROGRESS` — approved by the owner on 2026-10-05 ("ok implement"): decisions Q1–Q6 as recommended
- **Prerequisites:** M08-S06 `APPROVED` (merged to `master` at `fa91a38`); ADR-014/015/016/017 `Accepted`.
- **Manual work for the owner in this step: yes, this is the hands-on step.** See "Owner preparation" (some items take time, so start them early) and the live session script (Part D). The owner performs every Meta-side action and every test DM. Claude never touches Meta configuration, never sees a secret, and never sends a message to a real person.

## Milestone prompt (verbatim)

> Execute the official sandbox end-to-end path: connect, receive a real test customer message, deduplicate a retry, create/update conversation, assign, staff reply, receive delivery status if supported, take over, suppress automation, expire/revoke credentials, diagnose, reauthorize, and replay a safe failed event. Verify rate/window behavior and document every production-review prerequisite still outstanding. Do not claim production readiness without accepted credentials and review evidence.

**Review checkpoint:** approve sandbox evidence and explicitly classify production connection as `READY`, `CONDITIONALLY READY`, or `BLOCKED`.

## What exists today (verified 2026-10-05)

| Prompt item | Current support | Gap for a live run |
|---|---|---|
| Connect | `POST /v1/connections` with `PlainTextSecret` (Page token), `Instagram.PageId`, `Instagram.InstagramAccountId`; validated live against Graph before persisting (S02) | **No UI**: "Connect Channel" is disabled (onboarding is M10). There is no safe way for the owner to enter the token |
| Receive a real message | `GET/POST /v1/webhooks/instagram`: app-level verify token, HMAC with the App Secret, payload-first routing, fan-out (S03, ADR-015); ingestion into conversations (S04) | Needs a public HTTPS callback (tunnel); **no tunnel tool is installed**; **no user secrets are configured** |
| Deduplicate a retry | Event-level `DeduplicationKey` + unique constraints (S03) | Meta redelivery can't be forced on demand |
| Conversation / assign / reply / takeover | S04–S06; inbox UI on the real API | none |
| Delivery status | `messaging_seen` → `Read`; the Send API response → `Sent`; Instagram has no `Delivered` (ADR-014) | none |
| Suppress automation | Gate denies automation after takeover (ADR-017; integration-tested) | **No automation sender exists until M09**, so it can't be triggered live |
| Expire/revoke, diagnose, reauthorize | Graph error 190 → connection `Expired` (S05); `POST /connections/{id}/health`; `PUT /connections/{id}` reauthorize with live validation (S02); diagnostics UI (M07-S06) | Reauthorize also has no UI (same as connect) |
| Replay a safe failed event | `POST /webhooks/{id}/replay` for `Failed`/`DeadLetter`, plus a Replay button in diagnostics | **No safe way to produce a real failed inbound event.** Ingress drops events for Disabled/Revoked connections without storing them, and processing starts immediately, so there is no time to disable a connection between storing and processing |
| Window | `ConversationGate` enforces 24 h locally (`window_closed`); `HUMAN_AGENT` is off | A live check needs a conversation whose last customer message is more than 24 h old |
| Rate limits | Graph codes 4/17/32/613 and HTTP 429 treated as transient (`InstagramGraphClient.cs:240`) | Throttling must not be provoked against Meta; documentation only |

**Open owner findings from S06 that matter here:**

1. The Simulator accepts a fixed signature in every environment.
2. The sign-in limiter is one global window: 5 sign-ins per 15 minutes in total.

Both matter more once the local API is reachable from the internet.

## Objective

Prove the Instagram adapter end to end against Meta's real sandbox, with real test DMs, using only the official path. Record evidence with no identifiers, tokens or message content. Write down every production prerequisite that is still outstanding, and classify production connection honestly.

## Design

### A. Readiness work before the live session (small, tested)

- **A1 — Simulator signature bypass (S06 finding 1).** `sha256=valid_test_signature` is accepted only when the environment is Development or Testing; elsewhere the real HMAC is required. Tests for production, Development and Testing behavior.
- **A2 — Sign-in limiter partitioning (S06 finding 2).** Partition `auth-sign-in` per client IP, with the existing limits (5 per 15 minutes per IP), following the existing `public-reads` partition helper.
  - Tests: two IPs don't share a budget; the 6th attempt from one IP gets 429.
  - The E2E harness keeps its once-per-persona sign-in.
- **A3 — Hangfire server switch.** Config `BackgroundJobs:ServerEnabled` (default `true`). When `false`, jobs are still enqueued but not executed.
  - This makes the safe failed-event scenario deterministic (D-S12). It is also the first piece of a future separate worker.
  - Tests: the default registers the server; `false` does not.
- **A4 — Owner-run connect/reauthorize script** (`scripts/sandbox/instagram-connection.mjs`, Node, no dependencies).
  - Signs in with the owner's account (the password is typed into a hidden prompt) and selects the workspace.
  - Reads the Page token from a **hidden prompt**: never echoed, never logged, never written to disk.
  - Calls `POST /v1/connections` (connect) or `PUT /v1/connections/{id}` (reauthorize) with CSRF.
  - Prints only the outcome code and the connection status.
  - This is a Development tool, not a product feature. Real onboarding (OAuth) is M10.
- **A5 — Webhook-only exposure proxy** (`scripts/sandbox/webhook-proxy.mjs`). The tunnel points at this proxy, never at the API.
  - It forwards **only** `GET` and `POST /v1/webhooks/instagram` to the local API, byte for byte, including signature headers. Everything else returns 404.
  - The rest of the API (sign-in, Hangfire dashboard, Simulator webhooks, diagnostics) is not reachable from the internet.
  - **Duplicate-next mode** (`d` + Enter in the proxy terminal): forwards the next Meta delivery to the API twice, with identical bytes and signature, and prints both status codes. This is the dedup test with real signed traffic, since a Meta redelivery can't be forced.
  - Logs show method, path, status, size and timing only, never bodies.
- **A6 — Sandbox runner** (`scripts/sandbox/run.mjs`), started by Claude:
  - a dedicated Postgres container `kreyora-sandbox-pg` with named volume `kreyora_sandbox_pgdata` (kept across sessions so the next-day window check can use it; removed at the end of the step with the owner's OK);
  - migrate (Development);
  - the API on `localhost:5030` with user secrets (names below);
  - the web app on `localhost:3000` with `NEXT_PUBLIC_API_URL`;
  - the proxy on `localhost:5031`.

  The project's Compose database is not used: it has a pending migration and its data is preserved.
- **A7 — Evidence collector** (`scripts/sandbox/evidence.mjs`). Read-only SQL against the sandbox database. Prints only:
  - counts by type and status (`webhook_events`, `inbound_events`, `conversations`, `messages`, `outbound_messages`, `outbound_delivery_attempts`, dead letters);
  - the maximum observed `mid` length;
  - delivery states;
  - connection status.

  No IDs, no names, no content. Its output goes into the checkpoint.

### B. Documentation verification (read-only, official Meta pages)

Re-verify current Meta requirements and record cited facts in `docs/architecture/INSTAGRAM_PRODUCTION_READINESS.md`:

- app Live/publish requirements: privacy policy URL, data-deletion URL or callback, app icon/category;
- which webhook events are delivered to Standard-Access apps (app-role users only?) and what makes someone an app-role tester for Instagram;
- permissions and access levels needed: `instagram_basic`, `instagram_manage_messages`, `pages_manage_metadata`, `pages_show_list`, and any others;
- App Review and Advanced Access; business verification; the `HUMAN_AGENT` feature review;
- Page-token lifetime (closes the ADR-014 "non-expiring" claim);
- Send API rate limits; webhook retry behavior; data-retention terms (closes an ADR-014 `[UNRESOLVED]` item if published).

Anything not documented stays `[UNRESOLVED]`. Nothing is inferred.

### C. Owner preparation (before the live session)

**Start the slow items now:**

1. **Meta app in Live mode** (if Part B confirms webhooks need it). This needs a public **privacy policy URL**, and likely a **data-deletion instructions URL**. Kreyora isn't deployed, so these must be hosted somewhere public. Any static page you control works.
2. **A second Instagram account to act as the customer**, belonging to a person with a role on the Meta app (tester/developer; Part B confirms the exact rule). Invites must be accepted, which can take a while.
3. **Page access token** with the permissions confirmed in Part B (Graph API Explorer).
4. **Tunnel tool:** authorize installing `cloudflared` (Homebrew; a quick tunnel needs no account) or provide your own (Q4).

**Configure, by name only. Values never go to chat or to files in the repo.** Run `dotnet user-secrets set <name> <value>` yourself in `services/api/src/Kreyora.WebApi`:

- `InstagramWebhook:AppSecret`
- `InstagramWebhook:VerifyToken` (any random string; you enter the same value in the Meta dashboard)
- `SecretEncryption:MasterKey` (the runner prints a command that generates a random key straight into user secrets, so the value is never displayed)

**Long-lead, optional:** App Review for `instagram_manage_messages` Advanced Access and the `HUMAN_AGENT` feature needs a screencast of the working flow. The live session (Part D) is a good moment to record it. Submitting is outside this step; the readiness document lists the requirements.

### D. Live session script (owner and Claude together, about 60–90 minutes, plus an optional 10 minutes the next day)

Claude runs local processes and the evidence collector. The owner does every Meta action and every DM. Results are recorded as pass/fail plus counts.

| # | Scenario | Who / how | Expected and recorded |
|---|---|---|---|
| S1 | Start the sandbox | Claude: runner (A6). Owner: tunnel to the proxy | API, web and proxy up; the tunnel URL goes to the owner by screen only |
| S2 | Webhook handshake | Owner: Meta dashboard callback `https://<tunnel>/v1/webhooks/instagram` + verify token; fields `messages`, `messaging_seen`, `message_reactions` | Dashboard verification succeeds; proxy shows `GET 200` |
| S3 | Connect | Owner: `subscribed_apps` for the Page (Graph Explorer); then the A4 script with the Page token | Connection `Active`; the live Graph validation passed |
| S4 | Real inbound message | Owner: DM from the tester account | Proxy `POST 200`; one webhook event → one inbound event → a new conversation in the inbox within seconds; unread badge |
| S5 | Dedup a retry | Owner: press `d` in the proxy, then send another DM | Two forwards (both 200); **one** stored event, **one** message. Any real Meta redelivery is recorded if observed |
| S6 | Update conversation | Owner: a second DM and a reaction | Same conversation; the reaction is attached; `mid` length recorded |
| S7 | Assign + label | Owner: inbox UI | State matches the API (evidence script) |
| S8 | Staff reply | Owner: reply in the inbox | `Sending…` → `Sent` (delivery job, Send API); the tester receives it |
| S9 | Delivery status | Owner: open the reply on the tester phone | `Read` appears (`messaging_seen`) |
| S10 | Take over / suppress automation | Owner: hand back to automation, then take over | Indicators flip. Live automation sending can't be triggered (no automation sender until M09); suppression is proven by the ADR-017 integration tests and is re-verified live at M09. Recorded as such |
| S11 | Expire/revoke → diagnose | Owner: remove the app under Facebook "Business integrations" (a real revoke), then reply from the inbox | Send fails with `190`; connection `Expired`; diagnostics show the failure; the inbox shows the failure copy. Health check confirms |
| S12 | Reauthorize | Owner: grant again, get a new Page token, A4 `reauthorize`; redo `subscribed_apps` if Meta dropped it | Connection `Active`; "Send again" on the failed reply → `Sent` |
| S13 | Replay a safe failed event | Claude: restart the API with `BackgroundJobs:ServerEnabled=false`. Owner: DM, then disable the connection. Claude: restart with the server on, so processing fails permanently (`Disabled`) → `DeadLetter`. Owner: enable, then **Replay** in diagnostics | One dead letter, visible with its reason; after replay the message appears once; replaying again is refused or a no-op |
| S14 | Window (next day, optional) | Owner: reply in a conversation whose last customer message is more than 24 h old | Inline "Reply window closed" (`window_closed`); no Send API call, so no outbound row. If skipped: recorded as "verified by tests only" |
| S15 | Rate limits | Not provoked against Meta | Documented limits (Part B) + existing transient-classification tests cited |
| S16 | Teardown | Owner: remove the Meta callback or leave it (their choice); stop the tunnel. Claude: stop processes | No listeners; tunnel closed |

**Evidence rules:** status codes, counts, states, timings and `mid` length only.

- No screenshots of real conversations: they contain the tester's identity and private messages.
- If the owner wants visual evidence, they can blur their own screenshots and keep them outside the repository.

### E. Production classification (decided from evidence, not in advance)

- **`READY`:**
  - all of S2–S13 pass;
  - Advanced Access for messaging is approved;
  - business verification is complete;
  - the Live-mode requirements are met;
  - no open security findings.
- **`CONDITIONALLY READY`:** S2–S13 pass, and every remaining item is an external approval or owner paperwork with no Kreyora code change needed. Each condition is listed.
- **`BLOCKED`:** any core scenario fails, or a required capability is missing in code (for example, a data-deletion callback if Meta requires one rather than an instructions URL).

The classification goes into the checkpoint and the readiness document. Production readiness is not claimed without accepted credentials and review evidence.

## Tests (automated, in the normal suites)

- **A1:** Simulator signature bypass by environment (unit + an HTTP integration test with the production environment → 401/403 for the fixed signature).
- **A2:** limiter partitioning by IP (integration: separate budgets; 429 on the 6th attempt from one IP).
- **A3:** server-switch registration (unit).
- **A4/A5/A7 scripts:** proxy allowlist and duplicate-next tested with a local stub upstream (Node test in `apps/web` or `scripts/`, no network).
- **Existing suites stay green:** backend, `ci:frontend`, fixture E2E, real-backend E2E.

## Contracts and migrations

- No API contract change (A1–A3 are behavioral and config only), no migration, no OpenAPI regeneration.
- New config key: `BackgroundJobs:ServerEnabled` (default `true`).

## Security and tenancy

- **Internet exposure** is limited to the Instagram webhook path through the A5 allowlist proxy; the tunnel exists only during sessions.
- **Secrets:**
  - App Secret, verify token and master key live in user secrets set by the owner.
  - The Page token goes only through the hidden prompt into the API, which encrypts it (ADR-013).
  - Nothing goes into chat, the repo, logs or evidence.
- **Real people:** the only person messaged is the owner's own tester account. Claude sends nothing; the owner presses Send.
- **Data:** the sandbox database is separate and disposable; it is removed at the end with the owner's OK. Evidence is aggregate only.
- **A1/A2** close the two S06 findings before exposure.

## Docker

- Record a baseline before the session.
- Create only `kreyora-sandbox-pg` and volume `kreyora_sandbox_pgdata`, kept until the step ends for S14.
- Remove both at the end, with confirmation.
- Never prune; never `down -v`. Project containers, volumes and data are untouched.

## Acceptance criteria

1. A1–A3 implemented with tests; full backend suite, EF (no migration), `pnpm ci:frontend`, both E2E suites and `git diff --check` green.
2. `docs/architecture/INSTAGRAM_PRODUCTION_READINESS.md` with cited current Meta requirements, outstanding prerequisites, and `[UNRESOLVED]` items.
3. Live session evidence for S2–S13 (S14 optional): status codes and aggregate counts; any failures recorded as they occurred.
4. ADR-014's open items closed or carried with reasons: observed sandbox webhook, `mid` length, Page-token lifetime, data-retention terms, App Review plan.
5. An explicit `READY` / `CONDITIONALLY READY` / `BLOCKED` classification with the conditions listed.
6. Docker back to baseline (sandbox container and volume removed after confirmation); tunnel closed; no listeners left.
7. Checkpoint `M08-S07.md` (`REVIEW`).

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Fix the two S06 findings (Simulator bypass, sign-in limiter) in S07 before exposing anything | **Yes.** Both are small, and the integration boundary can't be called production-ready with a forgeable Simulator |
| Q2 | Add the `BackgroundJobs:ServerEnabled` switch so a real failed event can be produced and replayed safely (S13) | **Yes.** Without it, "replay a safe failed event" can only be shown on outbound failures (S11/S12), not on a failed inbound webhook |
| Q3 | Connect/reauthorize through an owner-run script with hidden token input (A4), instead of building the connect UI now | **Yes, script.** The real connect flow (OAuth) belongs to M10 onboarding |
| Q4 | Tunnel | **`cloudflared` quick tunnel** (Homebrew install, no account, the URL changes each run), always pointed at the allowlist proxy. Alternative: ngrok with your own account for a stable URL |
| Q5 | Read-only access to official Meta documentation for Part B | **Yes** (same as S05) |
| Q6 | Revoke method for S11 | **Real revoke** (remove the app under Business integrations, then grant again). The alternative is waiting for a short-lived token to expire. Either way, `subscribed_apps` may need redoing |

## Out of scope

- OAuth onboarding and a connect UI (M10).
- AI automation (M09; live suppression re-verified there).
- Submitting App Review or business verification (owner, external).
- CI for real-backend E2E (M11).
- Provoking Meta rate limits.
- Production deployment.

## Build checklist

- [x] Task 1 — A1 Simulator bypass restricted to Development/Testing; tests
- [x] Task 2 — A2 sign-in limiter partitioned per IP; tests
- [x] Task 3 — A3 `BackgroundJobs:ServerEnabled`; tests
- [x] Task 4 — A4 connect/reauthorize script, A5 allowlist proxy (+ duplicate-next), A6 runner, A7 evidence collector; proxy tests
- [x] Task 5 — Part B documentation verification → `docs/architecture/INSTAGRAM_PRODUCTION_READINESS.md`
- [x] Task 6 — Automated gates (backend, EF, `ci:frontend`, both E2E suites, `git diff --check`)
- [x] Task 7 — **Live session with the owner** (S1–S13, S16); evidence recorded — *partial: S1–S5, S7, S8, S10, S16 passed; S9 not observed; S6, S11–S13 skipped by the owner*
- [ ] Task 8 — Optional next-day S14 window check — *not done*
- [x] Task 9 — Classification; ADR-014 status note (append); Docker/tunnel cleanup; checkpoint `M08-S07.md` (`REVIEW`); status docs — *tunnel closed; sandbox database kept (stopped) pending the owner's decision*

### Build progress (2026-10-05)

Tasks 1–6 are complete; the Task 7 live session is next. Additions found during the build are recorded here and in the checkpoint:

- **A2 widened.** The registration and password-reset limiters had the same single-global-window defect as sign-in (3 per hour for the whole platform). All three auth limiters are now partitioned per client address.
- **Rate-limit gap (Part B, R11).** Graph error 80002 (Instagram business-use-case throttle) was treated as a permanent rejection. It is now transient.
- **Window error mapping (Part B, R12).** Meta's outside-window errors are `10/2018278` and `2534022`, not 1545041 (which means "not available"). They now map to `window_closed`. ADR-017 has an appended correction note.
- **One more owner secret.** `Development:Seed:DemoPassword` is the owner's own sign-in password for the sandbox inbox (`owner@kreyora.test`); the runner refuses to start without it. The runner generates `Sandbox:PostgresPassword` itself.
- **Live-session addition (U3).** If a second customer account without an app role is available, one DM from it shows whether Standard-Access webhooks include non-role customers.
- **Dry run.** The runner, proxy, control endpoint, evidence queries and the connect script's session client were run against a real local stack: no tunnel, no Meta calls, throwaway credentials. The dry-run database was then destroyed, and Docker returned to baseline.

### Live-session corrections (2026-10-06)

- **Scenario S3:** the Page subscription fields are `messages,message_reactions`. `messaging_seen` belongs only to the app-level webhook (L3).
- **Scenario S2:** the subscription must go in the app's main **Webhooks** product. The Instagram-Login "Configure webhooks" page is a different product (L2). With the owner's explicit authorization, Claude created the app subscription via `POST /{app-id}/subscriptions`.
- **Connect needs `pages_read_engagement` (L1).**
- **Deviation:** the owner chose to paste the App Secret, tokens and the sandbox password into the chat and authorized Claude to use them. They were used only for local API calls, read-only Graph diagnostics, and the app subscription; they were not written to any file. The owner was advised to reset the App Secret and remove/regrant app access to invalidate those tokens.
