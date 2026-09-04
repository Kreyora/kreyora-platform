using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Inventory;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Customers;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Storefront;

public sealed class CheckoutSessionServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public CheckoutSessionServiceTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task CheckoutSession_RevalidatesReservesReplaysAndExpiresWithoutPersistingGuestContact()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "checkout-session");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var seeded = await SeedStoreAsync(db, tenant.Id, "checkout-session-store", 5, 4);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var checkout = CreateCheckoutService(db, accessor, clock, out var quotes);
        var quote = await quotes.CreateQuoteAsync(QuoteRequest(seeded.FirstVariantId, 3));

        Assert.True(quote.IsSuccess);
        var request = CheckoutRequest(quote.Value!.QuoteToken, "checkout-session-create", saveContact: false);
        var created = await checkout.CreateAsync(request);
        var replay = await checkout.CreateAsync(request);

        Assert.True(created.IsSuccess);
        Assert.False(created.Value!.WasReplayed);
        Assert.Null(created.Value.CustomerId);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.WasReplayed);
        Assert.Equal(created.Value.Id, replay.Value.Id);
        Assert.Equal(2, (await db.InventoryItems.SingleAsync(item => item.VariantId == seeded.FirstVariantId)).AvailableQuantity);
        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Single(await db.CheckoutSessionCommands.ToListAsync());
        Assert.DoesNotContain("Sushant", (await db.AuditEvents.SingleAsync(item => item.Action == "checkout-session.created")).Metadata ?? string.Empty);

        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        var expired = await checkout.ExpireDueSessionsAsync();

        Assert.Equal(1, expired);
        db.ChangeTracker.Clear();
        var session = await db.CheckoutSessions.SingleAsync();
        var reservation = await db.InventoryReservations.SingleAsync();
        var balance = await db.InventoryItems.SingleAsync(item => item.VariantId == seeded.FirstVariantId);
        Assert.Equal(CheckoutSessionState.Expired, session.State);
        Assert.Equal(InventoryReservationState.Expired, reservation.State);
        Assert.Equal(5, balance.AvailableQuantity);
    }

    [Fact]
    public async Task CheckoutSession_SavesCustomerOnlyWhenRequested_AndRejectsChangedQuotesWithoutPartialHolds()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "checkout-revalidate");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var seeded = await SeedStoreAsync(db, tenant.Id, "checkout-revalidate-store", 5, 1);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var checkout = CreateCheckoutService(db, accessor, clock, out var quotes);
        var pricedQuote = await quotes.CreateQuoteAsync(QuoteRequest(seeded.FirstVariantId, 1));
        seeded.Product.UpdateVariant(seeded.FirstVariantId, "CHECKOUT-ONE", "Standard", null, 1_250m, null, true);
        await db.SaveChangesAsync();

        var rejected = await checkout.CreateAsync(CheckoutRequest(pricedQuote.Value!.QuoteToken, "checkout-price-change", saveContact: true));

        Assert.True(rejected.IsFailure);
        Assert.Equal(409, rejected.Error!.Status);
        Assert.Empty(await db.InventoryReservations.ToListAsync());
        Assert.Empty(await db.CheckoutSessions.ToListAsync());
        Assert.Empty(await db.Customers.ToListAsync());

        var freshQuote = await quotes.CreateQuoteAsync(QuoteRequest(seeded.FirstVariantId, 1));
        var saved = await checkout.CreateAsync(CheckoutRequest(freshQuote.Value!.QuoteToken, "checkout-save-contact", saveContact: true));

        Assert.True(saved.IsSuccess);
        Assert.NotNull(saved.Value!.CustomerId);
        var customer = await db.Customers.SingleAsync();
        Assert.Equal("+9779812345678", customer.Phone);
        Assert.Equal("buyer@example.com", customer.Email);
    }

    [Fact]
    public async Task CheckoutInventoryBatch_DoesNotPersistPartialReservations_WhenAnyLineIsUnavailable()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "checkout-atomic");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var seeded = await SeedStoreAsync(db, tenant.Id, "checkout-atomic-store", 3, 0);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var inventory = CreateCheckoutInventoryService(db, accessor, clock);
        var result = await inventory.ReserveForCheckoutAsync(new CheckoutInventoryReservationRequest(
            "01J00000000000000000000003",
            [new CheckoutInventoryLine(seeded.FirstVariantId, 2), new CheckoutInventoryLine(seeded.SecondVariantId, 1)],
            clock.UtcNow.AddMinutes(10)));

        Assert.True(result.IsFailure);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.InventoryReservations.ToListAsync());
        Assert.Empty(await db.CheckoutSessions.ToListAsync());
        Assert.Equal(3, (await db.InventoryItems.SingleAsync(item => item.VariantId == seeded.FirstVariantId)).AvailableQuantity);
    }

    private static CheckoutSessionService CreateCheckoutService(
        Kreyora.Infrastructure.Persistence.AppDbContext db,
        TenantContextAccessor accessor,
        MutableTimeProvider clock,
        out StorefrontQuoteService quotes)
    {
        var protection = new EphemeralDataProtectionProvider();
        quotes = new StorefrontQuoteService(db, accessor, new StorefrontCatalogReadService(db), new StorefrontInventoryReadService(db), protection,
            Options.Create(new StorefrontQuoteOptions { LifetimeMinutes = 10 }), clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("checkout-session-integration-test"), authorizer);
        var inventory = CreateCheckoutInventoryService(db, accessor, clock, authorizer, audit);
        return new CheckoutSessionService(db, accessor, quotes, new CustomerCheckoutService(db, accessor), inventory, audit, clock,
            Options.Create(new CheckoutSessionOptions { LifetimeMinutes = 10, PiiReviewDays = 30, ExpiryBatchSize = 100 }));
    }

    private static InventoryService CreateCheckoutInventoryService(Kreyora.Infrastructure.Persistence.AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("checkout-inventory-integration-test"), authorizer);
        return CreateCheckoutInventoryService(db, accessor, clock, authorizer, audit);
    }

    private static InventoryService CreateCheckoutInventoryService(
        Kreyora.Infrastructure.Persistence.AppDbContext db,
        TenantContextAccessor accessor,
        MutableTimeProvider clock,
        TenantPermissionAuthorizer authorizer,
        AuditEventService audit) => new(db, accessor, authorizer, audit, clock, Options.Create(new InventoryReservationOptions()));

    private static StorefrontQuoteRequest QuoteRequest(string variantId, int quantity) => new([new StorefrontQuoteLineRequest(variantId, quantity)], Destination());

    private static StorefrontDestinationInput Destination() => new("NP", "Kathmandu", "KMC", "Thamel");

    private static CreateCheckoutSessionRequest CheckoutRequest(string quoteToken, string idempotencyKey, bool saveContact) => new(
        quoteToken,
        new CheckoutCustomerInput("Sushant Regmi", "9812345678", "Buyer@Example.com", saveContact, true),
        new CheckoutAddressInput("Thamel Marg", null, "Kathmandu", "KMC", "Thamel", null),
        idempotencyKey);

    private static async Task<SeededStore> SeedStoreAsync(Kreyora.Infrastructure.Persistence.AppDbContext db, string tenantId, string slug, int firstOnHand, int secondOnHand)
    {
        var store = Store.Create(tenantId, new StoreSettings("Kreyora Store", slug, null, StoreThemePreset.Default, null, "Kreyora", "hello@example.com", null,
            null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var product = Product.Create(tenantId, "Checkout Tee", null, $"{slug}-product");
        var first = product.AddVariant("CHECKOUT-ONE", "Standard", null, 1_000m, null, true);
        var second = product.AddVariant("CHECKOUT-TWO", "Large", null, 1_100m, null, true);
        product.Publish();
        var firstInventory = InventoryItem.Create(tenantId, first.Id);
        firstInventory.ApplyMovement(firstOnHand);
        var secondInventory = InventoryItem.Create(tenantId, second.Id);
        if (secondOnHand > 0) secondInventory.ApplyMovement(secondOnHand);
        db.AddRange(store, product, firstInventory, secondInventory,
            StoreProductPublication.Create(tenantId, store.Id, product.Id, StoreProductVisibility.Visible),
            DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings("Kathmandu delivery", 0, DeliveryFeeType.Flat, 150m, null, "1-2 business days", true, true,
                [new DeliveryZoneInput("Kathmandu", "KMC", "Thamel")])));
        await db.SaveChangesAsync();
        return new SeededStore(product, first.Id, second.Id);
    }

    private static async Task<Tenant> CreateTenantAsync(Kreyora.Infrastructure.Persistence.AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private sealed record SeededStore(Product Product, string FirstVariantId, string SecondVariantId);

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : Kreyora.Domain.Abstractions.ITimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
