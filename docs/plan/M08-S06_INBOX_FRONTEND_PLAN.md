# M08-S06 — Unified Inbox Frontend Integration — Scoped Plan

## Identity

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 06 — Unified inbox frontend integration
- **Author:** Claude (planning)
- **Date:** 2026-10-05
- **Status:** `APPROVED` 2026-10-05 (merged `fa91a38`); was `REVIEW` — implemented 2026-10-05 (checkpoint `artifacts/checkpoints/M08-S06.md`); was `IN PROGRESS` — approved by the owner on 2026-10-05 ("ok implement"): decisions Q1–Q4 as recommended
- **Prerequisites:** M08-S05 `APPROVED` (merged to `master` at `50faeac`); ADR-015/016/017 `Accepted`.
- **Manual work for the owner in this step:** review the UI screenshots, and optionally click through the inbox locally. No Meta or account actions.

## Milestone prompt (verbatim)

> Replace inbox, conversation, assignment, reply, takeover, connection health, delivery status, failure, and replay fixtures with generated real clients while retaining explicit demo mode. Implement live refresh through the simplest approved mechanism, accessible message composition, provider capability denials, optimistic reply with durable reconciliation, failed-message actions, session/permission recovery, and redacted diagnostic links. Add Owner/Admin/Operator/Viewer end-to-end tests with real backend and simulator/sandbox fixtures.

**Review checkpoint:** approve daily operator workflow and demonstrate that the inbox truth matches durable backend state.

## What exists today (verified 2026-10-05)

- **Frontend:**
  - `/inbox` and `/inbox/[id]` render fixture data only. `ClientSet.conversation` is hard-wired to `mockConversationClient`, even in API mode.
  - `ConversationClient` is read-only (list/get/messages).
  - The detail page's takeover/release/send buttons change local state only.
- **Patterns to reuse:**
  - `apiFetch` (correlation ID, RFC 7807 → `ApiClientError`, global `kreyora:api-error` event handled by the seller layout);
  - `X-Kreyora-Tenant-Id` and `X-CSRF-Token` headers in adapters;
  - `USING_FIXTURE_ADAPTERS` (no `NEXT_PUBLIC_API_URL` → demo) and `components/demo-indicator.tsx`;
  - the generated `src/lib/api/generated/v1.ts` (already used by the public storefront adapter);
  - the real `IntegrationClient` (connection health and diagnostics, M07-S06).
- **Frontend type gaps against the API:**
  - The UI `Conversation`/`Message` types use snake_case states, `assignment.assigneeName`, `senderName` and `deliveryState: "pending"`.
  - The API returns camelCase enums, `assignedUserId`, `actorUserId`, `isPending`, and `deliveryStatus`.
  - A mapping layer is required.
- **Backend gaps found while planning (must be fixed for a working inbox):**
  1. **`WebhookProcessingJob` and `OutboundDeliveryJob` are registered in DI but never scheduled with Hangfire** (`Program.cs` schedules only the inventory, media, checkout and notification jobs). In a running app, inbound webhooks are stored but never become conversations, and staff replies are queued but never sent. The test suites call the services directly, so this pre-existing M07 gap was never exercised.
  2. **No assignee directory for inbox users.** `GET /v1/memberships` is Owner/Admin only, so Operators and Viewers cannot see who a conversation is assigned to, or which staff member sent a reply.
  3. **`MessageItem` carries no failure reason**, so the UI cannot tell a definite failure from `delivery_unconfirmed` (which must warn against resending blindly).
- The Playwright config has a fixture-mode mobile project only. There is no real-backend browser harness, and the dev seed creates just one Owner and the "Development Store".

## Objective

Make the seller inbox show durable backend truth and let each role do its daily work against the real API: list, open, read, reply, take over, release, assign, label, and change status. Fixture data stays available only in an explicitly labeled demo mode. Close the three backend gaps that would otherwise make the live inbox inert. Prove the workflow with role-based end-to-end tests against a real API, database and Simulator connection.

## Design

### A. Backend enablers (small, tested)

1. **Job scheduling.**
   - Register recurring `WebhookProcessingJob` and `OutboundDeliveryJob` sweepers (minutely), so retries, backoff and stale-`Processing` reclaim run.
   - Ingress enqueues an immediate Hangfire job per newly persisted webhook event; a staff reply enqueues an immediate delivery job. A new message then reaches the inbox in seconds rather than up to a minute.
   - Enqueueing happens after commit. If it fails, the sweeper still picks the item up (no loss).
2. **`GET /v1/conversations/assignees`** (`conversations.read`): active members of the current tenant as `{ userId, displayName, role }`. No emails. Used for assignment pickers and to show sender and assignee names.
3. **`MessageItem.deliveryFailureCode?`**: the last delivery attempt's code for failed staff/automation messages (e.g. `delivery_unconfirmed`, `window_closed`, `551`). Additive field.
4. OpenAPI and TypeScript regenerated (additive), via the live-local-API workflow on a throwaway database.

### B. Frontend data layer

- Extend the `ConversationClient` port:
  - `listConversations({ status, unreadOnly, assignedTo, page })`, `getConversation`, `getMessages(id, before?)`;
  - `sendReply(id, text, idempotencyKey)`, `markRead`, `takeOver`, `release`, `assign`, `unassign`, `setLabels`, `changeStatus`, `listAssignees`.
- New `apiConversationClient` uses the generated `components["schemas"]` types, adds tenant + CSRF headers, and maps API → UI types in one tested module (`conversation-mapping.ts`):
  - camelCase status → existing UI state keys;
  - origin → `senderType`;
  - `isPending`/`deliveryStatus` → `deliveryState`;
  - actor/assignee IDs → names from `listAssignees`.
- `mockConversationClient` implements the full port. Simulated replies, takeover etc. update in-memory fixtures and are **visibly marked as demo** (existing `DemoIndicator` plus an inbox-level "Demo data — nothing is sent" notice).
- `ClientSet.conversation` follows `USING_FIXTURE_ADAPTERS` like the other clients (testable selection).
- **Denial reasons:** `ApiClientError.type` `urn:kreyora:problem:<code>` maps to plain-language copy and a suggested action. For example, `window_closed` → "The customer hasn't messaged in 24 hours. Instagram only allows replies inside that window."

### C. Live refresh — simplest mechanism: polling

- Open conversation: poll messages and detail every 5 s. List: poll every 15 s.
- Polling pauses while the tab is hidden and resumes with an immediate refresh. Backs off to 60 s after consecutive network failures, showing a "Reconnecting… / data may be stale since HH:MM" banner. A manual "Refresh" button is always available.
- **Rationale:** no new infrastructure (SignalR/SSE would need connection scaling and, for multi-instance, a backplane; Redis is not an MVP dependency). Documented as a decision; real-time push can replace it later behind the same port.

### D. Inbox list (`/inbox`)

- Server filters: status, unread only, assigned to me, all. Paging ("Load more").
- Each row shows the customer label, a channel chip, status badge, last message preview, relative time, unread count, assignee name, and an automation-on/human indicator.
- **States:** loading skeleton, empty ("No conversations yet — connect Instagram" with a link to integrations), error with retry, permission-denied, stale/disconnected banner, success.
- **Quota-warning state:** not applicable. No messaging quotas exist before M10; recorded as N/A with reason rather than faked.

### E. Conversation view (`/inbox/[id]`)

- **Timeline:**
  - provider-time order;
  - inbound vs outbound styling;
  - sender name (customer label / staff display name / "Automation" / "Sent from Instagram app" for echoes);
  - media as safe links with type (no hotlinked autoplay);
  - reactions summary;
  - delivery state icons with text labels: Pending, Sent, Read, Failed ("Delivered" never shown for Instagram);
  - "Load earlier messages" uses the keyset cursor;
  - redacted messages render as "Message removed".
- **Composer:**
  - labeled textarea with a character counter (limit from the API's denial code);
  - Enter sends, Shift+Enter adds a new line, plus an explicit Send button;
  - disabled with an explanation when the user lacks permission, the conversation is spam, or the window is closed. The window state is computed **server-side**: the UI shows the server's last-known window hint and the server always decides;
  - focus returns to the composer after sending;
  - new messages are announced via an `aria-live="polite"` region.
- **Optimistic reply with durable reconciliation:**
  - a generated idempotency key per send attempt; the pending bubble appears immediately;
  - the server response replaces it (same ID). Polling then moves it to Sent/Read/Failed;
  - a double-click or retry of the same attempt reuses the key (no duplicates);
  - on a denial the pending bubble becomes an inline error with the reason; nothing is persisted.
- **Failed-message actions:**
  - `Failed` → "Send again" (new reply, new key, same text);
  - `delivery_unconfirmed` → the same action behind a confirmation: "Instagram didn't confirm delivery. The customer may already have this message. Send again anyway?";
  - "Copy text" is always available.
- **Side panel:**
  - status actions (resolve/reopen/close/spam/unmark), allowed by the server state;
  - takeover/release with clear copy ("Automation is paused while you handle this conversation");
  - assign/unassign picker (assignee directory), label editor;
  - **connection health** card from `IntegrationClient` (e.g. "Instagram connection expired — reauthorize") with a **redacted diagnostics link** to the existing integration diagnostics page (IDs only, no payloads);
  - customer identity summary (masked label, first/last seen).
- **Mark read:** when an Operator+ opens a conversation with unread messages (once per view). Viewers never mark read (they can't); the badge stays.
- **Conflicts:** `409 conversation_changed` → "This conversation was updated by someone else" with a Refresh action; the change is never silently reapplied.
- **Session and permission recovery:** 401 → existing layout handling (sign-in with return URL); 403 → permission-denied panel; 404 → "Conversation not found or not in this workspace".

### F. Roles (UI mirrors server policy; the server is authoritative)

| Role | Read | Reply / takeover / release / assign / labels / status | Mark read |
|---|---|---|---|
| Owner / Admin / Operator | ✓ | ✓ | ✓ |
| Viewer | ✓ | Hidden or disabled with "View-only access" (server returns 403 if forced) | — |

### G. End-to-end tests against the real backend

- **New Playwright project `real-backend`** (opt-in: `pnpm --filter @kreyora/web test:e2e:real`). It is not part of `ci:frontend`.
  - Adding a database service to CI is a delivery-topology decision for the owner (Q3).
- **Harness script** (`apps/web/scripts/e2e-real.mjs`):
  1. Start a throwaway Postgres container.
  2. Run `--migrate`, then `--seed` with E2E personas enabled.
  3. Start the API (Development; Hangfire on) and Next with `NEXT_PUBLIC_API_URL`.
  4. Run the specs.
  5. Tear down the container **and its anonymous volume**.

  Project containers and data are never touched.
- **Dev seed extension** (Development-only, behind `Development:Seed:E2ePersonas=true`):
  - `admin@`, `operator@`, `viewer@kreyora.test` with the existing `Development:Seed:DemoPassword` mechanism (no committed secrets; the harness generates a random password per run);
  - a **Simulator** connection with a run-scoped signing secret;
  - two conversations with fresh customer messages.
- **Specs** (mobile Pixel 5 and desktop Chromium):
  1. **Operator daily workflow:** open inbox → open conversation → (marked read) → reply → pending bubble → becomes Sent (Simulator delivery via the scheduled job) → automation shown paused → assign to self → add label → resolve → reopen.
  2. **Live refresh:** the test posts a signed Simulator webhook (inbound customer message). It appears in the list and the open conversation within the polling interval, with an unread badge.
  3. **Inbox truth matches the backend:** after spec 1, the test reads the same conversation through the API and asserts status, assignee, labels, message count and delivery states equal what the UI shows.
  4. **Owner/Admin:** takeover and release from the panel; release resumes automation (indicator).
  5. **Viewer:** sees the list and timeline; no composer or actions; a forced API write returns 403.
  6. **Denial:** a conversation seeded outside the window shows the composer disabled with the window explanation, and a forced send shows the denial copy.
  7. **Demo mode** (existing fixture project): the inbox shows the demo notice and simulated actions never call the network.
- **Screenshots** (mobile and desktop) of list, conversation, composer states, failure and denial, captured into `artifacts/screenshots/M08-S06/` for the checkpoint.

## Tests (unit/component, Vitest — in `ci:frontend`)

- **Mapping module:** every status, origin, delivery state, pending, redacted and echo case.
- **Adapter:** headers (tenant, CSRF, idempotency), URLs, error mapping to denial copy.
- **Adapter selection:** API vs demo.
- **Polling hook:** interval, hidden-tab pause, backoff, stale banner, reduced motion.
- **Composer:** keyboard, counter, disabled reasons, focus return, `aria-live` announcement.
- **Optimistic reconciliation:** pending → replaced → failed; same key on retry; denial rollback.
- **Failed-message actions:** confirmation for unconfirmed.
- **Role gating:** Viewer and Operator.
- **409 conflict:** refresh prompt. **Permission-denied and not-found** states.

## Backend tests (Testcontainers)

- Job scheduling: the ingress enqueue call happens after commit (enqueuer stub); the sweepers are registered (recurring-job manager stub).
- `GET /v1/conversations/assignees`: tenant isolation, active members only, no emails, Viewer allowed, unauthenticated 401.
- `MessageItem.deliveryFailureCode`: populated for dead-lettered staff replies (unconfirmed, window closed); absent otherwise.

## Contracts and migrations

- **API:** one new route (`GET /v1/conversations/assignees` + `ConversationAssigneeItem`); `MessageItem.deliveryFailureCode` (additive).
- **No migration** expected (failure code read from existing outbox and attempt rows).
- Generated OpenAPI/TS regenerated.

## Security and tenancy

- No client-side authority: the UI hides actions per role, but every write is server-authorized.
- The window and capability state shown is the server's; browser input never sets status or delivery state.
- The assignee directory exposes display names and roles only, for the current tenant.
- Diagnostics links carry IDs only. Payload access stays behind the existing ADR-012 role-based redaction.
- Playwright seeds use run-scoped random passwords and secrets that are never committed.
- **Demo mode** can never call the real API (separate adapter), and is labeled.

## Acceptance criteria

1. With `NEXT_PUBLIC_API_URL` set, the inbox uses only real clients; no fixture conversation data appears. Without it, demo mode is fully functional and visibly labeled.
2. The real-backend Playwright suite passes for Owner, Admin, Operator and Viewer, including the live inbound refresh and the "UI equals API" assertion.
3. In a running app, inbound webhooks are processed and replies delivered without manual job invocation (scheduled plus immediate enqueue).
4. All required UX states exist; quota-warning is documented N/A.
5. Accessibility:
   - keyboard-only reply, assign and status flows;
   - visible focus;
   - 44 px targets;
   - `aria-live` for new messages;
   - reduced motion honored;
   - no horizontal scroll at 360 px.
6. Vitest + lint + typecheck + build (`pnpm ci:frontend`) green. Full backend suite green; EF clean (no migration). `git diff --check` clean. Docker back to baseline after both harnesses.
7. Checkpoint `M08-S06.md` with mobile and desktop screenshots, actual outputs, and status `REVIEW`.

## Exact verification commands

```bash
dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1
dotnet test services/api/Kreyora.slnx --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --configuration Release --no-build
pnpm generate:api            # live local API on a throwaway database
pnpm install --frozen-lockfile && pnpm ci:frontend
pnpm --filter @kreyora/web test:e2e              # fixture/demo project (existing)
pnpm --filter @kreyora/web test:e2e:real         # real API + throwaway Postgres + Simulator
git diff --check
```

## Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Include the three backend enablers (job scheduling, assignee directory, failure code) in S06 | **Yes.** Without job scheduling the live inbox never receives messages or sends replies (pre-existing M07 gap) |
| Q2 | Live refresh mechanism | **Polling** (5 s open conversation, 15 s list, paused when hidden). Push later behind the same port |
| Q3 | Run the real-backend Playwright suite in CI now | **Not yet.** Run locally and record evidence; add a Postgres service to CI at M11 (delivery topology) |
| Q4 | Auto mark-read when an Operator+ opens a conversation | **Yes**, once per view |

## Out of scope

- Real-time push (SSE/SignalR); internal notes; outbound media.
- Profile names; Meta sandbox (S07); AI suggestions (M09).

## Build checklist

- [x] Task 1 — Backend: schedule processing/delivery jobs + immediate enqueue; assignee directory route; `deliveryFailureCode`; tests
- [x] Task 2 — OpenAPI/TS regeneration (throwaway database)
- [x] Task 3 — Port + generated-type API adapter + mapping module + demo adapter parity + selection; unit tests
- [x] Task 4 — Polling hook + stale/reconnecting UX; tests
- [x] Task 5 — Inbox list: filters, paging, states, role gating
- [x] Task 6 — Conversation view: timeline, composer, optimistic reply, failed actions, side panel (status/takeover/assign/labels/health/diagnostics link), conflict and permission states; component tests
- [x] Task 7 — Dev-seed E2E personas + Simulator fixtures; real-backend Playwright harness and specs (roles, live refresh, UI=API, denial); demo spec; screenshots
- [x] Task 8 — Full gates; Docker cleanup; checkpoint `M08-S06.md` (`REVIEW`); status docs

Build checklist completed 2026-10-05. Deviations D1–D3 (window composer stays enabled with server denial shown inline; fixture-coupled inbox tests replaced; harness signs each persona in once because of the global sign-in limiter) are recorded in the checkpoint.
