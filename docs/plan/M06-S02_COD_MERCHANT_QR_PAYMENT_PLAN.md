# M06-S02 — COD and Merchant-QR Payment Domain

## Context

Milestone 06 Step 01 established the pure domain transition policy engine, Order aggregate state-transition methods, and `OrderOperationService` with role authorization, `xmin` concurrency, `OrderCommand` idempotency, and audit logging. The `Order` aggregate currently tracks `PaymentStatus` (Pending, AwaitingVerification, Paid, Failed, Refunded) and `OrderPaymentMethod` (CashOnDelivery, MerchantQr) as enums, and the M06-S01 transition policy already enforces:

- Merchant QR orders cannot be dispatched until payment is verified (Paid)
- COD orders stay payment Pending through fulfilment; collection requires dispatched or delivered status
- Payment verification/rejection and COD collection require `PaymentsManage` (Owner/Admin only)

However, there is currently **no dedicated payment domain model** — the Order aggregate directly transitions `PaymentStatus` without recording structured payment attempts, evidence, verification decisions, or provider-neutral references. This step builds that domain.

## Objective

Implement the payment domain entities, store payment configuration, and service layer that support the two MVP payment methods (COD and Merchant QR) with structured audit trails, proof handling, and provider-neutral extensibility.

## Source authority

- `docs/plan/plan.md` §10.4 (Payment aggregate: "PaymentAttempt → provider transaction/proof/refund references")
- `docs/plan/plan.md` §10.9 (Payments: COD and merchant QR/manual verification, no "paid" without explicit verification or gateway evidence)
- `docs/milestones/06_ORDER_OPERATIONS_PAYMENTS_NOTIFICATIONS.md` Prompt 02
- ADR-001 (service-layer pattern), ADR-008 (commerce provenance)

## Scope

### Allowed

- New domain entities: `StorePaymentConfiguration`, `PaymentAttempt`, `PaymentProof`, `PaymentVerificationDecision`, `CodCollectionRecord`
- New enums: `PaymentAttemptStatus`, `PaymentProofStatus`
- Store payment method configuration (which methods are enabled per store, merchant QR display data)
- Payment attempt lifecycle tied to orders
- Proof upload through existing `IPrivateObjectStorage` boundary (not the catalog `MediaAsset`)
- Verification decision with immutable audit
- COD collection record
- Provider-neutral transaction references (extensible for future gateways)
- Database migration for new entities
- Application service: `IPaymentService`
- API endpoints for payment operations
- Authorization enforcement (`PaymentsManage`, `PaymentsRead`)
- Idempotency for payment operations
- Unit and integration tests

### Prohibited

- Fake eSewa/Khalti integration (explicit milestone constraint)
- Live gateway adapters
- Modifying the approved M06-S01 transition policy engine
- Frontend integration (deferred to M06-S05)
- Notification creation (deferred to M06-S04)
- Inventory coordination changes (deferred to M06-S03)

---

## Design

### 1. Store Payment Configuration

Add a `StorePaymentConfiguration` entity to the `Kreyora.Domain.Storefront` module. This records which payment methods a store has enabled and any method-specific configuration.

```csharp
// Kreyora.Domain.Storefront.StorePaymentConfiguration
public sealed class StorePaymentConfiguration : BaseEntity, ITenantOwned
{
    public string TenantId { get; private set; }
    public string StoreId { get; private set; }
    public bool CodEnabled { get; private set; }
    public bool MerchantQrEnabled { get; private set; }
    public string? MerchantQrInstructions { get; private set; }  // e.g. "Pay to 98XXXXXXXX on eSewa"
    public string? MerchantQrMediaAssetId { get; private set; }  // optional QR code image reference
}
```

**Design decisions:**
- One configuration record per store (1:1 relationship via unique `(TenantId, StoreId)`)
- `CodEnabled` / `MerchantQrEnabled` flags — simple boolean availability
- `MerchantQrInstructions` — free text the seller provides (e.g., "Pay to eSewa number 9841XXXXXX")
- `MerchantQrMediaAssetId` — optional reference to an uploaded QR code image through the existing media boundary
- No gateway configuration — that's Phase 2

### 2. Payment Attempt Entity

New `PaymentAttempt` aggregate in `Kreyora.Domain.Payments`:

```csharp
public sealed class PaymentAttempt : BaseEntity, ITenantOwned
{
    public string TenantId { get; private set; }
    public string OrderId { get; private set; }
    public OrderPaymentMethod Method { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public decimal AmountNpr { get; private set; }
    public string Currency { get; private set; } = "NPR";
    public string? ProviderReference { get; private set; }    // provider-neutral external ref
    public string? InternalReference { get; private set; }    // system-generated reference
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public DateTimeOffset? CollectedAt { get; private set; }
    public string? VerifiedByUserId { get; private set; }
    public string? RejectedByUserId { get; private set; }
    public string? CollectedByUserId { get; private set; }
    public string? RejectionReason { get; private set; }
}
```

**`PaymentAttemptStatus` enum:**
```
Pending          — COD: awaiting delivery+collection; MerchantQr: created but no proof yet
AwaitingProof    — MerchantQr: attempt created, waiting for customer proof
ProofSubmitted   — MerchantQr: proof uploaded, awaiting seller verification  
Verified         — Payment accepted by authorized seller
Rejected         — Payment rejected by authorized seller (with reason)
Collected        — COD: cash physically collected and recorded
Expired          — Attempt timed out (future use)
```

**Lifecycle:**
- **Merchant QR**: `Pending → AwaitingProof → ProofSubmitted → Verified|Rejected`
  - A PaymentAttempt is created when the order is created with `MerchantQr` method
  - Customer optionally uploads proof (transitions to `ProofSubmitted`)
  - Seller verifies or rejects (with immutable decision record)
- **COD**: `Pending → Collected`
  - A PaymentAttempt is created when the order is created with `CashOnDelivery` method
  - Remains `Pending` until delivery staff records cash collection

**Key invariant:** The `PaymentAttempt` status is the detailed tracking entity. The `Order.PaymentStatus` remains the authoritative high-level state — `PaymentAttempt` verification/collection triggers the `Order` aggregate's `VerifyPayment()` / `MarkCodCollected()` methods from S01.

### 3. Payment Proof Entity

```csharp
public sealed class PaymentProof : BaseEntity, ITenantOwned
{
    public string TenantId { get; private set; }
    public string PaymentAttemptId { get; private set; }
    public string ObjectKey { get; private set; }         // S3-compatible storage path
    public string ContentType { get; private set; }
    public long ByteSize { get; private set; }
    public PaymentProofStatus Status { get; private set; }
    public string? SubmittedByInfo { get; private set; }   // customer identifier/name (non-PII where possible)
}
```

**`PaymentProofStatus` enum:** `UploadPending | Ready | DeletionPending | Deleted`

This mirrors the existing `MediaAsset` lifecycle pattern but is a separate entity in the Payments module, since:
- Payment proofs are not catalog media — different access control, retention, and visibility rules
- Proofs must be visible only to authorized sellers (`PaymentsManage` or `PaymentsRead`)
- Proofs use the same `IPrivateObjectStorage` backend but with a different object key prefix (`payments/proofs/{tenantId}/{attemptId}/`)

### 4. Payment Verification Decision (Immutable Audit Record)

Rather than creating a separate entity, verification decisions are recorded as immutable fields on `PaymentAttempt` itself (`VerifiedAt`, `VerifiedByUserId`, `RejectedAt`, `RejectedByUserId`, `RejectionReason`) plus an `AuditEvent` via the existing `IAuditEventService`. This keeps the model simpler while maintaining a complete audit trail.

The decision is immutable: once a `PaymentAttempt` is `Verified` or `Rejected`, the fields cannot be changed. A rejected attempt can trigger a new attempt cycle (the order can have multiple attempts).

### 5. COD Collection Record

COD collection is recorded on the `PaymentAttempt` entity itself via `CollectedAt` and `CollectedByUserId` fields, plus audit event. This avoids a separate entity while maintaining the audit trail.

### 6. Integration with Order Aggregate

The `OrderOperationService.ExecuteActionAsync` (from S01) already handles `VerifyPayment`, `RejectPayment`, and `MarkCodCollected` actions. M06-S02 extends the service to also update the corresponding `PaymentAttempt` record within the same transaction:

- `VerifyPayment` → sets `PaymentAttempt.Status = Verified`, records `VerifiedAt` and `VerifiedByUserId`
- `RejectPayment` → sets `PaymentAttempt.Status = Rejected`, records `RejectedAt`, `RejectedByUserId`, `RejectionReason`
- `MarkCodCollected` → sets `PaymentAttempt.Status = Collected`, records `CollectedAt` and `CollectedByUserId`

### 7. Provider-Neutral Transaction References

`PaymentAttempt.ProviderReference` stores external payment references (e.g., a future eSewa transaction ID). `PaymentAttempt.InternalReference` stores a system-generated reference code (e.g., `PAY-{ULID}`) for display to customers. For MVP (COD/QR), `ProviderReference` is nullable — it becomes relevant when gateways are added in Phase 2.

---

## File Plan

### Domain Layer (`Kreyora.Domain`)

#### [NEW] `Payments/PaymentAttempt.cs`
- `PaymentAttempt` entity with factory methods and lifecycle transitions
- `PaymentAttemptStatus` enum
- Invariant enforcement: immutable verification/rejection, amount validation

#### [NEW] `Payments/PaymentProof.cs`
- `PaymentProof` entity with upload lifecycle
- `PaymentProofStatus` enum

#### [NEW] `Storefront/StorePaymentConfiguration.cs`
- `StorePaymentConfiguration` entity with factory and update methods
- Validates at least one payment method is enabled

### Application Layer (`Kreyora.Application`)

#### [NEW] `Payments/PaymentContracts.cs`
- `IPaymentService` interface:
  - `GetPaymentAttemptsAsync(string orderId)` — list attempts for an order
  - `SubmitProofAsync(SubmitPaymentProofRequest)` — upload proof for a merchant QR attempt
  - `GetProofAsync(string proofId)` — read proof content for authorized viewers
- `IStorePaymentConfigurationService` interface:
  - `GetConfigurationAsync(string storeId)` — read configuration
  - `UpdateConfigurationAsync(UpdatePaymentConfigurationRequest)` — update settings

#### [MODIFY] `Orders/OrderContracts.cs`
- Extend `ExecuteOrderActionRequest` with optional `PaymentAttemptId` field for linking verification/collection actions to specific attempts
- Extend `OrderOperationResult` with `PaymentAttemptId` when applicable

### Infrastructure Layer (`Kreyora.Infrastructure`)

#### [NEW] `Payments/PaymentService.cs`
- Implements `IPaymentService` with authorization, idempotency, audit
- Proof upload delegates to `IPrivateObjectStorage`

#### [NEW] `Payments/StorePaymentConfigurationService.cs`
- Implements `IStorePaymentConfigurationService`

#### [MODIFY] `Orders/OrderOperationService.cs`
- Extend `ExecuteActionAsync` to transactionally update `PaymentAttempt` status when executing payment-related actions

#### [NEW] `Persistence/Configurations/PaymentAttemptConfiguration.cs`
- EF Core configuration for `payment_attempts` table
- Indexes: `(TenantId, OrderId)`, `(TenantId, InternalReference)` unique

#### [NEW] `Persistence/Configurations/PaymentProofConfiguration.cs`
- EF Core configuration for `payment_proofs` table
- Index: `(TenantId, PaymentAttemptId)`

#### [NEW] `Persistence/Configurations/StorePaymentConfigurationConfiguration.cs`
- EF Core configuration for `store_payment_configurations` table
- Unique index: `(TenantId, StoreId)`

#### [MODIFY] `Persistence/AppDbContext.cs`
- Add `DbSet<PaymentAttempt>`, `DbSet<PaymentProof>`, `DbSet<StorePaymentConfiguration>`
- Add tenant query filters
- Add append-only enforcement for payment verification/proof state

#### [MODIFY] `DependencyInjection.cs`
- Register `IPaymentService` and `IStorePaymentConfigurationService`

#### [NEW] `Persistence/Migrations/YYYYMMDDHHMMSS_AddPaymentDomain.cs`
- Migration for `payment_attempts`, `payment_proofs`, `store_payment_configurations` tables

### WebApi Layer (`Kreyora.WebApi`)

#### [NEW] `Controllers/PaymentController.cs`
- `GET /v1/orders/{orderId}/payments` — list payment attempts
- `POST /v1/orders/{orderId}/payments/{attemptId}/proof` — submit proof
- `GET /v1/payments/proofs/{proofId}` — read proof content

#### [NEW] `Controllers/StorePaymentConfigurationController.cs`
- `GET /v1/store/payment-configuration` — get payment configuration
- `PUT /v1/store/payment-configuration` — update payment configuration

### Tests

#### [NEW] `Kreyora.UnitTests/Domain/PaymentAttemptTests.cs`
- Factory creation for COD and MerchantQr attempts
- Status transition validity (Verified, Rejected, Collected)
- Immutability of verification/rejection decisions
- Amount validation
- Internal reference generation

#### [NEW] `Kreyora.UnitTests/Domain/PaymentProofTests.cs`
- Upload lifecycle: pending → ready → deletion
- Validation of content type, size

#### [NEW] `Kreyora.UnitTests/Domain/StorePaymentConfigurationTests.cs`
- Create/update with valid settings
- At least one method must be enabled
- QR instructions/media validation

#### [NEW] `Kreyora.IntegrationTests/Payments/PaymentServiceTests.cs`
- Real PostgreSQL tests for:
  - MerchantQr: create attempt → submit proof → verify payment → Order.PaymentStatus becomes Paid
  - MerchantQr: create attempt → submit proof → reject payment (with reason) → Order.PaymentStatus becomes Failed
  - COD: create attempt → mark collected → Order.PaymentStatus becomes Paid
  - Idempotent proof submission
  - Authorization enforcement (Operator cannot verify/reject)
  - Cross-tenant isolation
  - Concurrent verification conflict

---

## Invariants

1. **No payment is "paid" without explicit seller verification or collection recording** — browser/client cannot set Paid
2. **Verification decisions are immutable** — once Verified or Rejected, the PaymentAttempt fields cannot be changed
3. **Payment proof is visible only to authorized sellers** — requires `PaymentsManage` or `PaymentsRead`
4. **COD payment stays Pending** until delivery staff records collection (requires dispatched/delivered order)
5. **MerchantQr dispatch requires Paid** — existing S01 policy enforces this
6. **Amount on PaymentAttempt must match Order.TotalNpr** — server-authoritative, never accepted from client
7. **One active PaymentAttempt per order** — for MVP, a rejected attempt creates a new one; only one can be in a non-terminal state
8. **Proof storage uses tenant-prefixed object keys** — `payments/proofs/{tenantId}/{attemptId}/{filename}`
9. **All payment mutations require audit events** — via `IAuditEventService`
10. **Tenant isolation** — `TenantId` on all entities, query filters, ownership verification

## Migration Strategy

- New tables only; no modification to existing `orders` table
- Backward compatible — existing orders continue to work
- PaymentAttempts are retroactively created for existing orders via a data migration helper (optional, can be deferred)

## Rollback

- Drop the new tables (`payment_attempts`, `payment_proofs`, `store_payment_configurations`)
- Revert service registrations
- No impact on existing order data

## Acceptance Criteria (from Prompt 02)

- [ ] Store payment configuration works
- [ ] PaymentAttempt entity tracks the full payment lifecycle
- [ ] Manual payment evidence/proof can be uploaded through the existing media-security boundary
- [ ] Verification decision is immutable and audited
- [ ] COD collection is recorded according to policy
- [ ] Provider-neutral transaction references are in place
- [ ] Migrations, service methods, endpoints, authorization, idempotency, and tests all pass
- [ ] No fake eSewa/Khalti integration
- [ ] `dotnet ef migrations has-pending-model-changes` passes after migration
- [ ] All existing tests continue to pass

## Open Questions

None — the design follows directly from the plan authority (§10.4, §10.9) and the milestone prompt.

