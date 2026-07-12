# Current Work State

## Active position

- **Milestone:** 01 — Frontend Showcase
- **Step:** 07 — Inbox, Integrations, AI Assistant, and Takeover UI
- **Status:** `REVIEW`
- **Active milestone file:** `design_files/project_a_milestones/01_FRONTEND_SHOWCASE.md`

## Branch and commit state

- **Branch:** main (local, no commits or pushes)
- **Last state:** M01-S01 + M01-S02 + M01-S03 + M01-S04 + M01-S05 + M01-S06 + M01-S07 complete

## Checkpoint

- **Current checkpoint:** `artifacts/checkpoints/M01-S07.md`
- **Previous checkpoint:** `artifacts/checkpoints/M01-S06.md`

## Blockers

None.

## Current objective

M01-S07 implementation complete:

1. Unified inbox conversation list (`/inbox`) with search by customer name/message/labels, state filter (bot_active/human_assigned/awaiting_customer/resolved/closed), channel filter (Facebook/Instagram/WhatsApp/TikTok/Storefront), channel icon badges, unread count badges, state badges, assignment display, labels, last message preview, timestamps
2. Conversation detail page (`/inbox/[id]`) with message timeline (sender type badges, delivery state badges, attachment indicators, retry indicators for failed), human takeover/release controls with Bot Active/Bot Paused indicator, staff composer (only available when human has control), provider health context sidebar (connection status, events processed, failures), AI activity traces, customer info, labels display
3. Integration connection list (`/integrations`) with connection cards showing provider badge, account name, status badge, capabilities summary, events count, Connect Channel placeholder
4. Integration detail page (`/integrations/[id]`) with capabilities grid (6 flags with check/cross), health sidebar (status, last event, events processed/failed, token expiry with warning, webhook URL), webhook events table with status badges and Replay button for failed events, Reconnect button for disconnected/error connections
5. AI assistant policy page (`/assistant`) with sub-navigation (Overview/Knowledge/Console/History), config display (enabled status, language, tone, max iterations, cost budget, auto-escalation)
6. Knowledge documents page (`/assistant/knowledge`) with document list, status badges (pending_review/approved/rejected/archived), simulated lifecycle actions (approve/reject/archive), Upload Document placeholder
7. Test console page (`/assistant/console`) with simulated chat interface, pre-canned bot response from fixture trace data, tool-trace sidebar (intent, confidence, tokens, cost band, latency, escalation state, tool call details with input/output)
8. Action history page (`/assistant/history`) with expandable trace list, conversation links, tool calls count, confidence, cost band/escalation badges, token count, latency, expandable tool call details, generated response preview

**Evidence:** 402 tests pass, 0 type errors.

## Next permitted action

After M01-S07 review approval: start M01-S08 in **plan mode**.

## Next prohibited action

- M01-S08 implementation (until M01-S07 is approved)
- Any backend (.NET/C#) implementation
- Any real provider integration, deployment, or external service contact
- Committing, pushing, or deploying

## Update history

| Date | Change | By |
|---|---|---|
| 2026-07-12 | Initialized at bootstrap. Milestone 01, Step 01, NOT STARTED. | Bootstrap session |
| 2026-07-12 | M01-S01 initial implementation complete. | M01-S01 session |
| 2026-07-12 | M01-S01 amended (9 amendments). Status → REVIEW. | M01-S01 amendment session |
| 2026-07-12 | M01-S02 complete. 116 tests pass, clean type check. Status → REVIEW. | M01-S02 session |
| 2026-07-12 | M01-S03 complete. 169 tests pass, clean type check. Status → REVIEW. | M01-S03 session |
| 2026-07-12 | M01-S04 complete. 219 tests pass, clean type check. Status → REVIEW. | M01-S04 session |
| 2026-07-12 | M01-S05 complete. 284 tests pass, clean type check. Status → REVIEW. | M01-S05 session |
| 2026-07-12 | M01-S06 complete. 321 tests pass, clean type check. Status → REVIEW. | M01-S06 session |
| 2026-07-12 | M01-S07 complete. 402 tests pass, clean type check. Status → REVIEW. | M01-S07 session |
