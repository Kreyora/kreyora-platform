# Instagram Messaging — Production Readiness

- **Status:** M08-S07 `REVIEW` (2026-10-06). Live-sandbox results and the classification are in section 5.
- **Owner:** Platform Architect, with the Project Owner for Meta account actions.
- **Sources accessed:** 2026-10-05, read-only, public Meta developer documentation. Wording below is quoted from the fetched pages. None of the pages showed a last-updated date, except the Platform Terms (effective 2026-02-03).
- **Related:** ADR-014 (channel choice), ADR-015 (webhook ownership), ADR-016 (identity), ADR-017 (outbound and window), `PROVIDER_READINESS_EVALUATION.md` (M08-S01).

## 1. Verified Meta requirements

| # | Requirement (as documented) | Source | What it means for Kreyora |
|---|---|---|---|
| R1 | Permissions via Facebook Login: `instagram_basic`, `instagram_manage_messages`, `pages_manage_metadata`. The Page access token must carry `instagram_manage_messages`. The developer needs `MODERATE` task access on the Page | [Get started][get-started] | Matches the S02 connect validation. `instagram_manage_messages` depends on `instagram_basic` ([permission reference][perm-imm]) |
| R2 | Each seller's professional account must enable "Allow Access to Messages" under "Instagram Settings > Messages and story replies > Message controls > Connected Tools" | [Get started][get-started] | A seller-side onboarding step. M10 onboarding must tell sellers about it and detect when it's missing |
| R3 | "Your app must be published, regardless of app review status, to receive webhooks." Without approval, "webhooks are sent only to users with app roles who own/administer the data" | [Webhooks][webhooks] | Development-mode apps receive nothing. Before App Review, only accounts administered by app-role users produce webhooks |
| R4 | Webhook fields: `messages`, `message_reactions`, `messaging_postbacks`, `messaging_seen`, `messaging_referral`, `standby` | [Webhooks][webhooks] | Kreyora subscribes to `messages`, `messaging_seen`, `message_reactions` (S03). Postbacks, referrals and standby are not handled (out of MVP scope) |
| R5 | Signature `X-Hub-Signature-256: sha256=…` (SHA256 with the App Secret). Respond `200 OK` over HTTPS with a valid certificate (no self-signed). Up to 1000 updates per batch | [Webhooks getting started][wh-start] | Implemented (S03). Production needs a publicly trusted TLS endpoint (M11) |
| R6 | "If any update sent to your server fails, we will retry immediately, then try a few more times with decreasing frequency over the next 36 hours." | [Webhooks getting started][wh-start] | Redelivery is real. Event-level dedup (ADR-015) is required and exists |
| R7 | Standard Access: "can only be requested from app users who have a role on the requesting app". Advanced Access: available to any user and "requires Business Verification", sometimes plus per-permission App Review. Business apps get Standard Access automatically | [Access levels][access] | **Real sellers can't connect until Advanced Access is granted.** Today only app-role accounts work |
| R8 | App Review prerequisites: comply with policies; Page connected to the Instagram professional account; the app must handle webhooks and use the Send API (and the Conversation API for custom inboxes); "Apps For Other Businesses" vs "Apps For Your Own Business" | [App Review][app-review] | Kreyora submits as **Apps For Other Businesses** (multi-tenant). The working flow (S07 live session) is the material for the review screencast |
| R9 | Human Agent: lets a human reply "within 7 days of a user's message" with the `human_agent` tag. It "requires successful completion of the App Review process" and "is only available with business verification" | [Human Agent][human-agent] | `InstagramMessaging:HumanAgentTagApproved` stays `false` until approved (ADR-017) |
| R10 | Long-lived user tokens last "about 60 days". "Long-lived Page access token do not have an expiration date and only expire or are invalidated under certain conditions" | [Long-lived tokens][tokens] | Confirms ADR-014's "non-expiring" claim for **long-lived** Page tokens. Revocation (190) is still handled: connection `Expired` → reauthorize (S02/S05). The exact invalidation conditions page returned 404 (see U2) |
| R11 | Send API: "100 calls per second per Instagram professional account" for text, links, reactions and stickers; 10/s for audio or video. Conversations API: 2/s. Throttling codes include 4, 17, 32, 613 and **80002** (Instagram business-use-case limit) | [Rate limiting][rate] | Far above MVP volume. **Gap found and fixed in S07:** 80002 was treated as a permanent rejection instead of a retryable throttle |
| R12 | Outside the messaging window: `10 / 2018278` "This message is sent outside of allowed window" and `2534022`. `551 / 1545041` = "This person isn't available right now." | [Send API error codes][errors] | **Gap found and fixed in S07:** the client treated 1545041 as the window error, and 10/2018278 surfaced as "lacks permission". ADR-017's citation is corrected by an appended note |
| R13 | Apps must provide "either a data deletion callback instruction URL or a callback URL" in Basic Settings | [Data deletion][deletion] | The owner provided an **instructions URL** (allowed); no callback code needed. Kreyora's owner erasure service (S04) carries out deletions |
| R14 | Platform Terms (effective 2026-02-03): delete Platform Data "as soon as reasonably possible" when no longer needed for a legitimate business purpose; "promptly" on user or Meta request; provide "an easily accessible and clearly marked way" to request deletion; no fixed maximum retention period | [Platform Terms][terms] | Closes ADR-014's `[UNRESOLVED]` data-retention item. ADR-012's raw-payload purge plus the S04 erasure service meet the mechanism; a seller-facing deletion policy is still needed for production (P6) |

### Findings from the live session (2026-10-05/06)

| # | Finding | Evidence | Consequence |
|---|---|---|---|
| L1 | Kreyora's connect check reads the Page's linked Instagram account (`/{page-id}?fields=instagram_business_account`, and likewise `me` with a Page token). That read needs **`pages_read_engagement`** (Graph error 100: "This endpoint requires the 'pages_read_engagement' permission…"), on v21.0 and v26.0 alike | Live Graph responses with the owner's token | The working token needs `instagram_basic`, `instagram_manage_messages`, `pages_manage_metadata`, `pages_show_list` **and `pages_read_engagement`**. Add it to the App Review request (P3), or change the check later |
| L2 | Meta's dashboard offers **two** Instagram webhook setups. "API setup with Instagram business login → Configure webhooks" belongs to the Instagram-Login product and signs with the separate *Instagram app secret*. The Facebook-Login route Kreyora uses (ADR-014) needs the app's main **Webhooks** product, object `instagram` | With the subscription made only on the Instagram-Login page, `GET /{app-id}/subscriptions` returned 0 subscriptions, a real DM produced no delivery, and that page's Test deliveries failed the App-Secret signature check (3 × 401). After `POST /{app-id}/subscriptions` (object `instagram`; fields `messages`, `messaging_seen`, `message_reactions`), a real DM arrived, was signature-verified and processed | M10 onboarding docs and support material must point sellers and operators to the main Webhooks product. The 401s were the intended safe outcome |
| L3 | `messaging_seen` is not a Page `subscribed_fields` value (`POST /{page-id}/subscribed_apps` rejects it). The Page subscription only installs the app on the Page (`messages,message_reactions` worked); read receipts come through the app-level `instagram` webhook field | Graph error 100 listing the allowed Page fields | The M08-S03 manual prerequisites (and the first draft of this plan) listed it wrongly; a correction is appended to the S03 plan |
| L4 | A long-lived Page token made with "Extend Access Token" shows "This new long-lived access token will never expire" in the Access Token Tool | Owner's Access Token Tool | Confirms R10 |

## 2. Production prerequisites still outstanding

| # | Prerequisite | Type | Status (2026-10-05) |
|---|---|---|---|
| P1 | App published (Live), with privacy policy URL, data-deletion instructions URL, icon and category | Owner (Meta) | **Done** (owner-reported). Re-confirmed by the live session receiving webhooks |
| P2 | Business Verification of the business behind the app | Owner (Meta, external) | Not started |
| P3 | App Review: Advanced Access for `instagram_basic`, `instagram_manage_messages`, `pages_manage_metadata`, **`pages_read_engagement`** (L1) and `pages_show_list` (plus `business_management` if onboarding needs it), submitted as "Apps For Other Businesses", with a screencast | Owner (Meta, external) | Not started. Needs P2 |
| P4 | Human Agent feature review (late human replies up to 7 days) | Owner (Meta, external) | Not started; optional for launch. Without it, replies after 24 h are refused with a clear message |
| P5 | Public production HTTPS callback with a valid certificate; production deployment; client-IP forwarding so the per-IP auth limits work behind a proxy | Kreyora (M11) | Not started (deployment milestone) |
| P6 | Seller-facing privacy policy and deletion process in the product (not just the owner's interim pages) | Owner + Kreyora (M10/M11) | Interim pages exist |
| P7 | Seller onboarding for R2 ("Allow access to messages") and OAuth connect | Kreyora (M10) | Not started; the S07 connect script is a development tool |

## 3. Readiness code changes made in M08-S07

- The Simulator's fixed test signature is accepted only in Development/Testing (S06 finding 1).
- Auth rate limits (sign-in, registration, password reset) are partitioned per client address (S06 finding 2).
- Graph error 80002 is treated as throttling (R11).
- Meta's outside-window errors map to `window_closed`; 1545041 means "not available" (R12).
- The Hangfire server can be disabled per process (`BackgroundJobs:ServerEnabled`).

## 4. Unresolved

- **U1:** whether Meta disables a webhook subscription after sustained failures. The fetched pages are silent.
- **U2:** the full list of Page-token invalidation conditions. The documentation page returned 404 on 2026-10-05. Observed in the live session: S11 (removing the app's business integration).
- **U3:** whether, under Standard Access, a DM from a customer **without** an app role reaches an app-role business account's webhook. R3's wording ("users with app roles who own/administer the data") suggests yes; to be observed in the live session if a non-role account is available.
- **U4:** whether Kreyora needs Meta's "Tech Provider" path (businesses serving other businesses) in addition to App Review. Not verified; check before submitting P3.

## 5. Live sandbox evidence and classification

**Session:** 2026-10-05 22:58 – 2026-10-06 00:42 NPT.
- **Setup:** the owner's Live Meta app, the Kreyora Page with its linked Instagram professional account, and a customer account with an app role.
- **Local stack:** Development environment, a dedicated sandbox database, and a cloudflared quick tunnel that reached only the webhook allowlist proxy.
- **Evidence rules:** aggregate figures only; no identifiers or message content are recorded.

| # | Scenario | Result | Evidence |
|---|---|---|---|
| S1 | Sandbox + tunnel | Pass | Through the public URL: webhook path wrong-token handshake 403; `/v1/auth/csrf` and `/hangfire` 404 |
| S2 | Webhook handshake | Pass | 2 × Meta verification `GET` → 200 (owner dashboard, then the app subscription call) |
| S3 | Connect | Pass, after L1 | Connection `Instagram · Active`, validated live. First attempts refused: user token entered instead of Page token, then the missing `pages_read_engagement` (L1); the refusals were correct |
| S4 | Real inbound message | Pass, after L2 | 1 signature-verified delivery → 1 webhook event `Processed` → 1 inbound event `text` → 1 new conversation (unread 1) → 1 inbound message. `mid` length **164** (column 512) |
| S5 | Dedup a retry | Pass | The proxy forwarded Meta's echo delivery twice (200 / 200). The API logged "Duplicate webhook event … detected … Acknowledged in 5ms". Repeated dedup keys 0; repeated message IDs 0 |
| S6 | Update conversation (2nd DM, reaction) | **Skipped** by owner | Same-conversation reuse and reactions are covered by S04 integration tests only |
| S7 | Assign + label | Pass | Audit `conversations.assigned`; 1 label saved |
| S8 | Staff reply | Pass | 1 outbound `Staff · Sent`; 1 successful delivery attempt; Meta's echo merged with no extra message (ADR-017); conversation `HumanAssigned · HumanTakeover`; the owner confirmed the reply arrived on the customer phone |
| S9 | Read receipt | **Not observed** | No `messaging_seen` delivery after the owner opened the reply (customer-side read receipts possibly off); skipped by owner. Covered by S03/S04 tests |
| S10 | Take over / hand back | Pass | Audit sequence: takeover (implied by the reply) → assigned → released → takeover. Live automation sending can't be triggered until M09; suppression is proven by the ADR-017 integration tests |
| S11 | Revoke → diagnose | **Skipped** by owner | Not executed live. Covered by S02/S05 tests (190 → `Expired`) |
| S12 | Reauthorize | **Skipped** by owner | Not executed live. Covered by S02 tests |
| S13 | Replay a safe failed event | **Skipped** by owner | Not executed live. Tooling is ready (`BackgroundJobs:ServerEnabled`); replay is covered by M07 tests |
| S14 | Window (next day) | **Not done** | Covered by gate tests; Meta's window error mapping corrected (R12) |
| S15 | Rate limits | Documentation only | R11 limits; 80002 fix + tests |
| S16 | Teardown | Pass | Tunnel closed (public URL 530); no local listeners; sandbox database stopped and kept for an optional follow-up |

**Classification of the production connection: `BLOCKED`.** Production readiness is not claimed.

Reasons:
1. Real sellers can't connect until Meta grants **Advanced Access**, which requires **Business Verification** and **App Review** (R7, P2, P3). Neither has started.
2. The credential lifecycle (revoke → diagnose → reauthorize) and failed-event replay were **not executed live** (S11–S13 skipped). The plan's `CONDITIONALLY READY` criteria need them.
3. Deployment prerequisites are open (P5–P7).

What did pass is the core live path: a signature-verified real DM reached the right tenant conversation once; a duplicate delivery was deduplicated; a staff reply went out through the durable outbox and reached the customer; assignment, labels and takeover were audited.

**Path to `CONDITIONALLY READY`:** run S11–S13 live (about 15 minutes with the kept sandbox database), with S6, S9 and S14 optional. After that, only external approvals and deployment work remain.

[get-started]: https://developers.facebook.com/documentation/business-messaging/instagram-messaging/get-started
[webhooks]: https://developers.facebook.com/documentation/business-messaging/instagram-messaging/webhooks
[app-review]: https://developers.facebook.com/documentation/business-messaging/instagram-messaging/app-review
[wh-start]: https://developers.facebook.com/docs/graph-api/webhooks/getting-started
[human-agent]: https://developers.facebook.com/docs/features-reference/human-agent
[perm-imm]: https://developers.facebook.com/docs/permissions/reference/instagram_manage_messages
[access]: https://developers.facebook.com/docs/graph-api/overview/access-levels
[tokens]: https://developers.facebook.com/docs/facebook-login/guides/access-tokens/get-long-lived
[rate]: https://developers.facebook.com/docs/graph-api/overview/rate-limiting
[errors]: https://developers.facebook.com/docs/messenger-platform/reference/send-api/error-codes
[deletion]: https://developers.facebook.com/docs/development/create-an-app/app-dashboard/data-deletion-callback
[terms]: https://developers.facebook.com/terms/dfc_platform_terms/
