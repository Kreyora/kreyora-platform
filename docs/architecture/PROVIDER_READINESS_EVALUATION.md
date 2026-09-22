# Provider Readiness Evaluation — First Social Channel (M08-S01)

- **Date / access date:** 2026-09-22 (all sources fetched this date; public official documentation only, no credentials used)
- **Scope:** WhatsApp Business Platform (Cloud API), Messenger Platform, Instagram Messaging. TikTok deferred by owner; Telegram/Viber out of priority scope.
- **Outcome:** (a) — public docs sufficient for a ranked recommendation. ADR-014 drafted as `Proposed`. Blocking gaps enumerated in §6.
- **Cell legend:** `verified` = official doc URL + location; `unsupported` = official doc states absence; `unknown` = not evidenced, marked `[UNRESOLVED]`.

## Official sources

| ID | Document | URL |
|---|---|---|
| S1 | Webhooks — Getting Started (Graph API) | https://developers.facebook.com/docs/graph-api/webhooks/getting-started |
| S2 | WhatsApp Cloud API — Get Started | https://developers.facebook.com/docs/whatsapp/cloud-api/get-started |
| S3 | Pricing on the WhatsApp Business Platform | https://developers.facebook.com/docs/whatsapp/pricing |
| S4 | Instagram Messaging — Getting Started | https://developers.facebook.com/documentation/business-messaging/instagram-messaging/get-started |
| S5 | App Review for Messenger API support for Instagram | https://developers.facebook.com/documentation/business-messaging/instagram-messaging/app-review |
| S6 | Send a Message (Messenger Platform, incl. Instagram) | https://developers.facebook.com/documentation/business-messaging/messenger-platform/send-messages |
| S7 | Human Agent feature reference | https://developers.facebook.com/docs/features-reference/human-agent |
| S8 | Webhooks for Instagram Messaging | https://developers.facebook.com/documentation/business-messaging/instagram-messaging/webhooks |
| S9 | Webhooks (WhatsApp Business Platform) | https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/overview |

## Evidence matrix

| Dimension | WhatsApp Cloud API | Messenger Platform | Instagram Messaging |
|---|---|---|---|
| API / docs version + access date | `verified` — S2 sample code targets `graph.facebook.com/v23.0`; S3 rate cards effective Jul 1 2026; S9 current. Accessed 2026-09-22. | `verified` — S6 sample code targets `v25.0`. Accessed 2026-09-22. | `verified` — S4/S8 reference Graph v9.0+ endpoints (`/me/accounts`, postback `mid` since v11.0). Accessed 2026-09-22. |
| App/account ownership + sandbox path | `verified` — S2: Facebook/Meta account + developer registration; Meta app with WhatsApp use case; business portfolio; API Setup panel with test phone number, temp token, echo-bot test webhook. | `verified` — Meta app + Facebook Page; Dev-mode testing scoped to app roles (same app-role gating model as S8). Exact Page-setup page not fetched: detail `[UNRESOLVED]`. | `verified` — S4: Instagram professional account + connected Facebook Page + developer with `MODERATE` task + Meta app; S8: pre-review webhooks fire only for app-role users; app must be published to receive webhooks. |
| Webhook verification challenge | `verified` — S1 (`hub.mode=subscribe`, echo `hub.challenge`, match `hub.verify_token`); S2 links this same flow for custom servers. | `verified` — S1 (shared Meta framework). | `verified` — S1 (shared Meta framework). |
| Request signature scheme + replay window | `verified` — S1: `X-Hub-Signature-256: sha256={HMAC-SHA256(payload, App Secret)}`; validation optional-but-recommended. Replay-window/timestamp rule: `unknown` `[UNRESOLVED]` (no timestamp header documented in S1/S9). | `verified` — S1, same scheme. Replay window: `unknown` `[UNRESOLVED]`. | `verified` — S1, same scheme. Replay window: `unknown` `[UNRESOLVED]`. |
| Token types / scopes / expiry / refresh / revoke | `verified` — S2: temporary token (expires quickly) for first test; permanent system-user token with `business_management` + `whatsapp_business_messaging` + `whatsapp_business_management`; S9: webhooks need `whatsapp_business_messaging` (messages) / `whatsapp_business_management` (others). Refresh/revoke mechanics: `unknown` `[UNRESOLVED]`. | `verified` — S6: Page access token per sending Page; error `190` = token expired → regenerate. Scopes/refresh detail: `unknown` `[UNRESOLVED]`. | `verified` — S4: User token → Page access token; short-lived user ⇒ 1-hour PAT; long-lived user ⇒ non-expiring PAT; permissions `instagram_basic`, `instagram_manage_messages`, `pages_manage_metadata`. Refresh/revoke mechanics: `unknown` `[UNRESOLVED]`. |
| Inbound text (limits) | `verified` — text inbound with `wamid.*` id, `wa_id`, profile name, timestamp (S2/S9). Exact char limit: `unknown` `[UNRESOLVED]`. | `verified` — text via Send API/webhooks (S6). Exact char limit: `unknown` `[UNRESOLVED]`. | `verified` — text via `messages` field (S8). Exact char limit: `unknown` `[UNRESOLVED]`. |
| Inbound media (types, size caps) | `verified` — non-template image sending documented (S3 lists `type: image`); full inbound set/size caps: `unknown` `[UNRESOLVED]`. | `verified` — audio, files, GIFs, images, stickers, templates, videos (S6 content types). Size caps: `unknown` `[UNRESOLVED]`. | `verified` — audio, file, image, share, story_mention, video, ig_reel/reel; GIF/sticker inbound triggers **no** webhook; disappearing/ephemeral media unsupported (S8). Size caps: `unknown` `[UNRESOLVED]`. |
| Reactions | `unknown` `[UNRESOLVED]` (no reaction field documented in S2/S9). | `unknown` `[UNRESOLVED]` (not covered in fetched S6 sections). | `verified` — `message_reactions` (react/unreact, v12.0+ reaction set); business react/unreact sends **no** echo (S8). |
| Delivery / read receipts | `verified` — status notifications incl. delivered and read (S2 step 4; S9 `messages` field covers outbound statuses). Full status enum: `unknown` detail `[UNRESOLVED]`. | `verified` — `message_deliveries` + `message_reads`, Messenger-only (S6). | `verified` — `messaging_seen` (read) only; delivery receipts explicitly Messenger-only, i.e. `unsupported` for IG (S6). |
| Deleted / unsupported-type / retry-duplicate semantics | `verified` — failed deliveries retried with decreasing frequency up to 7 days; duplicates possible (S9). Delete/unsupported semantics: `unknown` `[UNRESOLVED]`. | `verified` — Graph API retries with decreasing frequency over 36h; server must dedupe (S1). | `verified` — `is_deleted`, `is_unsupported` flags on `messages` events (S8); retry/dedupe inherits S1 (36h). |
| Outbound text / media / link preview | `verified` — text + image send via `POST /{phone-number-id}/messages` (S2); link preview: `unknown` `[UNRESOLVED]`. | `verified` — text + media attachment via `/​{PAGE-ID}/messages`, incl. reusable-URL upload API (S6). Link preview: `unknown` `[UNRESOLVED]`. | `verified` — same Send API with IGSID recipient (S6); generic/button/product templates, quick replies, ice breakers (S8 nav). Link preview: `unknown` `[UNRESOLVED]`. |
| Templates + conversation-window rules | `verified` — 24h customer-service window opened by user reply; non-template only inside window; templates required outside (S2); categories marketing/utility/authentication (S3). | `verified` — 24h standard messaging window + opening user actions; RESPONSE/UPDATE inside, TAGGED outside, no promo in tags (S6). | `verified` — same 24h window + tags as Messenger (S6); HUMAN_AGENT 7-day manual-response extension (S6/S7). |
| Rate limits (throughput, caps) | `verified` — messaging limits exist: max unique users/day outside CSW, moving 24h, portfolio-level tiers + `account_alerts`/`business_capability_update` webhooks (S9); display-name verification + quality/throughput signals exist (S9 fields). Exact tier ladder/MPS numbers: `unknown` `[UNRESOLVED]` (third-party sources agree on tiers/80 MPS but no official numbers page fetched). | `verified` — rate limiting exists; error `613` + rate-limit reference (S6). Exact caps: `unknown` `[UNRESOLVED]`. | `verified` — inherits Messenger rate limiting (S6). Exact caps: `unknown` `[UNRESOLVED]`. |
| Sandbox limitations | `verified` — test number + temp token + echo-bot console logger (S2); some webhooks require Live mode (S9). Full Dev-vs-Live matrix: `unknown` detail `[UNRESOLVED]`. | `unknown` detail `[UNRESOLVED]` (Dev-mode gating assumed from shared platform model, not fetched for Messenger). | `verified` — pre-review traffic limited to app-role users; app must be published for any webhooks (S8). |
| App review / production prerequisites | `verified` — partner advanced-access review for `whatsapp_business_*` permissions (S9); display-name verification outcomes via webhook (S9). Exact production checklist: `unknown` detail `[UNRESOLVED]`. | `verified` — error `10` without `pages_messaging` (S6); Human Agent needs App Review + business verification (S7). Full checklist: `unknown` detail `[UNRESOLVED]`. | `verified` — App Review mandatory before non-role users (S5); Human Agent needs App Review + business verification, possibly extra contracts (S7). Full checklist: `unknown` detail `[UNRESOLVED]`. |
| Data-retention terms | `unknown` `[UNRESOLVED]` (no retention schedule in fetched docs). | `unknown` `[UNRESOLVED]`. | `unknown` `[UNRESOLVED]`. |
| Costs / billing model | `verified` — per-message template billing since Jul 1 2025; non-template + in-window utility free; 72h free-entry-point window; Nepal called out for standalone lower utility/auth rates Oct 1 2026 (S3). | `verified` — no per-message pricing published; Meta publishes a pricing model only for WhatsApp (S3 scope). No API-call fees documented as of 2026-09-22. | `verified` — same as Messenger: no per-message pricing published. |
| Identity model | `verified` — E.164 `wa_id` + profile name; `phone_number_id` routing; `wamid.*` message ids (S2/S9). | `verified` — Page-scoped PSID; app-scoped Login IDs do **not** work (S6). | `verified` — IGSID sender ids + business IGID; echo flag `is_echo` (S8). |

## Capability mapping onto `IChannelProvider`

- `ValidateWebhookAsync` ← S1 verification + HMAC contract (all three; WhatsApp additionally mTLS-optional per S9/S1).
- `NormalizeInboundAsync` ← S8 event catalog (IG), S9 `messages` field (WA), S6 content/recipient model (Messenger). IG `messaging_seen` → `MessageStatusUpdatedPayload(Read)`; no delivery receipt on IG (restriction, not gap).
- `SendMessageAsync` ← S2 (`POST /{phone-number-id}/messages`), S6 (`POST /{PAGE-ID}/messages`, IGSID recipient for IG). WA outside-window sends require templates; Messenger/IG outside-window sends require tags.
- `ValidateOrRefreshConnectionAsync` ← PAT/token-expiry model (S4/S6 error `190`); WA permanent system-user token (S2).
- `ChannelCapabilities.InstagramGraphApi()` matches evidence (no delivery receipts, read receipts true, 24h window true, signature verification true) — **no code change required** (Task 3 verify-only).

## Fallback UX (input to M08-S06, not built)

1. **Expired 24h window:** disable reply input with explanation; IG/Messenger offer HUMAN_AGENT 7-day manual path (requires review per S7) — exact send-error subcode `[UNRESOLVED]` (S6 documents `1545041` window-closed; provider-specific subcodes not fetched).
2. **Receipt display:** IG advances `Sent → Read` directly (no `Delivered`); WA/Messenger use full `Sent → Delivered → Read`.
3. **Media:** provider-fetched public URLs assumed; signed-URL delivery must stay within the R2 storage invariant — flow `[UNRESOLVED]` until S02/S03.
4. **Invisible inbound:** IG GIF/sticker/disappearing media never arrives (S8) — no UX error, documented limitation.

## Recommendation (proposed, not selected)

Ranked: **1. Instagram** — professional-account onboarding without a phone number, free API messaging, `messaging_seen` read receipts, full echo/deleted/unsupported flags, HUMAN_AGENT fallback; **2. Messenger** — same stack plus delivery receipts, but weaker Nepal boutique fit `[ASSUMPTION]`; **3. WhatsApp** — richest receipts + identity, but per-message template billing (incl. Nepal-specific rates) and phone-number/messaging-limit operations burden.

## Blocking gaps (owner action required)

1. No Meta app / test assets exist (owner: create app + IG professional account + linked Page, or WA test number) — S02 cannot start.
2. No sandbox run has been observed — S07 evidence outstanding.
3. App Review + business verification not started (S5/S7 prerequisites).
4. `[UNRESOLVED]` details above (limits, retention, subcodes, refresh mechanics) to be closed during S02/S03 against sandbox behavior.
