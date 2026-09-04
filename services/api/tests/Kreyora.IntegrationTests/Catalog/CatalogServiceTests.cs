using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Catalog;

public class CatalogServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public CatalogServiceTests(PostgresFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task CreateProduct_PersistsAudits_AndReplaysTheSameIdempotentRequest()
    {
        var accessor = new TenantContextAccessor();
        await using var dbContext = fixture.CreateDbContext(accessor);
        await dbContext.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(dbContext, "catalog-create");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var service = CreateService(dbContext, accessor);
        var request = NewCreateRequest("classic-tee", "TEE-CLASSIC-S", $"create-{Guid.NewGuid():N}");

        var created = await service.CreateProductAsync(request);
        var replayed = await service.CreateProductAsync(request);

        Assert.True(created.IsSuccess);
        Assert.True(replayed.IsSuccess);
        Assert.Equal(created.Value!.Id, replayed.Value!.Id);
        Assert.Equal("classic-tee", created.Value.Slug);
        Assert.Single(created.Value.Variants);
        Assert.Equal(1, await dbContext.Products.CountAsync());
        Assert.Equal(1, await dbContext.CatalogCommandIdempotencyRecords.CountAsync());
        Assert.Single(await dbContext.AuditEvents.Where(item => item.Action == "catalog.product.created").ToListAsync());
    }

    [Fact]
    public async Task TenantBoundary_HidesProducts_AndAllowsTenantScopedSlugAndSku()
    {
        var accessor = new TenantContextAccessor();
        await using var dbContext = fixture.CreateDbContext(accessor);
        await dbContext.Database.MigrateAsync();
        var firstTenant = await CreateTenantAsync(dbContext, "catalog-first");
        var secondTenant = await CreateTenantAsync(dbContext, "catalog-second");
        var service = CreateService(dbContext, accessor);

        using (accessor.BeginScope(OwnerContext(firstTenant.Id)))
        {
            var first = await service.CreateProductAsync(NewCreateRequest("same-product", "SAME-SKU", $"first-{Guid.NewGuid():N}"));
            Assert.True(first.IsSuccess);
        }

        using (accessor.BeginScope(OwnerContext(secondTenant.Id)))
        {
            var visibleBeforeCreate = await service.ListProductsAsync();
            Assert.True(visibleBeforeCreate.IsSuccess);
            Assert.Empty(visibleBeforeCreate.Value!);

            var second = await service.CreateProductAsync(NewCreateRequest("same-product", "SAME-SKU", $"second-{Guid.NewGuid():N}"));
            Assert.True(second.IsSuccess);
            Assert.Equal(secondTenant.Id, second.Value!.TenantId);
        }
    }

    [Fact]
    public async Task ListProducts_FiltersBySearchAndUsesAnOpaqueCursor()
    {
        var accessor = new TenantContextAccessor();
        await using var dbContext = fixture.CreateDbContext(accessor);
        await dbContext.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(dbContext, "catalog-query");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));
        var service = CreateService(dbContext, accessor);

        Assert.True((await service.CreateProductAsync(NewCreateRequest("linen-shirt", "LINEN-SHIRT", $"query-{Guid.NewGuid():N}"))).IsSuccess);
        Assert.True((await service.CreateProductAsync(NewCreateRequest("wool-scarf", "WOOL-SCARF", $"query-{Guid.NewGuid():N}"))).IsSuccess);

        var search = await service.ListProductsAsync(new CatalogProductQuery("linen", null, null, 10));
        var firstPage = await service.ListProductsAsync(new CatalogProductQuery(null, null, null, 1));
        var secondPage = await service.ListProductsAsync(new CatalogProductQuery(null, null, firstPage.Value!.NextCursor, 1));

        Assert.True(search.IsSuccess);
        Assert.Single(search.Value!.Items);
        Assert.Equal("linen-shirt", search.Value.Items[0].Slug);
        Assert.True(firstPage.IsSuccess);
        Assert.NotNull(firstPage.Value!.NextCursor);
        Assert.True(secondPage.IsSuccess);
        Assert.Single(secondPage.Value!.Items);
        Assert.NotEqual(firstPage.Value.Items[0].Id, secondPage.Value.Items[0].Id);
    }

    private static CatalogService CreateService(Kreyora.Infrastructure.Persistence.AppDbContext dbContext, TenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(dbContext, accessor, new Correlation("catalog-integration-test"), authorizer);
        return new CatalogService(dbContext, accessor, authorizer, audit);
    }

    private static async Task<Tenant> CreateTenantAsync(Kreyora.Infrastructure.Persistence.AppDbContext dbContext, string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{suffix}");
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private static CreateProductRequest NewCreateRequest(string slug, string sku, string idempotencyKey) => new(
        "Classic Tee",
        "A soft cotton tee.",
        slug,
        [new CreateProductVariantRequest(sku, "Small", new Dictionary<string, string> { ["Size"] = "S" }, 1_500m, 1_800m, true)],
        idempotencyKey);

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
