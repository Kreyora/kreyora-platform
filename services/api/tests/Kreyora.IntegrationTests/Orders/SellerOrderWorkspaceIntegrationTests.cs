using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Inventory;
using Kreyora.Application.Orders;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Customers;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Orders;

public sealed class SellerOrderWorkspaceIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public SellerOrderWorkspaceIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task ListOrders_ReturnsPaginatedOrders_AndAppliesFilters()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "list-orders-test");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order1 = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Sita Sharma", "+9779811111111");
        var order2 = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.MerchantQr, "Gita Shrestha", "+9779822222222");

        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        // 1. List all
        var allResult = await queryService.ListOrdersAsync(new OrderQuery(Page: 1, PageSize: 10));
        Assert.True(allResult.IsSuccess);
        Assert.True(allResult.Value!.TotalCount >= 2);
        Assert.Contains(allResult.Value.Items, o => o.Id == order1.Id);
        Assert.Contains(allResult.Value.Items, o => o.Id == order2.Id);

        // 2. Filter by search (customer name)
        var searchResult = await queryService.ListOrdersAsync(new OrderQuery(Search: "Sita"));
        Assert.True(searchResult.IsSuccess);
        Assert.Single(searchResult.Value!.Items, o => o.Id == order1.Id);
        Assert.DoesNotContain(searchResult.Value.Items, o => o.Id == order2.Id);

        // 3. Filter by payment method
        var qrResult = await queryService.ListOrdersAsync(new OrderQuery(PaymentMethod: OrderPaymentMethod.MerchantQr));
        Assert.True(qrResult.IsSuccess);
        Assert.Contains(qrResult.Value!.Items, o => o.Id == order2.Id);
        Assert.DoesNotContain(qrResult.Value.Items, o => o.Id == order1.Id);
    }

    [Fact]
    public async Task ListOrders_EnforcesTenantIsolation()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "iso-list-a");
        var tenantB = await CreateTenantAsync(db, "iso-list-b");

        string orderAId;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            var orderA = await CreateDirectOrderAsync(db, tenantA.Id, OrderPaymentMethod.CashOnDelivery, "Customer A", "+9779800000001");
            orderAId = orderA.Id;
        }

        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var authorizer = new TenantPermissionAuthorizer(accessor);
            var queryServiceB = new OrderQueryService(db, accessor, authorizer);

            var listResult = await queryServiceB.ListOrdersAsync(new OrderQuery());
            Assert.True(listResult.IsSuccess);
            Assert.DoesNotContain(listResult.Value!.Items, o => o.Id == orderAId);

            var detailResult = await queryServiceB.GetOrderDetailAsync(orderAId);
            Assert.True(detailResult.IsFailure);
            Assert.Equal(404, detailResult.Error!.Status);
        }
    }

    [Fact]
    public async Task GetOrderDetail_ReturnsFullOrder_WithItemsAndPaymentAttempts()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "detail-test");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "detail-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.MerchantQr, "detail-idem-key");

        // Add payment attempt with proof
        var attempt = await db.PaymentAttempts.SingleAsync(pa => pa.OrderId == order.Id);

        var proof = PaymentProof.CreatePending(
            tenant.Id,
            attempt.Id,
            "proofs/object-123.jpg",
            "image/jpeg",
            2048,
            DateTimeOffset.UtcNow.AddHours(1),
            "Customer transaction screenshot");
        proof.Complete(DateTimeOffset.UtcNow);
        db.PaymentProofs.Add(proof);
        await db.SaveChangesAsync();

        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        var result = await queryService.GetOrderDetailAsync(order.Id);
        Assert.True(result.IsSuccess);
        var detail = result.Value!;
        Assert.Equal(order.Id, detail.Id);
        Assert.Equal("Ram Shrestha", detail.CustomerName);
        Assert.Equal(OrderPaymentMethod.MerchantQr, detail.PaymentMethod);
        Assert.True(detail.RowVersion > 0);
        Assert.NotEmpty(detail.Items);
        Assert.Equal(2, detail.Items[0].Quantity);
        Assert.Single(detail.PaymentAttempts);
        Assert.Single(detail.PaymentAttempts[0].Proofs);
        Assert.Equal("image/jpeg", detail.PaymentAttempts[0].Proofs[0].ContentType);
    }

    [Fact]
    public async Task ExecuteAction_Succeeds_AndUpdatesOrderState()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "exec-action-test");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Hari Bahadur", "+9779844444444");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        var initialDetail = await queryService.GetOrderDetailAsync(order.Id);
        Assert.Equal(OrderStatus.PendingConfirmation, initialDetail.Value!.Status);

        // Confirm order
        var executeResult = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            OrderId: order.Id,
            Action: OrderAction.Confirm,
            Reason: null,
            ExpectedVersion: initialDetail.Value.RowVersion,
            IdempotencyKey: "confirm-test-key-1"));

        Assert.True(executeResult.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, executeResult.Value!.Status);

        db.ChangeTracker.Clear();
        var updatedDetail = await queryService.GetOrderDetailAsync(order.Id);
        Assert.Equal(OrderStatus.Confirmed, updatedDetail.Value!.Status);
    }

    [Fact]
    public async Task ExecuteAction_StaleVersion_ReturnsConflict409()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "stale-version-test");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Bikash Shrestha", "+9779855555555");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        var detail = await queryService.GetOrderDetailAsync(order.Id);

        // Execute first action to change version
        var firstResult = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            OrderId: order.Id,
            Action: OrderAction.Confirm,
            Reason: null,
            ExpectedVersion: detail.Value!.RowVersion,
            IdempotencyKey: "first-action-key"));
        Assert.True(firstResult.IsSuccess);

        db.ChangeTracker.Clear();

        // Attempt second action with the original (stale) version
        var conflictResult = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            OrderId: order.Id,
            Action: OrderAction.Prepare,
            Reason: null,
            ExpectedVersion: detail.Value.RowVersion, // Stale!
            IdempotencyKey: "stale-action-key"));

        Assert.True(conflictResult.IsFailure);
        Assert.Equal(409, conflictResult.Error!.Status);
    }

    [Fact]
    public async Task ExecuteAction_ViewerRole_ReturnsForbidden403()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "viewer-forbidden-test");

        Order order;
        uint version;
        using (accessor.BeginScope(OwnerContext(tenant.Id)))
        {
            order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Viewer Test", "+9779866666666");
            var authorizer = new TenantPermissionAuthorizer(accessor);
            var queryService = new OrderQueryService(db, accessor, authorizer);
            var detail = await queryService.GetOrderDetailAsync(order.Id);
            version = detail.Value!.RowVersion;
        }

        using (accessor.BeginScope(ViewerContext(tenant.Id)))
        {
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var operationService = CreateOrderOperationService(db, accessor, clock);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
                    OrderId: order.Id,
                    Action: OrderAction.Confirm,
                    Reason: null,
                    ExpectedVersion: version,
                    IdempotencyKey: "viewer-action-key"));
            });
        }
    }

    [Fact]
    public async Task GetOrderActivity_ReturnsChronologicalAuditTrail()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "activity-audit-test");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Audit Test", "+9779877777777");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        var detail = await queryService.GetOrderDetailAsync(order.Id);

        // Confirm
        await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, detail.Value!.RowVersion, "audit-key-1"));

        db.ChangeTracker.Clear();
        var afterConfirm = await queryService.GetOrderDetailAsync(order.Id);

        // Prepare
        await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Prepare, null, afterConfirm.Value!.RowVersion, "audit-key-2"));

        db.ChangeTracker.Clear();

        // Get activities
        var activitiesResult = await queryService.GetOrderActivityAsync(order.Id);
        Assert.True(activitiesResult.IsSuccess);
        Assert.True(activitiesResult.Value!.Count >= 2);
        Assert.Contains(activitiesResult.Value, a => a.Action == "order.confirm");
        Assert.Contains(activitiesResult.Value, a => a.Action == "order.prepare");
    }

    [Fact]
    public async Task GetOrderNotifications_ReturnsRedactedNotifications()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-query-test");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Notif Customer", "+9779888888888");

        // Simulate an outbox message and a notification request for this order
        var outboxMsg = new OutboxMessage
        {
            TenantId = tenant.Id,
            Type = "order.confirmed.v1",
            Content = $"{{\"orderId\":\"{order.Id}\",\"customerEmail\":\"customer@example.com\"}}",
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow
        };
        db.OutboxMessages.Add(outboxMsg);
        await db.SaveChangesAsync();

        var notifReq = Domain.Notifications.NotificationRequest.Create(
            tenant.Id,
            "order.confirmed.v1",
            outboxMsg.Id,
            "order_confirmed",
            1,
            Domain.Notifications.NotificationChannel.Email,
            "Notif Customer",
            "customer@example.com",
            3,
            "notif-idem-key-1");
        db.NotificationRequests.Add(notifReq);
        await db.SaveChangesAsync();

        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        var result = await queryService.GetOrderNotificationsAsync(order.Id);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        var notif = result.Value![0];
        Assert.Equal("order_confirmed", notif.TemplateCode);
        Assert.Contains("*", notif.RecipientContactRedacted); // Redacted!
        Assert.DoesNotContain("customer@example.com", notif.RecipientContactRedacted);
    }

    // --- Helpers ---

    private static OrderOperationService CreateOrderOperationService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("seller-workspace-test"), authorizer);
        var options = Options.Create(new InventoryReservationOptions());
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, options);
        return new OrderOperationService(db, accessor, authorizer, inventory, audit, clock);
    }

    private static OrderCreationService CreateOrderCreationService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("order-create-test"), authorizer);
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, Options.Create(new InventoryReservationOptions()));
        return new OrderCreationService(db, accessor, inventory, audit, clock);
    }

    private static CheckoutSessionService CreateCheckoutService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock, out StorefrontQuoteService quotes)
    {
        quotes = new StorefrontQuoteService(db, accessor, new StorefrontCatalogReadService(db), new StorefrontInventoryReadService(db), new EphemeralDataProtectionProvider(),
            Options.Create(new StorefrontQuoteOptions { LifetimeMinutes = 10 }), clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("checkout-test"), authorizer);
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, Options.Create(new InventoryReservationOptions()));
        return new CheckoutSessionService(db, accessor, quotes, new CustomerCheckoutService(db, accessor), inventory, audit, clock,
            Options.Create(new CheckoutSessionOptions { LifetimeMinutes = 10, PiiReviewDays = 30, ExpiryBatchSize = 100 }));
    }

    private static async Task<Order> CreateOrderFromCheckoutAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        MutableTimeProvider clock,
        string storeId,
        string variantId,
        int quantity,
        OrderPaymentMethod paymentMethod,
        string idempotencyKey)
    {
        var checkout = CreateCheckoutService(db, accessor, clock, out var quotes);
        var quote = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest([new StorefrontQuoteLineRequest(variantId, quantity)], new StorefrontDestinationInput("NP", "Kathmandu", "KMC", "Baluwatar")));
        Assert.True(quote.IsSuccess);

        var sessionResult = await checkout.CreateAsync(new CreateCheckoutSessionRequest(quote.Value!.QuoteToken,
            new CheckoutCustomerInput("Ram Shrestha", "9812345678", "ram@example.com", false, true),
            new CheckoutAddressInput("Baluwatar Marg", null, "Kathmandu", "KMC", "Baluwatar", null), $"sess-{idempotencyKey}"));
        Assert.True(sessionResult.IsSuccess);

        var orderCreationService = CreateOrderCreationService(db, accessor, clock);
        var orderResult = await orderCreationService.CreateFromCheckoutAsync(new CreateOrderFromCheckoutRequest(sessionResult.Value!.Id, paymentMethod, idempotencyKey));
        Assert.True(orderResult.IsSuccess);

        return await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderResult.Value!.Id);
    }

    private static async Task<SeededStore> SeedStoreWithStockAsync(AppDbContext db, string tenantId, string slug, int openingStock, bool codAvailable)
    {
        var store = Store.Create(tenantId, new StoreSettings("Test Store", slug, null, StoreThemePreset.Default, null, "Kreyora", "test@example.com", null,
            null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var product = Product.Create(tenantId, "Test Product", null, $"{slug}-prod");
        var variant = product.AddVariant("SKU-TEST", "Standard", null, 1_000m, null, true);
        product.Publish();

        var inventory = InventoryItem.Create(tenantId, variant.Id);
        inventory.ApplyMovement(openingStock);

        var movement = StockMovement.Create(
            tenantId,
            inventory.Id,
            variant.Id,
            StockMovementType.OpeningBalance,
            openingStock,
            "Initial stock",
            "01J00000000000000000000001",
            $"open-{variant.Id}",
            "fp-open",
            null,
            null,
            CommerceActorKind.Member);

        db.AddRange(store, product, inventory, movement,
            StoreProductPublication.Create(tenantId, store.Id, product.Id, StoreProductVisibility.Visible),
            DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings("Kathmandu Delivery", 0, DeliveryFeeType.Flat, 100m, null, "1-2 days", codAvailable, true,
                [new DeliveryZoneInput("Kathmandu", "KMC", "Baluwatar")])));
        await db.SaveChangesAsync();

        return new SeededStore(store.Id, product.Id, variant.Id);
    }

    private static async Task<Order> CreateDirectOrderAsync(AppDbContext db, string tenantId, OrderPaymentMethod paymentMethod, string customerName, string customerPhone)
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
            customerName, customerPhone, "customer@example.com", "Baluwatar", null, "Kathmandu",
            "KMC", "Baluwatar", null, new string('b', 64), now.AddDays(30), 1500m, 0m, 100m,
            0m, 0m, 0m, 1600m, "NPR", rule.Id, rule.Name, "1-2 days", true, now));
        session.Complete(now);
        db.CheckoutSessions.Add(session);
        await db.SaveChangesAsync();

        var order = Order.Create(new OrderCreation(
            tenantId, store.Id, session.Id, null, paymentMethod,
            customerName, customerPhone, "customer@example.com", "Baluwatar", null, "Kathmandu",
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

    private sealed record SeededStore(string StoreId, string ProductId, string FirstVariantId);
    private sealed class Correlation(string correlationId) : ICorrelationContext { public string CorrelationId => correlationId; public void SetCorrelationId(string value) { } }
    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : Kreyora.Domain.Abstractions.ITimeProvider { public DateTimeOffset UtcNow { get; set; } = utcNow; }
}
