using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Inventory;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Inventory;

public class InventoryServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public InventoryServiceTests(PostgresFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Adjustment_PersistsLedgerBalanceAudit_AndIdempotentReplay()
    {
        var accessor = new TenantContextAccessor();
        await using var dbContext = fixture.CreateDbContext(accessor);
        await dbContext.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(dbContext, "inventory-ledger");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var variantId = await CreateVariantAsync(dbContext, tenant.Id, "ledger-tee");
        var service = CreateService(dbContext, accessor);
        var request = new StockAdjustmentRequest(
            variantId,
            StockMovementType.OpeningBalance,
            12,
            "Opening count verified",
            $"inventory-{Guid.NewGuid():N}");

        var first = await service.AdjustStockAsync(request);
        var replay = await service.AdjustStockAsync(request);
        var keyConflict = await service.AdjustStockAsync(request with { Quantity = 13 });
        var secondOpeningBalance = await service.AdjustStockAsync(new StockAdjustmentRequest(
            variantId,
            StockMovementType.OpeningBalance,
            1,
            "Incorrect duplicate opening count",
            $"duplicate-opening-{Guid.NewGuid():N}"));
        var reconciliation = await service.ReconcileInventoryAsync(variantId);

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.False(first.Value!.WasReplayed);
        Assert.True(replay.Value!.WasReplayed);
        Assert.True(keyConflict.IsFailure);
        Assert.Equal(409, keyConflict.Error!.Status);
        Assert.True(secondOpeningBalance.IsFailure);
        Assert.Equal(400, secondOpeningBalance.Error!.Status);
        Assert.Equal(12, first.Value.Balance.OnHandQuantity);
        Assert.Equal(first.Value.Movement.Id, replay.Value.Movement.Id);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
        Assert.Single(await dbContext.StockMovements.ToListAsync());
        Assert.Single(await dbContext.AuditEvents.Where(item => item.Action == "inventory.stock.adjusted").ToListAsync());
    }

    [Fact]
    public async Task TenantBoundary_AndNegativeBalanceProtection_AreEnforced()
    {
        var accessor = new TenantContextAccessor();
        await using var dbContext = fixture.CreateDbContext(accessor);
        await dbContext.Database.MigrateAsync();
        var firstTenant = await CreateTenantAsync(dbContext, "inventory-first");
        var secondTenant = await CreateTenantAsync(dbContext, "inventory-second");
        string firstVariantId;
        string secondVariantId;
        using (accessor.BeginScope(OwnerContext(firstTenant.Id)))
        {
            firstVariantId = await CreateVariantAsync(dbContext, firstTenant.Id, "first-tee");
        }

        using (accessor.BeginScope(OwnerContext(secondTenant.Id)))
        {
            secondVariantId = await CreateVariantAsync(dbContext, secondTenant.Id, "second-tee");
        }

        var service = CreateService(dbContext, accessor);

        using (accessor.BeginScope(OwnerContext(firstTenant.Id)))
        {
            var opening = await service.AdjustStockAsync(new StockAdjustmentRequest(
                firstVariantId, StockMovementType.Receipt, 2, "Supplier receipt", $"first-{Guid.NewGuid():N}"));
            Assert.True(opening.IsSuccess);

            var negative = await service.AdjustStockAsync(new StockAdjustmentRequest(
                firstVariantId, StockMovementType.Damage, 3, "Damaged stock", $"negative-{Guid.NewGuid():N}"));
            Assert.True(negative.IsFailure);
            Assert.Equal(400, negative.Error!.Status);
        }

        using (accessor.BeginScope(OwnerContext(secondTenant.Id)))
        {
            var foreign = await service.GetInventoryAsync(firstVariantId);
            Assert.True(foreign.IsFailure);
            Assert.Equal(404, foreign.Error!.Status);

            var own = await service.AdjustStockAsync(new StockAdjustmentRequest(
                secondVariantId, StockMovementType.Receipt, 2, "Supplier receipt", $"second-{Guid.NewGuid():N}"));
            Assert.True(own.IsSuccess);
        }
    }

    [Fact]
    public async Task StockMovement_IsAppendOnly()
    {
        var accessor = new TenantContextAccessor();
        await using var dbContext = fixture.CreateDbContext(accessor);
        await dbContext.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(dbContext, "inventory-append-only");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var variantId = await CreateVariantAsync(dbContext, tenant.Id, "append-only-tee");
        var service = CreateService(dbContext, accessor);
        var adjustment = await service.AdjustStockAsync(new StockAdjustmentRequest(
            variantId, StockMovementType.Receipt, 3, "Supplier receipt", $"append-{Guid.NewGuid():N}"));
        Assert.True(adjustment.IsSuccess);

        var movement = await dbContext.StockMovements.SingleAsync();
        dbContext.Entry(movement).Property(nameof(StockMovement.Reason)).CurrentValue = "Tampered";

        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    private static InventoryService CreateService(Kreyora.Infrastructure.Persistence.AppDbContext dbContext, TenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(dbContext, accessor, new Correlation("inventory-integration-test"), authorizer);
        return new InventoryService(dbContext, accessor, authorizer, audit);
    }

    private static async Task<Tenant> CreateTenantAsync(Kreyora.Infrastructure.Persistence.AppDbContext dbContext, string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{suffix}");
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant;
    }

    private static async Task<string> CreateVariantAsync(Kreyora.Infrastructure.Persistence.AppDbContext dbContext, string tenantId, string slug)
    {
        var product = Product.Create(tenantId, $"{slug} product", null, slug);
        var variant = product.AddVariant($"SKU-{Guid.NewGuid():N}"[..20], "Default", null, 1_000m, null, true);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();
        return variant.Id;
    }

    private static TenantContext OwnerContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
