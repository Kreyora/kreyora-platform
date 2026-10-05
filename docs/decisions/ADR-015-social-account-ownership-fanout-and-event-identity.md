# ADR-015 — Social Account Ownership, Webhook Account Fan-out, and Inbound Event Identity

- **Status:** `Accepted`
- **Date:** 2026-10-05
- **Owner:** Platform Architect
- **Reviewers:** Project Owner
- **Affected milestones:** Milestone 07 (runtime), Milestone 08

> Accepted by the project owner on 2026-10-05 as part of approving the M08-S03 corrective scope ("ok do your recommended things"). Amends ADR-010, ADR-011 and ADR-012 as described below; those records carry an appended pointer to this ADR and are otherwise unchanged.

## Context

The M08-S03 review (`docs/plan/M08-S03_WEBHOOK_NORMALIZATION_PLAN.md`, "Corrective scope") reproduced defects that sit on tenant and idempotency boundaries:

1. **Ownership.** `ChannelConnection` uniqueness was `(TenantId, Channel, ExternalAccountId)` (ADR-010 consequences). Two tenants could own the same Instagram account, and ingress resolved by account with an unordered first match. Instagram connections could also be created without live validation. Plan §10.4 requires that "each external account maps to one tenant connection".
2. **Routing.** Unsigned headers and route connection IDs could place a validly signed payload under another tenant.
3. **Multi-account deliveries.** Meta batches "multiple changes from different objects that are of the same type" in one request (Webhooks — Getting Started, accessed 2026-10-05). The whole body was attributed to the first entry's connection.
4. **Event identity.** Inbound deduplication used the *referenced* provider message ID. A read receipt and reactions on one message collided: events were silently dropped, or a unique violation lost the delivery and left it stuck in `Processing`.
5. **App-level secrets.** Meta's signature secret and verify token are per app, not per connection.

## Decision

1. **Global ownership.** `channel_connections` has a unique index on `(Channel, ExternalAccountId)` across all tenants.
   - Creating a duplicate returns a neutral `409` that does not name the owning workspace.
   - Transfer requires the owner to delete its connection.
   - Instagram connections are created or reauthorized only after live Graph validation of the account they claim.
2. **Routing precedence.**
   - Account identity from the signed payload outranks routing headers.
   - A route connection ID is honored only when every signed account in the delivery equals that connection's `ExternalAccountId`; otherwise the request is rejected with `403` and nothing is persisted.
   - For per-connection-secret providers, the account reported by validation must equal the resolved connection's account.
3. **App-signed providers.** `IChannelProvider` gains default members: `UsesConnectionSecretForSignature` (default `true`), `AcknowledgementStatusCode` (default `202`), and `SplitByAccount` (default `null`). Providers returning `false` (Instagram) behave as follows:
   - the whole delivery is verified with the app secret **before** any connection lookup, and connection credentials are never decrypted on the webhook path;
   - the delivery is split per provider account and each slice is routed to its owning connection;
   - all slices are persisted in one database transaction;
   - unknown accounts are acknowledged and dropped with a redacted log;
   - the documented acknowledgement status is returned (Instagram: `200`).
4. **Inbound connection-status policy.** For a verified delivery:
   - `Active`, `Degraded` and `Expired` connections store the event, so customer messages are kept while credentials are repaired;
   - `Disabled`, `Revoked` and `Pending` connections are acknowledged without storing anything (owner decision, 2026-10-05).
5. **Event identity.** `NormalizedInboundEnvelope` gains an optional `DeduplicationKey`. This is a non-breaking optional field, so the schema stays `v1`.
   - `InboundEvent` stores `DeduplicationKey` as lowercase hex SHA-256 of that identity, unique per `(ConnectionId, DeduplicationKey)`. When no key is supplied, the M07 legacy identity is used: the provider message ID, or the row ID when absent.
   - `ProviderMessageId` becomes a non-unique lookup column, widened to 512 characters.
   - Instagram keys:
     - message: the (part) message ID;
     - read: `read:{mid}:{reader}`;
     - reaction: `reaction:{mid}:{reactor}:{action}:{timestamp}`.
6. **Raw payload storage.** For splitting providers, the stored `RawPayload` is the per-account slice of a delivery whose signature was verified at ingress. It is not the byte-identical request body. No tenant's raw log contains another tenant's entries.
7. **Processing recovery.**
   - A failed processing attempt discards all pending changes before recording the failure.
   - Events left in `Processing` beyond a 15-minute lease are reclaimed by the processing job.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Keep per-tenant uniqueness; refuse ambiguous routes | No cross-tenant existence signal | An unvalidated squatter can deny service to the real owner | Rejected |
| Uniqueness among `Active` connections only | Easier transfer | Every status can return to `Active`; ambiguity returns | Rejected |
| Per-connection verify tokens via `/{connectionId}` callbacks | No new configuration | Meta allows one callback per app object; binds all accounts to one tenant | Rejected |
| Store the full delivery under each tenant | Byte-identical raw logs | Copies other tenants' customer PII into each tenant's log | Rejected |
| Filter foreign entries only during normalization | Small change | Other tenants' messages are lost | Rejected as sole measure (kept as defense in depth) |
| Envelope `EventId` as the dedup key | No new field | Simulator event IDs are random; its dedup would break | Rejected |
| `INSERT … ON CONFLICT DO NOTHING` | Race-free inserts | Bypasses EF tenant-ownership enforcement | Rejected |

## Consequences

- **Product impact:**
  - One Instagram account can be connected to exactly one workspace.
  - Sellers with expired or degraded credentials keep receiving customer messages.
  - Disabled connections stop storing inbound data.
- **Architecture impact:**
  - Additive default members on `IChannelProvider`, plus an optional `RawWebhookPayload.ExternalAccountId`.
  - Simulator behavior is unchanged except for one rule: when its validation reports an account, that account must equal the routed connection's account.
- **Security/privacy impact:**
  - Unsigned input can no longer choose the tenant.
  - The duplicate-account error reveals only that the account is connected somewhere.
  - Raw logs are tenant-pure.
- **Cost/operations impact:** unknown-account deliveries are acknowledged (no 36-hour Meta retry storm) and logged with a hashed account ID.
- **Migration or rollback impact:**
  - Migration `20261005052230_HardenChannelOwnershipAndInboundEventIdentity` aborts with a count-only message if an account is connected in several tenants.
  - It backfills `deduplication_key` with the legacy identity, hashed identically in SQL and C#.
  - `Down` cannot restore the old unique message index once several events reference one message; forward-fix instead.

## Validation evidence

- `InstagramWebhookCorrectiveReproTests`: each B1–B5 reproduction failed on the original S03 code (2026-10-05) and passes after the change. It also covers:
  - the connection-status policy;
  - HTTP `200`/`401` responses;
  - the stale-`Processing` reclaim;
  - SQL/C# hash equivalence.
- `InstagramChannelProviderTests`: splitting, the foreign-entry filter, dedup keys, and the app-level verify token.
- The full backend suite, with all M07 Simulator suites, passes.

## Supersession conditions

- A provider whose account identity cannot be derived from signed content.
- A need for shared ownership of one external account across workspaces (e.g. agencies).
- Meta changing batching, signing, or retry semantics.
