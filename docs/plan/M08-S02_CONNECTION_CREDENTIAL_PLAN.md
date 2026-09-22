# M08-S02 — Connection / Credential Lifecycle (Instagram) — Scoped Plan

## Identity

- **Milestone:** 08 — First Validated Social Channel and Unified Inbox
- **Step:** 02 — Connection/OAuth or credential lifecycle
- **Author:** Phase 1 (Architect)
- **Date:** 2026-09-22
- **Status:** `PLANNING` — Awaiting human approval before implementation
- **Prerequisites:** M08-S01 `APPROVED`; ADR-014 must be marked `Accepted` before Phase 2 build starts (owner decision; IG choice already sandbox-proven). Owner-verified Dev-mode sandbox: Meta app + Page + linked IG professional account, Connected Tools ON, scopes granted, token + thread queries working.

## Objective

Implement the Instagram connection lifecycle against the real Graph API surface using Dev-mode sandbox credentials supplied outside source control: live-validated connect, encrypted token storage (ADR-013), health from real provider responses, reauthorize/refresh, disconnect, revocation handling, capability record updates, redaction, and audit. No OAuth redirect flow server-side (Business Login is client-side; backend accepts the resulting Page token — recorded explicitly with reason). No webhook/inbox work (S03/S04 scope).

## Gate, Boundaries, and Scope

### Allowed

1. **Application contract** `IInstagramGraphClient` (new, in `Kreyora.Application.Integrations.Instagram`): `ValidateTokenAsync(pageId, igAccountId)` → parsed profile/username or typed error (expired=190, permissions=10, throttle, transport). Pure contract, no HTTP.
2. **Infrastructure client** `InstagramGraphClient` (new, `Kreyora.Infrastructure.Integrations.Instagram`): `HttpClient` to versioned Graph API only; version constant (v21.0 sandbox-proven; configurable, never hard-coded per call). Timeouts + cancellation; maps Graph error JSON to typed results; never logs tokens (redact Authorization header). **First outbound HTTP in the codebase**: register via `AddHttpClient` typed client in `DependencyInjection.cs` backed by new startup-validated `InstagramGraphOptions` (`BaseAddress` default `https://graph.facebook.com`, `ApiVersion` default `v21.0`, `TimeoutSeconds`); no URL literals at call sites.
3. **Connection flow** in `ChannelConnectionService` (extend, no new service): `CreateConnectionAsync` gains optional live-validation path for `ChannelType.Instagram` — validate PAT via client **before** persisting `EncryptedSecret`; on failure return typed denial (no row, no secret stored). `UpdateConnectionAsync` = reauthorize path (new PAT → `UpdateCredentials`, revives Expired/Revoked/Degraded per domain rules). `CheckHealthAsync` branches on `ChannelType.Instagram` to the client **before** the existing provider-registry lookup (registry path preserved for Simulator); persists via `UpdateHealth` (Healthy/Expired/Revoked/Degraded). **No `IChannelProvider` implementation in S02** — the registry stays Simulator-only; S03 builds the full adapter on this same client and health then delegates to the registered provider (transition recorded here to avoid dual paths).
4. **DTOs + endpoints:** extend `Create/UpdateChannelConnectionRequest` with optional `InstagramConnectOptions(PageId, InstagramAccountId)` — no secret-shaped changes (`PlainTextSecret` carries the PAT over TLS, encrypted at rest immediately, never returned: DTO already exposes only `HasCredentials`/`KeyVersion`). **No new routes**: existing `ChannelConnectionsController` actions (create/update/rotate/disable/enable/delete/`{id}/health`) suffice. `TokenExpiresAt` mapping: caller-supplied when known (short-lived PAT ≈ +1h), else null = non-expiring long-lived PAT; Graph error 190 observed at health time → `Expired`.
5. **Capability/health records:** on successful validation, refresh `Capabilities` from `ChannelCapabilities.InstagramGraphApi()` + set health summary (username, token kind long/short-lived). No preset change (S01-verified).
6. **Audit + RBAC:** reuse existing `IntegrationsWrite` policy + audit trail for create/update/disable/delete/reconnect; reconnect emits explicit audit event.
7. **Tests:** unit (validation mapping, denial paths, redaction, reauthorize state machine via stubbed HTTP — no live calls); contract (client shape vs recorded-safe redacted fixtures, fake IDs only); integration (Testcontainers PostgreSQL for persist/health/audit paths with stubbed HTTP). **No `HttpMessageHandler` stub precedent exists in tests** — this step introduces an approved `FakeInstagramHttpHandler : HttpMessageHandler` test helper with queued responses. Optional owner-executed live sandbox checklist documented in checkpoint (not CI).
8. Checkpoint `artifacts/checkpoints/M08-S02.md` (`REVIEW`).

### Prohibited

- Accepting ADR-014 in code (owner marks accepted separately before build).
- Server-side OAuth callbacks / state-redirect flows (N/A — document why).
- App-secret handling or `debug_token` scope introspection (deferred, `[UNRESOLVED]`).
- Webhook validation/normalization changes (S03), conversation/message entities (S04), reply/takeover (S05), frontend changes (S06).
- Real Page/IG IDs, tokens, or personal payloads in code, fixtures, snapshots, logs, or docs.
- Starting M08-S03 before S02 approval.

## Test plan

- `InstagramGraphClientTests` (unit, stubbed handler): valid profile, error 190→expired, error 10→denied, HTTP 429/5xx→transient, timeout→transient, Authorization header redacted in logs, version constant used.
- `ChannelConnectionInstagramTests` (unit): connect validates-before-persist (invalid token → no row); reauthorize revives Expired; health mapping persists via UpdateHealth; DTO never carries secret back.
- Integration (Postgres + stubbed HTTP): end-to-end connect → health → disconnect → reauthorize with audit rows; cross-tenant access denied.
- Live sandbox checklist (owner, manual): connect with Dev PAT → validation passes → disconnect → reauthorize → revoke simulation; record pass/fail in checkpoint, no secrets recorded.

## Quality gates

- `dotnet build services/api/Kreyora.slnx --configuration Release --disable-build-servers /m:1` (0/0)
- `dotnet test services/api/Kreyora.slnx --configuration Release` (Docker available; full suite incl. Testcontainers)
- `dotnet ef migrations has-pending-model-changes ... --no-build` (expect none — no schema change)
- `pnpm ci:frontend` (expect green, no frontend change)
- `git diff --check`; grep proof: no `graph.facebook.com` outside `Instagram/` client, no token-like literals in tests/fixtures.
