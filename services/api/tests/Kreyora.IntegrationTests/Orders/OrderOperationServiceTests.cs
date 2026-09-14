using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Orders;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Common;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Orders;

public sealed class OrderOperationServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public OrderOperationServiceTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task GetAllowedActions_ReturnsAccurateEvaluations_ForRole()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-actions-eval");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, accessor, clock);

        // 1. Owner evaluations
        var ownerResult = await service.GetAllowedActionsAsync(order.Id);
        Assert.True(ownerResult.IsSuccess);
        var ownerActions = ownerResult.Value!;
        var confirmAction = ownerActions.Single(a => a.Action == OrderAction.Confirm);
        Assert.True(confirmAction.IsAllowed);
        var dispatchAction = ownerActions.Single(a => a.Action == OrderAction.Dispatch);
        Assert.False(dispatchAction.IsAllowed);
        Assert.Equal(OrderActionDenialReasons.OrderNotReady, dispatchAction.DenialReason);

        // 2. Operator evaluations
        using (accessor.BeginScope(OperatorContext(tenant.Id)))
        {
            var opResult = await service.GetAllowedActionsAsync(order.Id);
            Assert.True(opResult.IsSuccess);
            var opVerifyAction = opResult.Value!.Single(a => a.Action == OrderAction.VerifyPayment);
            Assert.False(opVerifyAction.IsAllowed);
            Assert.Equal(OrderActionDenialReasons.RoleNotAuthorized, opVerifyAction.DenialReason);
        }

        // 3. Viewer evaluations
        using (accessor.BeginScope(ViewerContext(tenant.Id)))
        {
            var viewerResult = await service.GetAllowedActionsAsync(order.Id);
            Assert.True(viewerResult.IsSuccess);
            Assert.All(viewerResult.Value!, a => Assert.False(a.IsAllowed));
        }
    }

    [Fact]
    public async Task FullCodOrderLifecycle_TransitionsCorrectly_AndAppendsAuditEvents()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-cod-lifecycle");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, accessor, clock);

        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // 1. Confirm
        var confirmResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, version, "key-confirm"));
        Assert.True(confirmResult.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, confirmResult.Value!.Status);
        version = confirmResult.Value.RowVersion;

        // 2. Prepare (Mark Ready)
        var prepareResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Prepare, null, version, "key-prepare"));
        Assert.True(prepareResult.IsSuccess);
        Assert.Equal(OrderStatus.Processing, prepareResult.Value!.Status);
        Assert.Equal(FulfilmentStatus.Ready, prepareResult.Value.FulfilmentStatus);
        version = prepareResult.Value.RowVersion;

        // 3. Dispatch
        var dispatchResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Dispatch, null, version, "key-dispatch"));
        Assert.True(dispatchResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Dispatched, dispatchResult.Value!.FulfilmentStatus);
        version = dispatchResult.Value.RowVersion;

        // 4. Deliver
        var deliverResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Deliver, null, version, "key-deliver"));
        Assert.True(deliverResult.IsSuccess);
        Assert.Equal(OrderStatus.Fulfilled, deliverResult.Value!.Status);
        Assert.Equal(FulfilmentStatus.Delivered, deliverResult.Value.FulfilmentStatus);
        version = deliverResult.Value.RowVersion;

        // 5. Mark COD Collected
        var collectResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.MarkCodCollected, null, version, "key-collect"));
        Assert.True(collectResult.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, collectResult.Value!.PaymentStatus);

        // Verify audit trail in PostgreSQL
        db.ChangeTracker.Clear();
        var audits = await db.AuditEvents.Where(a => a.TargetId == order.Id).OrderBy(a => a.CreatedAt).ToListAsync();
        Assert.Equal(5, audits.Count);
        Assert.Contains(audits, a => a.Action == "order.confirm");
        Assert.Contains(audits, a => a.Action == "order.prepare");
        Assert.Contains(audits, a => a.Action == "order.dispatch");
        Assert.Contains(audits, a => a.Action == "order.deliver");
        Assert.Contains(audits, a => a.Action == "order.markcodcollected");
    }

    [Fact]
    public async Task FullMerchantQrLifecycle_RequiresVerificationBeforeDispatch()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-qr-lifecycle");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, accessor, clock);

        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // Confirm
        var confirmResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, version, "qr-confirm"));
        Assert.True(confirmResult.IsSuccess);
        version = confirmResult.Value!.RowVersion;

        // Prepare
        var prepareResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Prepare, null, version, "qr-prepare"));
        Assert.True(prepareResult.IsSuccess);
        version = prepareResult.Value!.RowVersion;

        // Attempt Dispatch before verification: must fail!
        var unverifiedDispatch = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Dispatch, null, version, "qr-dispatch-fail"));
        Assert.True(unverifiedDispatch.IsFailure);
        Assert.Equal(OrderActionDenialReasons.MerchantQrPaymentUnverified, unverifiedDispatch.Error!.Detail);

        // Verify payment
        var verifyResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.VerifyPayment, null, version, "qr-verify"));
        Assert.True(verifyResult.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, verifyResult.Value!.PaymentStatus);
        version = verifyResult.Value.RowVersion;

        // Dispatch now succeeds
        var dispatchResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Dispatch, null, version, "qr-dispatch-success"));
        Assert.True(dispatchResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Dispatched, dispatchResult.Value!.FulfilmentStatus);
    }

    [Fact]
    public async Task Cancellation_CapturesReason_AndPreventsFurtherMutations()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-cancel");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, accessor, clock);

        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // Missing reason fails validation
        var missingReason = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "ab", version, "cancel-short"));
        Assert.True(missingReason.IsFailure);
        Assert.Equal(OrderActionDenialReasons.ReasonRequired, missingReason.Error!.Detail);

        // Valid cancellation
        var cancelResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer requested order cancellation", version, "cancel-valid"));
        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, cancelResult.Value!.Status);
        Assert.Equal(FulfilmentStatus.Cancelled, cancelResult.Value.FulfilmentStatus);
        version = cancelResult.Value.RowVersion;

        // Further actions fail
        var prepareAttempt = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Prepare, null, version, "cancel-prepare-fail"));
        Assert.True(prepareAttempt.IsFailure);
        Assert.Equal(OrderActionDenialReasons.OrderAlreadyCancelled, prepareAttempt.Error!.Detail);
    }

    [Fact]
    public async Task OptimisticConcurrency_DetectsStaleVersion()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-concurrency");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, accessor, clock);

        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // Pass incorrect version
        var conflict = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, version + 9999, "key-conflict"));

        Assert.True(conflict.IsFailure);
        Assert.Equal(409, conflict.Error!.Status);
    }

    [Fact]
    public async Task Idempotency_ReplaysIdenticalCommand_AndRejectsConflictingPayload()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-idempotency");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, accessor, clock);

        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;
        var request = new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "idem-key-1");

        // First execution
        var first = await service.ExecuteActionAsync(request);
        Assert.True(first.IsSuccess);
        Assert.False(first.Value!.WasReplayed);

        // Replay with identical payload
        var replay = await service.ExecuteActionAsync(request);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.WasReplayed);
        Assert.Equal(first.Value.OrderId, replay.Value.OrderId);

        // Replay with different payload/reason
        var conflict = await service.ExecuteActionAsync(request with { Reason = "Different reason" });
        Assert.True(conflict.IsFailure);
        Assert.Equal(409, conflict.Error!.Status);
    }

    [Fact]
    public async Task RoleEnforcement_BlocksUnauthorizedRoles()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-rbac");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, accessor, clock);

        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // Operator attempting VerifyPayment -> 403 Forbidden
        using (accessor.BeginScope(OperatorContext(tenant.Id)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.VerifyPayment, null, version, "op-verify")));
        }

        // Viewer attempting Confirm -> 403 Forbidden
        using (accessor.BeginScope(ViewerContext(tenant.Id)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "viewer-confirm")));
        }
    }

    [Fact]
    public async Task CrossTenantIsolation_CannotAccessOrMutateOtherTenantOrder()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "order-iso-a");
        var tenantB = await CreateTenantAsync(db, "order-iso-b");

        // Tenant A creates order
        Order orderA;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            orderA = await CreateDirectOrderAsync(db, tenantA.Id, OrderPaymentMethod.CashOnDelivery);
        }

        // Tenant B attempts to read actions or mutate order A
        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var serviceB = CreateService(db, accessor, clock);

            var readResult = await serviceB.GetAllowedActionsAsync(orderA.Id);
            Assert.True(readResult.IsFailure);
            Assert.Equal(404, readResult.Error!.Status);

            var mutateResult = await serviceB.ExecuteActionAsync(new ExecuteOrderActionRequest(
                orderA.Id, OrderAction.Confirm, null, 1, "cross-tenant-key"));
            Assert.True(mutateResult.IsFailure);
            Assert.Equal(404, mutateResult.Error!.Status);
        }
    }

    private static OrderOperationService CreateService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("order-operation-test"), authorizer);
        var options = Microsoft.Extensions.Options.Options.Create(new InventoryReservationOptions());
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, options);
        return new OrderOperationService(db, accessor, authorizer, inventory, audit, clock);
    }

    private static async Task<Order> CreateDirectOrderAsync(AppDbContext db, string tenantId, OrderPaymentMethod paymentMethod)
    {
        var store = Store.Create(tenantId, new StoreSettings("Test Store", $"test-{Guid.NewGuid():N}"[..20], null, StoreThemePreset.Default, null,
            "Kreyora", "test@example.com", null, null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var rule = DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings("Test Delivery", 0, DeliveryFeeType.Flat, 100m, null, "1-2 days", true, true,
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

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);
    private static TenantContext OperatorContext(string tenantId) => new(tenantId, "01J00000000000000000000003", "01J00000000000000000000004", TenantRole.Operator);
    private static TenantContext ViewerContext(string tenantId) => new(tenantId, "01J00000000000000000000005", "01J00000000000000000000006", TenantRole.Viewer);

    private sealed class Correlation(string correlationId) : ICorrelationContext { public string CorrelationId => correlationId; public void SetCorrelationId(string value) { } }
    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : Kreyora.Domain.Abstractions.ITimeProvider { public DateTimeOffset UtcNow { get; set; } = utcNow; }
}
