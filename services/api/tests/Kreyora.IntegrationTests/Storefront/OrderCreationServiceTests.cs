using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Inventory;
using Kreyora.Application.Orders;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Customers;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Storefront;

public sealed class OrderCreationServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public OrderCreationServiceTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task OrderCreation_CommitsSessionReservationsSnapshotsFactsAndReplaysIdempotently()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-create");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var seeded = await SeedStoreAsync(db, tenant.Id, "order-create-store", true);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var sessionId = await CreateSessionAsync(db, accessor, clock, seeded.FirstVariantId, "order-session", saveContact: true);
        var service = CreateOrderService(db, accessor, clock);
        var request = new CreateOrderFromCheckoutRequest(sessionId, OrderPaymentMethod.CashOnDelivery, "order-create");

        var created = await service.CreateFromCheckoutAsync(request);
        var replay = await service.CreateFromCheckoutAsync(request);
        var changedKey = await service.CreateFromCheckoutAsync(request with { IdempotencyKey = "order-create-different" });

        Assert.True(created.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.WasReplayed);
        Assert.Equal(created.Value!.Id, replay.Value.Id);
        Assert.True(changedKey.IsFailure);
        Assert.Equal(409, changedKey.Error!.Status);
        Assert.Equal(OrderStatus.PendingConfirmation, created.Value.Status);
        Assert.Equal(PaymentStatus.Pending, created.Value.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Unfulfilled, created.Value.FulfilmentStatus);

        db.ChangeTracker.Clear();
        var order = await db.Orders.Include(item => item.Items).SingleAsync();
        var session = await db.CheckoutSessions.SingleAsync();
        var reservation = await db.InventoryReservations.SingleAsync();
        var balance = await db.InventoryItems.SingleAsync(item => item.VariantId == seeded.FirstVariantId);
        var movement = await db.StockMovements.SingleAsync(item => item.ReferenceId == order.Id);
        var audit = await db.AuditEvents.SingleAsync(item => item.Action == "order.created");
        var outbox = await db.OutboxMessages.SingleAsync(item => item.Type == "order.created.v1");

        Assert.Equal(CheckoutSessionState.Completed, session.State);
        Assert.Equal(InventoryReservationState.Committed, reservation.State);
        Assert.Equal(2, balance.OnHandQuantity);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(CommerceActorKind.CommerceSystem, movement.ActorKind);
        Assert.Null(movement.ActorUserId);
        Assert.Equal(CommerceActorKind.CommerceSystem, audit.ActorKind);
        Assert.Null(audit.ActorUserId);
        Assert.DoesNotContain("Sushant", audit.Metadata ?? string.Empty);
        Assert.DoesNotContain("9812345678", outbox.Content);
        Assert.Equal(1_000m, order.Items.Single().UnitPriceNpr);
        Assert.Equal("Checkout Tee", order.Items.Single().ProductTitle);

        seeded.Product.UpdateDetails("Changed Catalog Title", null, "changed-catalog-title");
        seeded.Product.UpdateVariant(seeded.FirstVariantId, "ORDER-ONE", "Changed Variant", null, 1_900m, null, true);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var immutable = await db.Orders.Include(item => item.Items).SingleAsync();
        Assert.Equal("Checkout Tee", immutable.Items.Single().ProductTitle);
        Assert.Equal("Standard", immutable.Items.Single().VariantName);
        Assert.Equal(1_000m, immutable.Items.Single().UnitPriceNpr);
    }

    [Fact]
    public async Task OrderCreation_InitializesQrStateAndRejectsCodWhenTheSessionDisallowsIt()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "order-payment");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var qrStore = await SeedStoreAsync(db, tenant.Id, "order-qr-store", false);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var sessionId = await CreateSessionAsync(db, accessor, clock, qrStore.FirstVariantId, "order-qr-session", saveContact: false);
        var service = CreateOrderService(db, accessor, clock);

        var cod = await service.CreateFromCheckoutAsync(new CreateOrderFromCheckoutRequest(sessionId, OrderPaymentMethod.CashOnDelivery, "order-cod-denied"));
        var qr = await service.CreateFromCheckoutAsync(new CreateOrderFromCheckoutRequest(sessionId, OrderPaymentMethod.MerchantQr, "order-qr"));

        Assert.True(cod.IsFailure);
        Assert.Equal(400, cod.Error!.Status);
        Assert.True(qr.IsSuccess);
        Assert.Equal(PaymentStatus.AwaitingVerification, qr.Value!.PaymentStatus);
        Assert.Equal(OrderPaymentMethod.MerchantQr, qr.Value.PaymentMethod);
    }

    [Fact]
    public async Task ConcurrentOrderCreation_ForOneSessionCommitsStockAndCreatesOnlyOneOrder()
    {
        var setupAccessor = new TenantContextAccessor();
        await using var setupDb = fixture.CreateDbContext(setupAccessor);
        await setupDb.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(setupDb, "order-race");
        using (setupAccessor.BeginScope(OwnerContext(tenant.Id)))
        {
            var seeded = await SeedStoreAsync(setupDb, tenant.Id, "order-race-store", true);
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var sessionId = await CreateSessionAsync(setupDb, setupAccessor, clock, seeded.FirstVariantId, "order-race-session", saveContact: false);

            var results = await Task.WhenAll(
                CreateOrderInIndependentContextAsync(tenant.Id, sessionId, "order-race-one"),
                CreateOrderInIndependentContextAsync(tenant.Id, sessionId, "order-race-two"));

            Assert.Single(results, result => result.IsSuccess);
            setupDb.ChangeTracker.Clear();
            Assert.Single(await setupDb.Orders.ToListAsync());
            Assert.Single(await setupDb.StockMovements.Where(item => item.Type == StockMovementType.ReservationCommitted).ToListAsync());
            var balance = await setupDb.InventoryItems.SingleAsync(item => item.VariantId == seeded.FirstVariantId);
            Assert.Equal(2, balance.OnHandQuantity);
            Assert.Equal(0, balance.ReservedQuantity);
        }
    }

    [Fact]
    public async Task OrderCreation_RejectsForeignAndExpiredSessionsWithoutCommittingStock()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var owner = await CreateTenantAsync(db, "order-negative-owner");
        var otherTenant = await CreateTenantAsync(db, "order-negative-other");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        string sessionId;
        string variantId;

        using (accessor.BeginScope(OwnerContext(owner.Id)))
        {
            var seeded = await SeedStoreAsync(db, owner.Id, "order-negative-store", true);
            variantId = seeded.FirstVariantId;
            sessionId = await CreateSessionAsync(db, accessor, clock, variantId, "order-negative-session", saveContact: false);
        }

        using (accessor.BeginScope(OwnerContext(otherTenant.Id)))
        {
            var foreign = await CreateOrderService(db, accessor, clock).CreateFromCheckoutAsync(
                new CreateOrderFromCheckoutRequest(sessionId, OrderPaymentMethod.CashOnDelivery, "order-foreign"));
            Assert.True(foreign.IsFailure);
            Assert.Equal(404, foreign.Error!.Status);
        }

        clock.UtcNow = clock.UtcNow.AddHours(1);
        using (accessor.BeginScope(OwnerContext(owner.Id)))
        {
            var expired = await CreateOrderService(db, accessor, clock).CreateFromCheckoutAsync(
                new CreateOrderFromCheckoutRequest(sessionId, OrderPaymentMethod.CashOnDelivery, "order-expired"));
            Assert.True(expired.IsFailure);
            Assert.Equal(409, expired.Error!.Status);
        }

        using (accessor.BeginScope(OwnerContext(owner.Id)))
        {
            db.ChangeTracker.Clear();
            Assert.Empty(await db.Orders.ToListAsync());
            Assert.Empty(await db.StockMovements.Where(item => item.Type == StockMovementType.ReservationCommitted).ToListAsync());
            var balance = await db.InventoryItems.SingleAsync(item => item.VariantId == variantId);
            var reservation = await db.InventoryReservations.SingleAsync();
            Assert.Equal(5, balance.OnHandQuantity);
            Assert.Equal(3, balance.ReservedQuantity);
            Assert.Equal(InventoryReservationState.Active, reservation.State);
        }
    }

    private async Task<Kreyora.Application.Models.Result<OrderCreationResult>> CreateOrderInIndependentContextAsync(string tenantId, string sessionId, string key)
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        using var scope = accessor.BeginScope(OwnerContext(tenantId));
        var service = CreateOrderService(db, accessor, new MutableTimeProvider(DateTimeOffset.UtcNow));
        return await service.CreateFromCheckoutAsync(new CreateOrderFromCheckoutRequest(sessionId, OrderPaymentMethod.CashOnDelivery, key));
    }

    private static async Task<string> CreateSessionAsync(Kreyora.Infrastructure.Persistence.AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock, string variantId, string key, bool saveContact)
    {
        var checkout = CreateCheckoutService(db, accessor, clock, out var quotes);
        var quote = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest([new StorefrontQuoteLineRequest(variantId, 3)], Destination()));
        var session = await checkout.CreateAsync(new CreateCheckoutSessionRequest(quote.Value!.QuoteToken,
            new CheckoutCustomerInput("Sushant Regmi", "9812345678", "Buyer@Example.com", saveContact, true),
            new CheckoutAddressInput("Thamel Marg", null, "Kathmandu", "KMC", "Thamel", null), key));
        Assert.True(session.IsSuccess);
        return session.Value!.Id;
    }

    private static OrderCreationService CreateOrderService(Kreyora.Infrastructure.Persistence.AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("order-creation-integration-test"), authorizer);
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, Options.Create(new InventoryReservationOptions()));
        return new OrderCreationService(db, accessor, inventory, audit, clock);
    }

    private static CheckoutSessionService CreateCheckoutService(Kreyora.Infrastructure.Persistence.AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock, out StorefrontQuoteService quotes)
    {
        quotes = new StorefrontQuoteService(db, accessor, new StorefrontCatalogReadService(db), new StorefrontInventoryReadService(db), new EphemeralDataProtectionProvider(),
            Options.Create(new StorefrontQuoteOptions { LifetimeMinutes = 10 }), clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("order-checkout-integration-test"), authorizer);
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, Options.Create(new InventoryReservationOptions()));
        return new CheckoutSessionService(db, accessor, quotes, new CustomerCheckoutService(db, accessor), inventory, audit, clock,
            Options.Create(new CheckoutSessionOptions { LifetimeMinutes = 10, PiiReviewDays = 30, ExpiryBatchSize = 100 }));
    }

    private static StorefrontDestinationInput Destination() => new("NP", "Kathmandu", "KMC", "Thamel");

    private static async Task<SeededStore> SeedStoreAsync(Kreyora.Infrastructure.Persistence.AppDbContext db, string tenantId, string slug, bool codAvailable)
    {
        var store = Store.Create(tenantId, new StoreSettings("Kreyora Store", slug, null, StoreThemePreset.Default, null, "Kreyora", "hello@example.com", null,
            null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var product = Product.Create(tenantId, "Checkout Tee", null, $"{slug}-product");
        var first = product.AddVariant("ORDER-ONE", "Standard", null, 1_000m, null, true);
        product.Publish();
        var inventory = InventoryItem.Create(tenantId, first.Id);
        inventory.ApplyMovement(5);
        db.AddRange(store, product, inventory,
            StoreProductPublication.Create(tenantId, store.Id, product.Id, StoreProductVisibility.Visible),
            DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings("Kathmandu delivery", 0, DeliveryFeeType.Flat, 150m, null, "1-2 business days", codAvailable, true,
                [new DeliveryZoneInput("Kathmandu", "KMC", "Thamel")])));
        await db.SaveChangesAsync();
        return new SeededStore(product, first.Id);
    }

    private static async Task<Tenant> CreateTenantAsync(Kreyora.Infrastructure.Persistence.AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);
    private sealed record SeededStore(Product Product, string FirstVariantId);
    private sealed class Correlation(string correlationId) : ICorrelationContext { public string CorrelationId => correlationId; public void SetCorrelationId(string value) { } }
    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : Kreyora.Domain.Abstractions.ITimeProvider { public DateTimeOffset UtcNow { get; set; } = utcNow; }
}
