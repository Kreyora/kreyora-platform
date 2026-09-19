# M07-S02 — Connection Model and Encrypted-Secret Lifecycle Plan

## 1. Executive Summary

Milestone 07 Step 02 implements the persistent `ChannelConnection` aggregate root, EF Core configuration and PostgreSQL migration, encrypted credential lifecycle, authorized metadata management service, API controller, and end-to-end integration tests.

Building upon the capability models and accepted ADRs (ADR-010 through ADR-013) established in Step 01, this step establishes the durable foundation for managing merchant social media connections securely without coupling to live OAuth exchanges or exposing secrets.

---

## 2. Invariants & Governing ADRs

1. **Connection Ownership (ADR-010):** `ChannelConnection` is owned by `TenantId` (mandatory, indexed, EF global query filter) with an optional `StoreId` foreign key binding.
2. **Secrets Encryption at Rest (ADR-013):** Plaintext credentials are encrypted using `ISecretEncryptionService` (AES-256-GCM) before persistence. Only `EncryptedSecret` (`CiphertextBase64`, `IvBase64`, `AuthTagBase64`, `KeyVersion`) is stored in the database.
3. **Zero Secret Leakage:** Plain text credentials, ciphertexts, initialization vectors, and authentication tags are strictly excluded from API response DTOs and logging. APIs return only metadata (e.g. `HasCredentials: true`, `KeyVersion: "v1"`).
4. **Key Rotation Support:** Connections support seamless zero-downtime key rotation via `RotateConnectionSecretsAsync`, decrypting with the recorded `KeyVersion` and re-encrypting with the target key version.
5. **Multi-Tenant Isolation:** Every read and write operation enforces tenant context. Cross-tenant access, cross-tenant store binding, and duplicate connection collisions within a tenant are strictly prohibited.
6. **Policy-Based RBAC:** Read operations require `TenantPermissions.IntegrationsRead` (Owner, Admin, Operator, Viewer); mutation operations require `TenantPermissions.IntegrationsWrite` (Owner, Admin only).
7. **Append-Only Audit Trail:** All connection lifecycle events (create, update, rotate, disable, enable, delete) record immutable `AuditEvent` entries.

---

## 3. Detailed Technical Design

### 3.1 Domain Layer (`services/api/src/Kreyora.Domain/Integrations/`)

- **`ChannelConnection.cs`**: Aggregate Root inheriting from `BaseEntity, ITenantOwned`:
  - `Id`: 26-character Ulid (`IdGenerator.NewId()`).
  - `TenantId`: Required 26-character tenant identifier.
  - `StoreId`: Optional 26-character store identifier.
  - `Channel`: `ChannelType` enum (`WhatsApp`, `Instagram`, `Messenger`, `Viber`, `Telegram`, `Simulator`).
  - `ExternalAccountId`: External provider account/channel identifier (e.g., Phone Number ID, Page ID, Bot username). Max 128 chars.
  - `DisplayName`: Human-readable label for the connection (e.g., "Main WhatsApp Line"). Max 128 chars.
  - `Status`: `ChannelConnectionStatus` enum (`Pending`, `Active`, `Degraded`, `Expired`, `Revoked`, `Disabled`).
  - `EncryptedCredentials`: `EncryptedSecret?` holding GCM ciphertext, IV, auth tag, and key version.
  - `Capabilities`: `ChannelCapabilities` instance reflecting granted or supported capabilities for this connection.
  - `TokenExpiresAt`: Optional token expiry timestamp.
  - `RefreshTokenExpiresAt`: Optional refresh token expiry timestamp.
  - `LastRefreshedAt`: Optional timestamp of last successful token refresh.
  - `LastValidatedAt`: Optional timestamp of last connection validation.
  - `LastHealthCheckAt`: Optional timestamp of last health check.
  - `HealthSummary`: Short human-readable health summary (max 256 chars).
  - `HealthDetails`: Detailed diagnostic message or error (max 1024 chars).
  - `WebhookVerificationToken`: Optional token used for webhook challenge verification (max 128 chars).
  - Domain methods:
    - `Create(...)`
    - `UpdateMetadata(displayName, storeId)`
    - `UpdateCredentials(encryptedSecret, tokenExpiresAt)`
    - `RotateSecret(ISecretEncryptionService encryptionService, string targetKeyVersion)`
    - `UpdateHealth(isHealthy, status, summary, details, checkedAt)`
    - `Disable(reason)`
    - `Enable()`
    - `Revoke(reason)`

### 3.2 Persistence Layer (`services/api/src/Kreyora.Infrastructure/`)

- **`ChannelConnectionConfiguration.cs`**:
  - Table: `channel_connections`
  - Primary Key: `Id` (max 26)
  - Alternate Key: `(TenantId, Id)`
  - Unique Index: `(TenantId, Channel, ExternalAccountId)` — prevents duplicate connections to the same external account within a tenant.
  - Indexes: `(TenantId, StoreId)`, `(TenantId, Status)`
  - Foreign Key: `(TenantId, StoreId)` -> `Store(TenantId, Id)` with `DeleteBehavior.Restrict` (nullable).
  - Value conversions & owned entities:
    - `Channel`: string conversion (max 32).
    - `Status`: string conversion (max 32).
    - `EncryptedCredentials`: `OwnsOne(c => c.EncryptedCredentials, b => { ... })` mapping to `credentials_ciphertext`, `credentials_iv`, `credentials_auth_tag`, `credentials_key_version`.
    - `Capabilities`: JSON conversion (`jsonb`) using `System.Text.Json`.
  - Concurrency token: PostgreSQL shadow `xmin` column.
- **`AppDbContext.cs`**:
  - Add `DbSet<ChannelConnection> ChannelConnections => Set<ChannelConnection>();`
  - Add global query filter: `builder.Entity<ChannelConnection>().HasQueryFilter(c => c.TenantId == CurrentTenantId);`
- **EF Core Migration**:
  - Generate migration: `AddChannelConnections` in `Kreyora.Infrastructure`.

### 3.3 Application Layer (`services/api/src/Kreyora.Application/Integrations/`)

- **`IChannelConnectionService.cs`**:
  - `Task<Result<ChannelConnectionDto>> CreateConnectionAsync(CreateChannelConnectionRequest request, CancellationToken cancellationToken = default);`
  - `Task<Result<ChannelConnectionDto>> UpdateConnectionAsync(string connectionId, UpdateChannelConnectionRequest request, CancellationToken cancellationToken = default);`
  - `Task<Result<ChannelConnectionDto>> RotateConnectionSecretsAsync(string connectionId, string targetKeyVersion, CancellationToken cancellationToken = default);`
  - `Task<Result<ChannelConnectionDto>> DisableConnectionAsync(string connectionId, string? reason, CancellationToken cancellationToken = default);`
  - `Task<Result<ChannelConnectionDto>> EnableConnectionAsync(string connectionId, CancellationToken cancellationToken = default);`
  - `Task<Result<bool>> DeleteConnectionAsync(string connectionId, CancellationToken cancellationToken = default);`
  - `Task<Result<ChannelConnectionDto>> GetConnectionByIdAsync(string connectionId, CancellationToken cancellationToken = default);`
  - `Task<Result<IReadOnlyList<ChannelConnectionDto>>> GetConnectionsAsync(string? storeId = null, ChannelType? channel = null, CancellationToken cancellationToken = default);`
  - `Task<Result<ConnectionHealthResult>> CheckHealthAsync(string connectionId, CancellationToken cancellationToken = default);`
- **Contracts / DTOs**:
  - `CreateChannelConnectionRequest`: `ChannelType Channel`, `string? StoreId`, `string ExternalAccountId`, `string DisplayName`, `string? PlainTextSecret`, `string? WebhookVerificationToken`, `DateTimeOffset? TokenExpiresAt`.
  - `UpdateChannelConnectionRequest`: `string? DisplayName`, `string? StoreId`, `string? PlainTextSecret`, `DateTimeOffset? TokenExpiresAt`.
  - `ChannelConnectionDto`: Strict redaction!
    ```csharp
    public sealed record ChannelConnectionDto(
        string Id,
        string TenantId,
        string? StoreId,
        ChannelType Channel,
        string ExternalAccountId,
        string DisplayName,
        ChannelConnectionStatus Status,
        bool HasCredentials,
        string? KeyVersion,
        DateTimeOffset? TokenExpiresAt,
        DateTimeOffset? LastValidatedAt,
        DateTimeOffset? LastHealthCheckAt,
        string? HealthSummary,
        ChannelCapabilities Capabilities,
        DateTimeOffset CreatedAt,
        DateTimeOffset ModifiedAt);
    ```

### 3.4 Infrastructure Service (`services/api/src/Kreyora.Infrastructure/Integrations/`)

- **`ChannelConnectionService.cs`**:
  - Injected dependencies: `AppDbContext`, `ITenantContextAccessor`, `ITenantPermissionAuthorizer`, `ISecretEncryptionService`, `IAuditEventService`, `IChannelProviderRegistry`, `Domain.Abstractions.ITimeProvider`.
  - Enforces `ITenantPermissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite)` on write operations.
  - Enforces `ITenantPermissionAuthorizer.Demand(TenantPermissions.IntegrationsRead)` on read operations.
  - Validates `StoreId` belongs to current tenant if specified.
  - Encrypts secrets using `ISecretEncryptionService.Encrypt`.
  - Records structured audit events for every state modification.

### 3.5 Web API Layer (`services/api/src/Kreyora.WebApi/Controllers/`)

- **`ChannelConnectionsController.cs`**:
  - Attributes: `[ApiController]`, `[RequireTenantContext]`, `[ApiVersion("1.0")]`, `[Route("v{version:apiVersion}/integrations/connections")]`.
  - Endpoints:
    - `GET /v1/integrations/connections` (`Authorize(Policy = TenantPermissions.IntegrationsRead)`)
    - `GET /v1/integrations/connections/{id}` (`Authorize(Policy = TenantPermissions.IntegrationsRead)`)
    - `POST /v1/integrations/connections` (`Authorize(Policy = TenantPermissions.IntegrationsWrite)`, `[ValidateAntiForgeryToken]`)
    - `PUT /v1/integrations/connections/{id}` (`Authorize(Policy = TenantPermissions.IntegrationsWrite)`, `[ValidateAntiForgeryToken]`)
    - `POST /v1/integrations/connections/{id}/rotate-secret` (`Authorize(Policy = TenantPermissions.IntegrationsWrite)`, `[ValidateAntiForgeryToken]`)
    - `POST /v1/integrations/connections/{id}/disable` (`Authorize(Policy = TenantPermissions.IntegrationsWrite)`, `[ValidateAntiForgeryToken]`)
    - `POST /v1/integrations/connections/{id}/enable` (`Authorize(Policy = TenantPermissions.IntegrationsWrite)`, `[ValidateAntiForgeryToken]`)
    - `DELETE /v1/integrations/connections/{id}` (`Authorize(Policy = TenantPermissions.IntegrationsWrite)`, `[ValidateAntiForgeryToken]`)
    - `POST /v1/integrations/connections/{id}/health` (`Authorize(Policy = TenantPermissions.IntegrationsRead)`)

---

## 4. Test Strategy

1. **Unit Tests (`Kreyora.UnitTests/Integrations/ChannelConnectionTests.cs`)**:
   - Connection creation with valid parameters.
   - Validation failures (empty tenant, external account, display name).
   - Domain status transitions (enable, disable, revoke).
   - Domain secret rotation (`RotateSecret`) ensuring ciphertext and key version change while preserving decrypted value.
   - Guard against enabling revoked connection without new credentials.
2. **Integration Tests (`Kreyora.IntegrationTests/Integrations/ChannelConnectionIntegrationTests.cs`)**:
   - Uses real PostgreSQL via Testcontainers.
   - **Persistence & Encryption:** Create connection -> verify raw SQL query in PostgreSQL contains `credentials_ciphertext`, `credentials_iv`, `credentials_auth_tag`, `credentials_key_version`, and ZERO plaintext.
   - **Redaction:** `GetConnectionById` returns `HasCredentials: true` but zero secret fields.
   - **Key Rotation:** Call `RotateConnectionSecretsAsync` from `v1` to `v2` -> verify database row updated to `credentials_key_version = 'v2'`, and decrypted secret matches original plaintext.
   - **Tenant Isolation:** Tenant A creates connection -> Tenant B cannot see, retrieve, update, rotate, or delete it (returns 404 Not Found).
   - **Uniqueness Invariant:** Inserting duplicate `(TenantId, Channel, ExternalAccountId)` returns 409 Conflict.
   - **Store Binding Invariant:** Binding to a valid store succeeds; binding to a cross-tenant store returns 400 Validation Error.
   - **RBAC:** Verify Operator and Viewer roles receive 403 Forbidden on write operations.
   - **Audit Trail:** Verify audit events are created in `audit_events` table for create, update, rotate, disable, enable, and delete.

---

## 5. Quality Gates & Acceptance Criteria

1. `dotnet build services/api/Kreyora.slnx --configuration Release --no-restore --disable-build-servers /m:1` passes with 0 warnings and 0 errors.
2. `dotnet ef migrations has-pending-model-changes --project services/api/src/Kreyora.Infrastructure/Kreyora.Infrastructure.csproj --startup-project services/api/src/Kreyora.WebApi/Kreyora.WebApi.csproj --no-build` verifies migration is clean.
3. `dotnet test services/api/Kreyora.slnx --configuration Release --no-build` passes (all unit, contract, architecture, and PostgreSQL Testcontainers integration tests).
4. `pnpm ci:frontend` passes (lint, typecheck, tests, Next.js build).
5. `git diff --check` passes cleanly.
6. Checkpoint `artifacts/checkpoints/M07-S02.md` created with status `REVIEW`.

