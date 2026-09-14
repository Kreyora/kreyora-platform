using System.Text;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Orders;
using Kreyora.Application.Payments;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Common;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Media;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Payments;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Payments;

public sealed class PaymentServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] ValidJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private readonly PostgresFixture fixture;

    public PaymentServiceTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task GetPaymentAttempts_ReturnsAttempts_ForOrder()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "pay-get-attempts");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr);
        var attempt = PaymentAttempt.Create(tenant.Id, order.Id, OrderPaymentMethod.MerchantQr, order.TotalNpr);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var paymentService = CreatePaymentService(db, accessor, new InMemoryPrivateObjectStorage());
        var result = await paymentService.GetPaymentAttemptsAsync(order.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(attempt.Id, result.Value![0].Id);
        Assert.Equal(PaymentAttemptStatus.AwaitingProof, result.Value[0].Status);
        Assert.Equal(order.TotalNpr, result.Value[0].AmountNpr);
    }

    [Fact]
    public async Task ProofUploadAndComplete_TransitionsAttemptToProofSubmitted()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "pay-proof-upload");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr);
        var attempt = PaymentAttempt.Create(tenant.Id, order.Id, OrderPaymentMethod.MerchantQr, order.TotalNpr);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var storage = new InMemoryPrivateObjectStorage();
        var paymentService = CreatePaymentService(db, accessor, storage);

        // 1. Initiate proof upload
        var initiateResult = await paymentService.InitiateProofUploadAsync(new InitiatePaymentProofUploadRequest(
            order.Id,
            attempt.Id,
            "image/png",
            ValidPngBytes.Length,
            "Customer transaction screenshot"));

        Assert.True(initiateResult.IsSuccess);
        Assert.Equal(PaymentProofStatus.UploadPending, initiateResult.Value!.Status);
        Assert.Equal("Customer transaction screenshot", initiateResult.Value.CustomerNote);

        // 2. Complete proof upload
        using var stream = new MemoryStream(ValidPngBytes);
        var completeResult = await paymentService.CompleteProofUploadAsync(initiateResult.Value.Id, stream);

        Assert.True(completeResult.IsSuccess);
        Assert.Equal(PaymentProofStatus.Ready, completeResult.Value!.Status);
        Assert.NotNull(completeResult.Value.ReadyAt);

        // 3. Verify PaymentAttempt transitioned to ProofSubmitted
        var attemptsResult = await paymentService.GetPaymentAttemptsAsync(order.Id);
        Assert.True(attemptsResult.IsSuccess);
        Assert.Equal(PaymentAttemptStatus.ProofSubmitted, attemptsResult.Value![0].Status);
        Assert.Single(attemptsResult.Value[0].Proofs);

        // 4. Verify Content retrieval
        var contentResult = await paymentService.GetProofContentAsync(completeResult.Value.Id);
        Assert.True(contentResult.IsSuccess);
        Assert.Equal("image/png", contentResult.Value!.ContentType);
        Assert.Equal(ValidPngBytes.Length, contentResult.Value.ByteSize);
    }

    [Fact]
    public async Task CompleteProofUpload_FailsOnMismatchedSignature()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "pay-proof-corrupt");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr);
        var attempt = PaymentAttempt.Create(tenant.Id, order.Id, OrderPaymentMethod.MerchantQr, order.TotalNpr);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var storage = new InMemoryPrivateObjectStorage();
        var paymentService = CreatePaymentService(db, accessor, storage);

        var initiateResult = await paymentService.InitiateProofUploadAsync(new InitiatePaymentProofUploadRequest(
            order.Id,
            attempt.Id,
            "image/jpeg",
            ValidPngBytes.Length)); // Declared JPEG but passing PNG bytes

        Assert.True(initiateResult.IsSuccess);

        using var stream = new MemoryStream(ValidPngBytes);
        var completeResult = await paymentService.CompleteProofUploadAsync(initiateResult.Value!.Id, stream);

        Assert.True(completeResult.IsFailure);
        Assert.Contains("declared type", completeResult.Error!.Detail);
    }

    [Fact]
    public async Task FullMerchantQr_VerifyFlow_SetsOrderPaid_AndPaymentAttemptVerified()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "pay-qr-verify-flow");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr);
        var attempt = PaymentAttempt.Create(tenant.Id, order.Id, OrderPaymentMethod.MerchantQr, order.TotalNpr);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var storage = new InMemoryPrivateObjectStorage();
        var paymentService = CreatePaymentService(db, accessor, storage);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var orderService = CreateOrderOperationService(db, accessor, clock);

        // 1. Customer uploads proof
        var initProof = await paymentService.InitiateProofUploadAsync(new InitiatePaymentProofUploadRequest(
            order.Id, attempt.Id, "image/jpeg", ValidJpegBytes.Length));
        using var stream = new MemoryStream(ValidJpegBytes);
        await paymentService.CompleteProofUploadAsync(initProof.Value!.Id, stream);

        // 2. Owner verifies payment
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;
        var verifyResult = await orderService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id,
            OrderAction.VerifyPayment,
            null,
            version,
            "key-verify-1",
            PaymentAttemptId: attempt.Id,
            ProviderReference: "ESEWA-9999"));

        Assert.True(verifyResult.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, verifyResult.Value!.PaymentStatus);

        // 3. Check PaymentAttempt entity state
        await using var readDb = fixture.CreateDbContext(accessor);
        var updatedAttempt = await readDb.PaymentAttempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Verified, updatedAttempt.Status);
        Assert.NotNull(updatedAttempt.VerifiedAt);
        Assert.Equal("01J00000000000000000000001", updatedAttempt.VerifiedByUserId);
        Assert.Equal("ESEWA-9999", updatedAttempt.ProviderReference);
    }

    [Fact]
    public async Task FullMerchantQr_RejectFlow_SetsOrderFailed_AndPaymentAttemptRejected()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "pay-qr-reject-flow");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr);
        var attempt = PaymentAttempt.Create(tenant.Id, order.Id, OrderPaymentMethod.MerchantQr, order.TotalNpr);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var orderService = CreateOrderOperationService(db, accessor, clock);

        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;
        var rejectResult = await orderService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id,
            OrderAction.RejectPayment,
            "Payment slip is illegible or amount does not match.",
            version,
            "key-reject-1",
            PaymentAttemptId: attempt.Id));

        Assert.True(rejectResult.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, rejectResult.Value!.PaymentStatus);

        await using var readDb = fixture.CreateDbContext(accessor);
        var updatedAttempt = await readDb.PaymentAttempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Rejected, updatedAttempt.Status);
        Assert.NotNull(updatedAttempt.RejectedAt);
        Assert.Equal("01J00000000000000000000001", updatedAttempt.RejectedByUserId);
        Assert.Equal("Payment slip is illegible or amount does not match.", updatedAttempt.RejectionReason);
    }

    [Fact]
    public async Task FullCod_CollectFlow_SetsOrderPaid_AndPaymentAttemptCollected()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "pay-cod-collect-flow");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery);
        var attempt = PaymentAttempt.Create(tenant.Id, order.Id, OrderPaymentMethod.CashOnDelivery, order.TotalNpr);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var orderService = CreateOrderOperationService(db, accessor, clock);

        // Confirm, prepare, dispatch
        var v1 = db.Entry(order).Property<uint>("xmin").CurrentValue;
        var r1 = await orderService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, v1, "k-conf"));
        var r2 = await orderService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, r1.Value!.RowVersion, "k-prep"));
        var r3 = await orderService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, r2.Value!.RowVersion, "k-disp"));

        // Collect payment
        var collectResult = await orderService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id,
            OrderAction.MarkCodCollected,
            null,
            r3.Value!.RowVersion,
            "k-collect",
            PaymentAttemptId: attempt.Id,
            ProviderReference: "CASH-REC-456"));

        Assert.True(collectResult.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, collectResult.Value!.PaymentStatus);

        await using var readDb = fixture.CreateDbContext(accessor);
        var updatedAttempt = await readDb.PaymentAttempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Collected, updatedAttempt.Status);
        Assert.NotNull(updatedAttempt.CollectedAt);
        Assert.Equal("01J00000000000000000000001", updatedAttempt.CollectedByUserId);
        Assert.Equal("CASH-REC-456", updatedAttempt.ProviderReference);
    }

    [Fact]
    public async Task CrossTenant_CannotAccessPaymentAttemptsOrProofs()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "pay-cross-a");
        var tenantB = await CreateTenantAsync(db, "pay-cross-b");

        Order orderA;
        PaymentAttempt attemptA;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            orderA = await CreateDirectOrderAsync(db, tenantA.Id, OrderPaymentMethod.MerchantQr);
            attemptA = PaymentAttempt.Create(tenantA.Id, orderA.Id, OrderPaymentMethod.MerchantQr, orderA.TotalNpr);
            db.PaymentAttempts.Add(attemptA);
            await db.SaveChangesAsync();
        }

        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var serviceB = CreatePaymentService(db, accessor, new InMemoryPrivateObjectStorage());

            // 1. Tenant B cannot get Tenant A's attempts
            var attemptsResult = await serviceB.GetPaymentAttemptsAsync(orderA.Id);
            Assert.True(attemptsResult.IsFailure);
            Assert.Equal(404, attemptsResult.Error!.Status);

            // 2. Tenant B cannot initiate proof upload on Tenant A's attempt
            var uploadResult = await serviceB.InitiateProofUploadAsync(new InitiatePaymentProofUploadRequest(
                orderA.Id, attemptA.Id, "image/png", ValidPngBytes.Length));
            Assert.True(uploadResult.IsFailure);
            Assert.Equal(404, uploadResult.Error!.Status);
        }
    }

    private static PaymentService CreatePaymentService(AppDbContext db, TenantContextAccessor accessor, IPrivateObjectStorage storage)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("payment-test"), authorizer);
        return new PaymentService(
            db,
            accessor,
            authorizer,
            new TenantKeyBuilder(accessor),
            storage,
            audit,
            new MutableTimeProvider(DateTimeOffset.UtcNow),
            Options.Create(new MediaStorageOptions { MaxUploadBytes = 10_000_000, UploadLifetimeMinutes = 15 }));
    }

    private static OrderOperationService CreateOrderOperationService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("payment-order-test"), authorizer);
        var options = Options.Create(new InventoryReservationOptions());
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, options);
        return new OrderOperationService(db, accessor, authorizer, inventory, audit, clock);
    }

    private static TenantContext OwnerContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Order> CreateDirectOrderAsync(AppDbContext db, string tenantId, OrderPaymentMethod paymentMethod)
    {
        var store = Store.Create(tenantId, new StoreSettings(
            "Test Store",
            $"test-{Guid.NewGuid():N}"[..20],
            null,
            StoreThemePreset.Default,
            null,
            "Kreyora",
            "test@example.com",
            null, null, null, null, null,
            "Terms", "Privacy", "Returns", "Payment"));

        var rule = DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings(
            "Test Delivery",
            0,
            DeliveryFeeType.Flat,
            100m,
            null,
            "1-2 days",
            true,
            true,
            [new DeliveryZoneInput("Kathmandu", "KMC", "Baluwatar")]));

        db.AddRange(store, rule);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var session = CheckoutSession.Create(new CheckoutSessionCreation(
            tenantId, store.Id, null, new string('a', 64), now.AddMinutes(15), now.AddMinutes(15),
            "Hari Thapa", "+9779800000001", "hari@example.com", "Baluwatar", null, "Kathmandu",
            "KMC", "Baluwatar", null, new string('b', 64), now.AddDays(30), 1500m, 0m, 100m,
            0m, 0m, 0m, 1600m, "NPR", rule.Id, rule.Name, "1-2 days", true, now));
        session.Complete(now);
        db.CheckoutSessions.Add(session);
        await db.SaveChangesAsync();

        var order = Order.Create(new OrderCreation(
            tenantId, store.Id, session.Id, null, paymentMethod,
            "Hari Thapa", "+9779800000001", "hari@example.com", "Baluwatar", null, "Kathmandu",
            "KMC", "Baluwatar", null, 1500m, 0m, 100m, 0m, 0m, 0m, 1600m, "NPR", rule.Id, rule.Name, "1-2 days", true));

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : Kreyora.Domain.Abstractions.ITimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class InMemoryPrivateObjectStorage : IPrivateObjectStorage
    {
        private readonly Dictionary<string, byte[]> storage = new();

        public Task PutAsync(StorageObjectWrite request, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            request.Content.CopyTo(ms);
            storage[request.ObjectKey] = ms.ToArray();
            return Task.CompletedTask;
        }

        public Task<StorageObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            if (storage.TryGetValue(objectKey, out var bytes))
            {
                return Task.FromResult<StorageObjectMetadata?>(new StorageObjectMetadata(objectKey, "application/octet-stream", bytes.Length));
            }
            return Task.FromResult<StorageObjectMetadata?>(null);
        }

        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            if (storage.TryGetValue(objectKey, out var bytes))
            {
                return Task.FromResult<Stream?>(new MemoryStream(bytes));
            }
            return Task.FromResult<Stream?>(null);
        }

        public Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            storage.Remove(objectKey);
            return Task.CompletedTask;
        }
    }
}

