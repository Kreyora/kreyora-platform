using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Storefront;

public sealed class DeliveryRuleAndQuoteServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public DeliveryRuleAndQuoteServiceTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task DeliveryRules_ReplayIdempotently_AndRemainTenantScoped()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var firstTenant = await CreateTenantAsync(db, "delivery-first");
        var secondTenant = await CreateTenantAsync(db, "delivery-second");
        var service = CreateDeliveryRuleService(db, accessor);
        DeliveryRuleItem created;

        using (accessor.BeginScope(OwnerContext(firstTenant.Id)))
        {
            db.Stores.Add(CreateStore(firstTenant.Id, "delivery-first-store"));
            await db.SaveChangesAsync();
            var request = new CreateDeliveryRuleRequest(NewRule("Kathmandu", null, null, 0, 150m), $"delivery-{Guid.NewGuid():N}");

            var first = await service.CreateAsync(request);
            var replay = await service.CreateAsync(request);

            Assert.True(first.IsSuccess);
            Assert.True(replay.IsSuccess);
            Assert.Equal(first.Value!.Id, replay.Value!.Id);
            Assert.Single(await db.DeliveryRules.ToListAsync());
            Assert.Single(await db.StoreCommandIdempotencyRecords.Where(record => record.Operation == "delivery-rule.create").ToListAsync());
            Assert.True(await service.HasActiveRulesAsync((await db.Stores.SingleAsync()).Id));
            var readiness = await CreateStorefrontService(db, accessor, service).GetReadinessAsync();
            Assert.True(readiness.IsSuccess);
            Assert.DoesNotContain(readiness.Value!.Blockers, blocker => blocker.Code == "delivery_not_configured");
            Assert.Contains(readiness.Value.Blockers, blocker => blocker.Code == "payment_not_configured");
            created = first.Value;
        }

        using (accessor.BeginScope(OwnerContext(secondTenant.Id)))
        {
            db.Stores.Add(CreateStore(secondTenant.Id, "delivery-second-store"));
            await db.SaveChangesAsync();

            var foreign = await service.GetAsync(created.Id);

            Assert.True(foreign.IsFailure);
            Assert.Equal(404, foreign.Error!.Status);
        }
    }

    [Fact]
    public async Task Quote_UsesCurrentCatalogInventoryAndMostSpecificNepalZone()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "quote-flow");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var store = CreateStore(tenant.Id, "quote-flow-store");
        var product = Product.Create(tenant.Id, "Himalayan Tee", null, "himalayan-tee");
        var variant = product.AddVariant("QUOTE-TEE", "Standard", null, 1_000m, null, true);
        product.Publish();
        var inventory = InventoryItem.Create(tenant.Id, variant.Id);
        inventory.ApplyMovement(6);
        db.AddRange(
            store,
            product,
            inventory,
            StoreProductPublication.Create(tenant.Id, store.Id, product.Id, StoreProductVisibility.Visible),
            DeliveryRule.Create(tenant.Id, store.Id, ToSettings(NewRule("Kathmandu", null, null, 0, 150m))),
            DeliveryRule.Create(tenant.Id, store.Id, ToSettings(NewRule("Kathmandu", "KMC", "Thamel", 9_999, 50m, DeliveryFeeType.Threshold, 3_000m))));
        await db.SaveChangesAsync();
        var quotes = CreateQuoteService(db, accessor);
        var request = new StorefrontQuoteRequest(
            [new StorefrontQuoteLineRequest(variant.Id, 3)],
            new StorefrontDestinationInput("np", " kathmandu ", "kmc", "thamel"));

        var quote = await quotes.CreateQuoteAsync(request);
        var replayed = await quotes.ReadQuoteAsync(quote.Value?.QuoteToken ?? string.Empty);
        var tampered = await quotes.ReadQuoteAsync($"{quote.Value?.QuoteToken}tampered");

        Assert.True(quote.IsSuccess);
        Assert.Equal(3_000m, quote.Value!.Totals.MerchandiseSubtotalNpr);
        Assert.Equal(0m, quote.Value.Totals.DeliveryFeeNpr);
        Assert.Equal(3_000m, quote.Value.Totals.TotalNpr);
        Assert.Equal(0m, quote.Value.Delivery.FeeNpr);
        Assert.Equal("1-2 business days", quote.Value.Delivery.EstimatedEtaText);
        Assert.True(replayed.IsSuccess);
        Assert.True(tampered.IsFailure);
        Assert.Equal(400, tampered.Error!.Status);

        product.UpdateVariant(variant.Id, "QUOTE-TEE", "Standard", null, 1_200m, null, true);
        await db.SaveChangesAsync();
        var repriced = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest(
            [new StorefrontQuoteLineRequest(variant.Id, 1)],
            new StorefrontDestinationInput("NP", "Kathmandu", "KMC", "Thamel")));
        var outOfStock = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest(
            [new StorefrontQuoteLineRequest(variant.Id, 7)],
            new StorefrontDestinationInput("NP", "Kathmandu", null, null)));

        Assert.True(repriced.IsSuccess);
        Assert.Equal(1_200m, repriced.Value!.Lines.Single().UnitPriceNpr);
        Assert.True(outOfStock.IsFailure);
        Assert.Equal(400, outOfStock.Error!.Status);
    }

    private static DeliveryRuleService CreateDeliveryRuleService(Kreyora.Infrastructure.Persistence.AppDbContext db, TenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("delivery-rule-integration-test"), authorizer);
        return new DeliveryRuleService(db, accessor, authorizer, audit);
    }

    private static StorefrontAdministrationService CreateStorefrontService(
        Kreyora.Infrastructure.Persistence.AppDbContext db,
        TenantContextAccessor accessor,
        IDeliveryRuleReadService deliveryRules)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("storefront-readiness-integration-test"), authorizer);
        return new StorefrontAdministrationService(db, accessor, authorizer, audit, new StorefrontCatalogReadService(db), deliveryRules);
    }

    private static StorefrontQuoteService CreateQuoteService(Kreyora.Infrastructure.Persistence.AppDbContext db, TenantContextAccessor accessor) => new(
        db,
        accessor,
        new StorefrontCatalogReadService(db),
        new StorefrontInventoryReadService(db),
        new EphemeralDataProtectionProvider(),
        Options.Create(new StorefrontQuoteOptions { LifetimeMinutes = 10 }),
        new TestTimeProvider());

    private static DeliveryRuleInput NewRule(
        string district,
        string? municipality,
        string? locality,
        int priority,
        decimal fee,
        DeliveryFeeType feeType = DeliveryFeeType.Flat,
        decimal? freeAboveNpr = null) => new(
        "Kathmandu delivery",
        priority,
        feeType,
        fee,
        freeAboveNpr,
        "1-2 business days",
        true,
        true,
        [new DeliveryZoneInput(district, municipality, locality)]);

    private static DeliveryRuleSettings ToSettings(DeliveryRuleInput input) => new(
        input.Name,
        input.Priority,
        input.FeeType,
        input.BaseFeeNpr,
        input.FreeAboveNpr,
        input.EstimatedEtaText,
        input.CodAvailable,
        input.IsActive,
        input.Zones);

    private static Store CreateStore(string tenantId, string slug) => Store.Create(tenantId, new StoreSettings(
        "Kreyora Store", slug, null, StoreThemePreset.Default, null, "Kreyora", "hello@example.com", null,
        null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));

    private static async Task<Tenant> CreateTenantAsync(Kreyora.Infrastructure.Persistence.AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }

    private sealed class TestTimeProvider : Kreyora.Domain.Abstractions.ITimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
