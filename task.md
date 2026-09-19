# Handoff: Milestone 07 Step 01 — Provider-Neutral Social Runtime Contracts and Integration ADRs

## 1. Overview

- **Milestone:** 07 — Provider-Neutral Social Integration Runtime
- **Step:** 01 — Provider capability model and integration ADRs
- **Phase:** Phase 2 (Builder) Implementation — Completed
- **Governing Plan:** `docs/plan/M07-S01_SOCIAL_RUNTIME_CONTRACTS_PLAN.md`
- **Active Milestone File:** `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`
- **Current Checkpoint:** `artifacts/checkpoints/M07-S01.md` (REVIEW)
- **Prior Checkpoint:** `artifacts/checkpoints/M06-EXIT.md` (APPROVED)
- **Status:** `REVIEW`

---

## 2. Implementation Checklist (Phase 2 Builder)

- [x] **Task 1: Author and Record 4 Integration ADRs (`docs/decisions/`)**
  - Create `ADR-010-social-connection-ownership.md` (Tenant-level ownership with optional Store binding).
  - Create `ADR-011-normalized-social-event-versioning.md` (Strongly-typed polymorphic envelope with `schemaVersion: "v1"`).
  - Create `ADR-012-raw-webhook-payload-retention.md` (30-day raw retention for replay/debugging with automated purge/redaction).
  - Create `ADR-013-secrets-encryption-and-key-management.md` (AES-256-GCM envelope encryption with key versioning).
  - Update `docs/decisions/ADR_INDEX.md` with ADR-010 through ADR-013.

- [x] **Task 2: Domain Layer Value Objects & Models (`services/api/src/Kreyora.Domain`)**
  - Create `Kreyora.Domain/Integrations/ChannelType.cs` (WhatsApp, Instagram, Messenger, Viber, Telegram, Simulator).
  - Create `Kreyora.Domain/Integrations/ChannelCapabilities.cs` (Immutable value object covering 11 capability domains).
  - Create `Kreyora.Domain/Integrations/ChannelConnectionStatus.cs` (Pending, Active, Degraded, Expired, Revoked, Disabled).
  - Create `Kreyora.Domain/Integrations/EncryptedSecret.cs` (CiphertextBase64, IvBase64, AuthTagBase64, KeyVersion).
  - Create `Kreyora.Domain/Integrations/NormalizedInboundEvents.cs` (Polymorphic `NormalizedInboundEnvelope` and typed payloads).

- [x] **Task 3: Application Layer Contracts (`services/api/src/Kreyora.Application`)**
  - Create `Kreyora.Application/Integrations/ISecretEncryptionService.cs` (Encrypt/Decrypt with key versioning).
  - Create `Kreyora.Application/Integrations/IChannelProvider.cs` (ValidateWebhook, NormalizeInbound, SendMessage, Capabilities, ValidateOrRefreshConnection).
  - Create `Kreyora.Application/Integrations/IChannelProviderRegistry.cs` (Provider resolution by channel type).
  - Create `Kreyora.Application/Integrations/IntegrationContracts.cs` (DTOs, requests, and results).

- [x] **Task 4: Infrastructure Encryption Service (`services/api/src/Kreyora.Infrastructure`)**
  - Implement `AesGcmSecretEncryptionService` in `Kreyora.Infrastructure/Integrations/AesGcmSecretEncryptionService.cs`.
  - Implement `ChannelProviderRegistry` in `Kreyora.Infrastructure/Integrations/ChannelProviderRegistry.cs`.
  - Register services in `DependencyInjection.cs`.

- [x] **Task 5: Unit & Contract Tests (`services/api/tests/`)**
  - Add `AesGcmSecretEncryptionServiceTests` in `Kreyora.UnitTests/Integrations/`.
  - Add `NormalizedInboundEventTests` in `Kreyora.UnitTests/Integrations/`.
  - Add `ChannelCapabilitiesTests` in `Kreyora.UnitTests/Integrations/`.
  - Add `ChannelProviderContractTests` with `FakeSimulatorChannelProvider` in `Kreyora.ContractTests/Integrations/`.

- [x] **Task 6: Quality Gates & Verification**
  - Run full backend solution test suite: `dotnet test services/api/Kreyora.slnx --configuration Release`.
  - Run EF Core model changes check: `dotnet ef migrations has-pending-model-changes ...`.
  - Run full frontend CI gate: `pnpm ci:frontend`.
  - Run git diff check: `git diff --check`.

- [x] **Task 7: Checkpoint & Documentation**
  - Create checkpoint report `artifacts/checkpoints/M07-S01.md` (`REVIEW`).
  - Update `docs/context/CURRENT_WORK.md` and `docs/milestones/07_SOCIAL_INTEGRATION_RUNTIME.md`.
  - Wait for project owner review and approval.
