# ADR-013 — Secrets Encryption and Key Management for Social Credentials

- **Status:** `Accepted`
- **Date:** 2026-09-20
- **Owner:** Platform Architect
- **Reviewers:** Project Owner, Engineering Team
- **Affected milestones:** Milestone 07, Milestone 08, Milestone 11

## Context

Connecting social channels requires storing sensitive credentials at rest, including:
1. Long-lived access tokens (e.g., Meta System User access tokens, Page tokens).
2. App secrets and client secrets used to generate API signatures.
3. Webhook verification tokens and signing secrets (e.g. Meta app secret, Viber auth token, Telegram bot token).

In accordance with Kreyora's Architecture Invariants, secrets must be encrypted at rest and never exposed in plain text in database tables, API responses, or logs. We need an encryption abstraction and key management strategy that works seamlessly in local development and automated testing while providing enterprise-grade security and key rotation capabilities in production.

## Decision

We adopt **AES-256-GCM envelope encryption with key versioning** managed through an application abstraction `ISecretEncryptionService`.

1. **Cryptographic Algorithm:**
   - **AES-256-GCM** (Galois/Counter Mode, authenticated encryption with associated data).
   - Key size: 256 bits (32 bytes).
   - Nonce / Initialization Vector (IV): 96 bits (12 bytes), cryptographically random per encryption operation.
   - Authentication Tag: 128 bits (16 bytes), verifying ciphertext integrity and authenticity. Any tampering throws `CryptographicException`.
2. **Envelope Data Structure (`EncryptedSecret`):**
   - Immutable value object containing:
     - `CiphertextBase64`: Encrypted secret payload.
     - `IvBase64`: Unique initialization vector.
     - `AuthTagBase64`: GCM authentication tag.
     - `KeyVersion`: String identifier of the master encryption key used (e.g. `"v1"`, `"v2"`).
3. **Key Management & Environment Separation:**
   - `ISecretEncryptionService` defines `Encrypt(string plainText, string? keyVersion = null)` and `Decrypt(EncryptedSecret secret)`.
   - **Local Development & Test Environments:** Uses `AesGcmSecretEncryptionService` with a deterministic 256-bit development key configured in `appsettings.Development.json` or generated for testing.
   - **Production Environment:** Uses a master encryption key configured via secure environment variable or external KMS (e.g., AWS KMS, HashiCorp Vault, Azure Key Vault).
4. **Key Rotation Support:**
   - Decryption resolves the master key matching the `KeyVersion` recorded in the envelope.
   - Re-encryption routines can decrypt with old key versions and re-encrypt with the active key version without service downtime.
5. **Zero Secret Leakage:**
   - Secrets are never returned over seller or storefront APIs (only metadata, e.g. `LastFour`, `ExpiresAt`, `IsValid`).
   - Logging formatters explicitly redact any secret fields.

## Alternatives considered

| Option | Benefits | Costs/risks | Reason rejected or deferred |
|---|---|---|---|
| Plain text in database | Zero implementation overhead. | Severe security vulnerability; database leak exposes all merchant social channels; non-compliant with basic security standards. | Rejected. Directly violates Kreyora security invariants. |
| ASP.NET Core Data Protection (`IDataProtectionProvider`) | Built-in .NET feature; automatic key ring management. | Tied directly to ASP.NET XML key storage format; difficult to perform explicit key version inspection or migrate to external KMS; prone to key ring corruption in containerized multi-instance environments if shared storage is misconfigured. | Rejected in favor of an explicit envelope encryption service. |
| Single static symmetric key without versioning | Simple AES-GCM implementation. | Impossible to rotate encryption keys without mass migration and downtime; no way to identify which key encrypted which secret. | Rejected. Lack of key versioning creates severe operational debt. |
| **AES-256-GCM Envelope Encryption with Key Versioning (Chosen)** | Industry-standard authenticated encryption; cryptographic tampering detection; explicit key versioning enables zero-downtime key rotation; decoupled from external KMS dependencies in dev/test. | Requires implementing `ISecretEncryptionService` and managing key versions. | Accepted. Delivers maximum security, operational control, and portability. |

## Consequences

- **Product impact:** Merchants can connect sensitive social channels knowing credentials are cryptographically protected.
- **Architecture impact:** `Kreyora.Domain.Integrations` introduces `EncryptedSecret`. `Kreyora.Infrastructure` provides `AesGcmSecretEncryptionService`.
- **Security/privacy impact:** Database dumps, backups, or read replicas do not expose raw social channel credentials. Tampering is immediately detected and rejected.
- **Cost/operations impact:** AES-GCM is hardware-accelerated on modern CPUs (AES-NI / ARM Cryptography Extensions) with negligible CPU overhead.
- **Migration or rollback impact:** Future migration to cloud KMS requires only implementing `ISecretEncryptionService` with the cloud SDK without changing domain models.

## Validation evidence

- Unit tests in `AesGcmSecretEncryptionServiceTests` verify encryption, decryption, authentication tag verification, tampering detection, and key versioning.
- Integration tests confirm encrypted persistence in PostgreSQL.

## Supersession conditions

- Mandatory enterprise customer compliance requiring hardware security modules (HSM) directly for every cryptographic operation.

