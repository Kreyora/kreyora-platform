using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Payments;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Payments;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Payments;

public sealed class StorePaymentConfigurationServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public StorePaymentConfigurationServiceTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task GetConfiguration_ReturnsDefault_WhenNotYetConfigured()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "store-pay-config-default");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var store = await CreateStoreAsync(db, tenant.Id, "store-config-1");
        var service = CreateService(db, accessor);

        var result = await service.GetConfigurationAsync(store.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CodEnabled);
        Assert.True(result.Value.MerchantQrEnabled);
        Assert.Null(result.Value.MerchantQrInstructions);
    }

    [Fact]
    public async Task UpdateConfiguration_PersistsSettings_AndEmitsAudit()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "store-pay-config-update");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var store = await CreateStoreAsync(db, tenant.Id, "store-config-2");
        var service = CreateService(db, accessor);

        var updateResult = await service.UpdateConfigurationAsync(new UpdateStorePaymentConfigurationRequest(
            store.Id,
            CodEnabled: false,
            MerchantQrEnabled: true,
            MerchantQrInstructions: "Pay to eSewa 9841000000"));

        Assert.True(updateResult.IsSuccess);
        Assert.False(updateResult.Value!.CodEnabled);
        Assert.True(updateResult.Value.MerchantQrEnabled);
        Assert.Equal("Pay to eSewa 9841000000", updateResult.Value.MerchantQrInstructions);

        var getResult = await service.GetConfigurationAsync(store.Id);
        Assert.True(getResult.IsSuccess);
        Assert.False(getResult.Value!.CodEnabled);
        Assert.True(getResult.Value.MerchantQrEnabled);

        var audits = await db.AuditEvents.Where(a => a.TargetId == updateResult.Value.Id).ToListAsync();
        Assert.Contains(audits, a => a.Action == "store.payment_configuration.updated");
    }

    [Fact]
    public async Task UpdateConfiguration_Fails_WhenDisablingBothMethods()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "store-pay-config-invalid");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var store = await CreateStoreAsync(db, tenant.Id, "store-config-3");
        var service = CreateService(db, accessor);

        var result = await service.UpdateConfigurationAsync(new UpdateStorePaymentConfigurationRequest(
            store.Id,
            CodEnabled: false,
            MerchantQrEnabled: false));

        Assert.True(result.IsFailure);
        Assert.Contains("At least one payment method", result.Error!.Detail);
    }

    [Fact]
    public async Task CrossTenant_CannotAccessOtherTenantsStoreConfiguration()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "store-pay-tenant-a");
        var tenantB = await CreateTenantAsync(db, "store-pay-tenant-b");

        Store storeA;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            storeA = await CreateStoreAsync(db, tenantA.Id, "store-a");
        }

        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var serviceB = CreateService(db, accessor);
            var result = await serviceB.GetConfigurationAsync(storeA.Id);
            Assert.True(result.IsFailure);
            Assert.Equal(404, result.Error!.Status);
        }
    }

    private static StorePaymentConfigurationService CreateService(AppDbContext db, TenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("store-config-test"), authorizer);
        return new StorePaymentConfigurationService(db, accessor, authorizer, audit);
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

    private static async Task<Store> CreateStoreAsync(AppDbContext db, string tenantId, string prefix)
    {
        var store = Store.Create(tenantId, new StoreSettings(
            "Test Store",
            $"{prefix}-{Guid.NewGuid():N}"[..20],
            null,
            StoreThemePreset.Default,
            null,
            "Owner",
            "owner@example.com",
            null, null, null, null, null,
            "Terms", "Privacy", "Returns", "Payment"));
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}

