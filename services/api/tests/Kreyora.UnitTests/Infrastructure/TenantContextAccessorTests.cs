using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Tenancy;

namespace Kreyora.UnitTests.Infrastructure;

public class TenantContextAccessorTests
{
    [Fact]
    public void NestedScopes_RestoreThePreviousContext_AndThenClearIt()
    {
        var accessor = new TenantContextAccessor();
        var outer = new TenantContext("tenant-outer", "user-outer", "membership-outer", TenantRole.Owner);
        var inner = new TenantContext("tenant-inner", "user-inner", "membership-inner", TenantRole.Admin);

        using (accessor.BeginScope(outer))
        {
            Assert.Equal(outer, accessor.Current);

            using (accessor.BeginScope(inner))
            {
                Assert.Equal(inner, accessor.RequireCurrent());
            }

            Assert.Equal(outer, accessor.RequireCurrent());
        }

        Assert.Null(accessor.Current);
        Assert.Throws<InvalidOperationException>(() => accessor.RequireCurrent());
    }

    [Fact]
    public void TenantKeyBuilder_UsesTheVerifiedTenantPrefix_AndRejectsUnsafeSegments()
    {
        var accessor = new TenantContextAccessor();
        var keys = new TenantKeyBuilder(accessor);

        Assert.Throws<InvalidOperationException>(() => keys.BuildCacheKey("catalog"));

        using (accessor.BeginScope(new TenantContext("tenant-1", null, null, null)))
        {
            Assert.Equal("tenants/tenant-1/products/image.webp", keys.BuildStorageObjectKey("products", "image.webp"));
            Assert.Equal("tenant:tenant-1:catalog:list", keys.BuildCacheKey("catalog", "list"));
            Assert.Equal("tenant:tenant-1:products", keys.BuildSearchKey("products"));
            Assert.Throws<ArgumentException>(() => keys.BuildStorageObjectKey("../private"));
        }
    }
}
