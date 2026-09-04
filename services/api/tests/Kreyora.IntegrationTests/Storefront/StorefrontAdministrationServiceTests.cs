using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Storefront;

public sealed class StorefrontAdministrationServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public StorefrontAdministrationServiceTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task CreateStore_ReplaysIdempotently_AndReadinessListsDeferredDependencies()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "store-create");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var service = CreateService(db, accessor);
        var request = NewCreateRequest("namaste-store", $"store-{Guid.NewGuid():N}");

        var created = await service.CreateStoreAsync(request);
        var replay = await service.CreateStoreAsync(request);
        var readiness = await service.GetReadinessAsync();

        Assert.True(created.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(created.Value!.Id, replay.Value!.Id);
        Assert.Equal(1, await db.Stores.CountAsync());
        Assert.Single(await db.StoreCommandIdempotencyRecords.ToListAsync());
        Assert.Contains(readiness.Value!.Blockers, blocker => blocker.Code == "delivery_not_configured");
        Assert.Contains(readiness.Value.Blockers, blocker => blocker.Code == "payment_not_configured");
        Assert.Single(await db.AuditEvents.Where(item => item.Action == "store.created").ToListAsync());
    }

    [Fact]
    public async Task PlatformSlug_IsGlobalButTenantRecordsRemainIsolated()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var first = await CreateTenantAsync(db, "store-first");
        var second = await CreateTenantAsync(db, "store-second");
        var service = CreateService(db, accessor);

        using (accessor.BeginScope(OwnerContext(first.Id)))
        {
            Assert.True((await service.CreateStoreAsync(NewCreateRequest("shared-store", $"first-{Guid.NewGuid():N}"))).IsSuccess);
        }

        using (accessor.BeginScope(OwnerContext(second.Id)))
        {
            var duplicate = await service.CreateStoreAsync(NewCreateRequest("shared-store", $"second-{Guid.NewGuid():N}"));
            var current = await service.GetStoreAsync();
            Assert.True(duplicate.IsFailure);
            Assert.Equal(409, duplicate.Error!.Status);
            Assert.True(current.IsFailure);
            Assert.Equal(404, current.Error!.Status);
        }
    }

    [Fact]
    public async Task Database_AllowsOnlyOneActiveStorePerTenant()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "store-active");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var first = Store.Create(tenant.Id, ToDomainSettings("first-active-store"));
        first.Activate(DateTimeOffset.UtcNow);
        db.Stores.Add(first);
        await db.SaveChangesAsync();

        var second = Store.Create(tenant.Id, ToDomainSettings("second-active-store"));
        second.Activate(DateTimeOffset.UtcNow);
        db.Stores.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Publication_UsesCanonicalCatalogEligibility_AndNeverActivatesBeforeDeliveryAndPayments()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "store-publication");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var service = CreateService(db, accessor);
        var store = await service.CreateStoreAsync(NewCreateRequest("publication-store", $"store-{Guid.NewGuid():N}"));
        var product = Product.Create(tenant.Id, "Tee", null, "store-tee");
        product.AddVariant("STORE-TEE", "Standard", null, 1_000m, null, true);
        product.Publish();
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var publication = await service.SetProductVisibilityAsync(new SetStoreProductVisibilityRequest(product.Id, StoreProductVisibility.Visible, 0, $"publication-{Guid.NewGuid():N}"));
        var readiness = await service.GetReadinessAsync();
        var activation = await service.ActivateStoreAsync(new ActivateStoreRequest(store.Value!.Version, $"activate-{Guid.NewGuid():N}"));

        Assert.True(publication.IsSuccess);
        Assert.True(readiness.IsSuccess);
        Assert.True(readiness.Value!.Sections.Single(section => section.Name == "catalog").IsReady);
        Assert.False(activation.IsSuccess);
        Assert.Equal(400, activation.Error!.Status);
        Assert.Contains("delivery_not_configured", activation.Error.Detail, StringComparison.Ordinal);
        Assert.Equal(StoreStatus.Draft, (await db.Stores.SingleAsync()).Status);
    }

    private static StorefrontAdministrationService CreateService(Kreyora.Infrastructure.Persistence.AppDbContext db, TenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("storefront-integration-test"), authorizer);
        var deliveryRules = new DeliveryRuleService(db, accessor, authorizer, audit);
        return new StorefrontAdministrationService(db, accessor, authorizer, audit, new StorefrontCatalogReadService(db), deliveryRules);
    }

    private static CreateStoreRequest NewCreateRequest(string slug, string key) => new(new StoreSettingsInput(
        "Namaste Crafts", slug, "Handcrafted with care", StoreThemePreset.Default, "#A1B2C3", "Namaste Crafts",
        "hello@example.com", "+9779800000000", null, null, null, null, "Terms", "Privacy", "Returns", "Payment"), key);

    private static StoreSettings ToDomainSettings(string slug) => new(
        "Namaste Crafts", slug, null, StoreThemePreset.Default, null, "Namaste Crafts", "hello@example.com", "+9779800000000",
        null, null, null, null, "Terms", "Privacy", "Returns", "Payment");

    private static async Task<Tenant> CreateTenantAsync(Kreyora.Infrastructure.Persistence.AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
