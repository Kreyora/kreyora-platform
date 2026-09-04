# M04-S04 — Media Authorization and Object-Storage Abstraction Plan

## Status and objective

- **Milestone:** 04 — Catalog, Inventory, and Media
- **Step:** 04 — Media Authorization and Object-Storage Abstraction
- **Status:** Plan proposed; implementation must not begin until approved.
- **Prerequisite:** M04-S03 is approved and its PostgreSQL/Testcontainers inventory suite passed 5/5 on 2026-09-04.

Add private, tenant-owned product-media metadata and a provider-neutral storage boundary. The step establishes safe media ownership, lifecycle, local development storage, and a Cloudflare R2 configuration seam. It does not expose HTTP endpoints, generated clients, frontend upload UI, public storefront media, checkout, or any production R2 credential.

## Existing foundation and scope boundary

The current Catalog aggregate owns `Product` and `ProductVariant` but has no media entity. `ITenantKeyBuilder` already constructs path-safe tenant prefixes and rejects empty, slash, backslash, and traversal segments. That builder is the sole source for persisted media object keys.

M04-S05 owns controllers, OpenAPI, generated TypeScript, and the seller product-media UI. This step supplies application contracts and safe capability objects only; it does not add an externally reachable upload or download route.

### Included

- `MediaAsset` metadata, lifecycle, ordering, alt text, and tenant/product attachment rules.
- A private object-storage port and local filesystem implementation for development/testing.
- An R2/S3-compatible implementation and validated configuration seam, without account credentials in source control.
- Authorized initiation, completion verification, read-capability issuance, attachment/reordering, deletion request, and orphan-cleanup service operations.
- Tenant-scoped persistence, migration, authorization, audit events, and Hangfire cleanup job.
- Unit, PostgreSQL integration, migration, and storage-isolation tests.

### Explicitly deferred

- Controllers, OpenAPI, generated clients, Next.js upload controls, product-editor integration, demo-fixture changes, and public-store rendering.
- Public buckets, CDN/public URLs, customer-uploaded files, knowledge documents, video/audio, SVG, image transformation, EXIF processing, virus scanning, OCR, and content moderation.
- Cloudflare account creation, R2 bucket provisioning, credentials, domain/CDN configuration, and a production object-storage rollout.

## Locked design

### Media type and size policy

This step supports product-image originals only:

| Allowed MIME type | Maximum bytes | Canonical extension |
|---|---:|---|
| `image/jpeg` | 10 MiB | `jpg` |
| `image/png` | 10 MiB | `png` |
| `image/webp` | 10 MiB | `webp` |

The server verifies both declared MIME type and image signature after upload. Client filename, extension, MIME declaration, path, and content length are untrusted input. SVG is deliberately rejected because it can contain active content; GIF, HEIC, video, and arbitrary binary uploads are deferred.

### `MediaAsset` aggregate

Add tenant-owned `MediaAsset` to the Catalog domain with these immutable fields: `Id`, `TenantId`, `ObjectKey`, `ContentType`, `ByteSize`, and creation timestamp. Its mutable fields are nullable `ProductId`, `SortOrder`, `AltText`, lifecycle timestamps, and state.

States are:

```text
UploadPending ──verified completion──► Ready
      │                                  │
      └──expiry/orphan cleanup──► Deleted ◄── deletion request ── DeletionPending
```

- `UploadPending` cannot issue a read capability and expires after 15 minutes.
- `Ready` may be attached only to a product in the same verified tenant. Attachments are ordered from zero upward. Alt text is optional, normalized, and limited to 300 characters.
- A deletion request immediately prevents new reads by changing state to `DeletionPending`; the cleanup worker deletes the object and records `Deleted` only after the provider confirms deletion or object absence.
- `Deleted` is terminal. Metadata is retained for audit/recovery; its object key is never reused.

`ProductId` has a nullable tenant-inclusive foreign key to `(tenant_id, id)` on `products`. Database constraints enforce non-negative `sort_order`, positive `byte_size`, valid state/timestamp combinations, and a unique `(tenant_id, product_id, sort_order)` position for non-deleted attachments.

### Storage key and access model

Every asset key is server-generated from the verified tenant and asset ID:

```text
tenants/{tenantId}/media/{mediaAssetId}/original.{jpg|png|webp}
```

No client-controlled path segment or original filename enters the object key. Objects are private at every provider. The database stores only the object key—not a public URL or provider credential.

The application issues an expiring `MediaReadCapability` for an existing `Ready` asset only after `catalog.read` authorization. It carries the asset ID, tenant binding, intended object key, and an expiry of at most five minutes. The capability is opaque to callers and is designed for M04-S05’s protected delivery endpoint or an R2 signed GET URL; it is not a public URL by itself. Expired, deleted, foreign-tenant, and unattached-pending assets cannot be read.

### Provider-neutral storage port

Define an application-owned `IPrivateObjectStorage` port with provider-neutral records for:

- creating a bounded upload grant for a server-generated key and allowed content policy;
- writing or confirming an upload;
- reading object metadata and a bounded prefix for signature verification;
- issuing a time-limited read capability/URL where the provider supports it; and
- idempotently deleting an object.

The port does not expose AWS, S3, R2, filesystem paths, buckets, or raw provider exceptions to Catalog domain code. `IMediaAssetService` owns authorization, asset state, product attachment, metadata validation, and audit evidence; storage adapters only handle object bytes and capabilities.

The local adapter writes below a configured private root outside `wwwroot`, resolves no user path, and uses a temporary-file-plus-atomic-rename write. It never serves files statically. The R2 adapter uses the S3-compatible endpoint only when `Storage:Media:Provider=R2`; it keeps the bucket private and produces short-lived signed operations. No R2 operation is attempted when the local provider is selected.

### Configuration and deployment seam

Add validated options, with safe defaults for Development only:

```json
"Storage": {
  "Media": {
    "Provider": "Local",
    "LocalRoot": "AppData/private-media",
    "MaxUploadBytes": 10485760,
    "UploadLifetimeMinutes": 15,
    "ReadLifetimeMinutes": 5,
    "R2": {
      "AccountId": "",
      "BucketName": "",
      "Endpoint": "",
      "AccessKeyId": "",
      "SecretAccessKey": ""
    }
  }
}
```

`Local` is allowed only in Development. `R2` requires an HTTPS endpoint, account/bucket values, and credentials from environment/secret configuration; the repository’s sample configuration stays blank. The Docker deployment later mounts the local root only for development. Production uses a private R2 bucket and secret injection—never a committed credential or public ACL.

## Service flow

```text
verified tenant + catalog.write
  → validate requested image type/size policy
  → create UploadPending MediaAsset with server key and expiry
  → obtain bounded storage upload grant
  → caller uploads through the future protected/proxied endpoint or R2 capability
  → complete: head/read prefix, verify byte count + magic signature + key
  → mark Ready, optionally attach/order/alt text, audit

catalog.read + Ready asset in same tenant
  → issue <=5 minute read capability

delete request or scheduled cleanup
  → block reads (DeletionPending)
  → storage delete idempotently
  → mark Deleted and audit
```

The completion operation re-checks tenant context and asset state; a stolen or expired upload capability cannot attach media to another tenant or product. Cleanup treats a missing object as a successful deletion so retries converge. A storage write that succeeds before the database update leaves an orphan that the cleanup job removes; a database deletion intent that precedes a failed object deletion remains `DeletionPending` and is retried.

## Persistence, authorization, and audit plan

1. Add `MediaAsset`/`MediaAssetState`, EF mapping, `DbSet`, tenant filter, tenant-write enforcement, and one additive migration.
2. Add tenant-inclusive product relationship/indexes plus a filtered attachment-order uniqueness index.
3. Add media contracts and `IMediaAssetService`; require existing `CatalogWrite` for initiate/complete/attach/reorder/delete and `CatalogRead` for list/read capability. PlatformSupport and Viewer remain unable to mutate.
4. Register media service, storage options, and the selected adapter in Infrastructure DI. Add the AWS S3-compatible dependency only in Infrastructure, never Domain/Application.
5. Append tenant-scoped audit events: `media.upload.initiated`, `media.upload.completed`, `media.attached`, `media.reordered`, `media.deletion.requested`, and `media.deleted`. Metadata contains IDs, content type, byte size, and state only—never signed URLs, tokens, filesystem paths, original filenames, or credentials.
6. Add a Hangfire `MediaCleanupJob`. It enumerates active tenants and enters each through `ITenantJobRunner`, then deletes expired pending assets and deletion-pending/orphaned objects in a bounded batch. It uses fresh scopes and continues safely after a single-tenant failure.

## Verification plan

### Unit tests

- Every media state transition, terminal deletion, alt-text normalization, ordering, and server-generated key rule.
- MIME/size policy and signature validation: JPEG/PNG/WebP accepted; declared/actual mismatch, SVG, traversal, over-limit, and invalid bytes rejected.
- Local adapter blocks root escape, uses no public/static URL, and deletes idempotently.
- Capability expiry and tenant/asset binding are enforced; audit metadata never contains sensitive storage data.

### PostgreSQL/Testcontainers integration tests

- Media rows, product attachment, ordering, and migration constraints persist correctly.
- Guessed foreign asset/product IDs cannot be read, attached, reordered, completed, or deleted from another tenant.
- Viewer/PlatformSupport mutation attempts are denied.
- Expired pending assets and deletion retries converge without a visible asset or leaked object.
- Local storage object/write lifecycle and cleanup job remain tenant-scoped.

### Required completion gates

```bash
dotnet build services/api/Kreyora.slnx --configuration Release --no-restore
dotnet test services/api/tests/Kreyora.UnitTests/Kreyora.UnitTests.csproj --configuration Release --no-build
dotnet test services/api/tests/Kreyora.IntegrationTests/Kreyora.IntegrationTests.csproj --configuration Release --no-build --filter FullyQualifiedName~Media
dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --no-build
git diff --check
```

Run the Testcontainers suite with Docker available. After validation, verify no Testcontainers-labelled container remains; remove only the temporary test container/network/volume and images created for that run if a clean Docker cache is requested. Never remove unrelated developer images or containers.

## Risks and decisions awaiting implementation review

- [ASSUMPTION] A 10 MiB limit and JPEG/PNG/WebP-only policy are suitable for MVP product imagery; adjust only through plan review before implementation.
- [ASSUMPTION] Protected HTTP delivery is deliberately deferred to M04-S05, so this step returns internal read capabilities rather than exposing a route prematurely.
- Cloudflare R2 account, bucket, region/jurisdiction, retention policy, credentials, and CDN configuration remain deployment gates. No provider behavior is claimed until tested against a real non-production R2 bucket.
- Graphify incremental refresh was attempted after M04-S03 approval but could not write its cache in this restricted workspace; semantic documentation extraction also has no configured LLM key. This is tooling-only and does not alter repository architecture.

## Approval request

Approve this plan to start M04-S04 implementation. The implementation will stop after M04-S04 and create its checkpoint; M04-S05 will not begin automatically.
