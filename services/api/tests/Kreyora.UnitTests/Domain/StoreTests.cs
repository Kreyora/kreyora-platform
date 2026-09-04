using Kreyora.Domain.Storefront;

namespace Kreyora.UnitTests.Domain;

public sealed class StoreTests
{
    private const string TenantId = "01J00000000000000000000001";

    [Fact]
    public void Create_NormalizesSlugAndAccent_AndStartsDraft()
    {
        var store = Store.Create(TenantId, Settings("  Namaste Crafts  ", " Namaste-Crafts ", "#a1b2c3"));

        Assert.Equal("Namaste Crafts", store.DisplayName);
        Assert.Equal("namaste-crafts", store.PlatformSlug);
        Assert.Equal("NAMASTE-CRAFTS", store.NormalizedPlatformSlug);
        Assert.Equal("#A1B2C3", store.BrandAccentHex);
        Assert.Equal(StoreStatus.Draft, store.Status);
    }

    [Theory]
    [InlineData("bad--slug")]
    [InlineData("bad_slug")]
    [InlineData("bad slug")]
    public void Create_RejectsInvalidStoreSlug(string slug) =>
        Assert.Throws<ArgumentException>(() => Store.Create(TenantId, Settings("Store", slug, null)));

    [Fact]
    public void Settings_RejectExecutablePolicyAndInvalidThemeValues()
    {
        Assert.Throws<ArgumentException>(() => Store.Create(TenantId, Settings("Store", "safe-store", "blue")));
        Assert.Throws<ArgumentException>(() => Store.Create(TenantId, Settings("Store", "safe-store", null, terms: "<script>alert(1)</script>")));
    }

    [Fact]
    public void ActiveStore_CannotChangePlatformSlug()
    {
        var store = Store.Create(TenantId, Settings("Store", "safe-store", null));
        store.Activate(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => store.UpdateSettings(Settings("Store", "different-store", null)));
    }

    private static StoreSettings Settings(string name, string slug, string? accent, string? terms = null) => new(
        name, slug, null, StoreThemePreset.Default, accent, "Seller", "seller@example.com", "+9779800000000", null,
        null, null, null, terms, "Privacy", "Returns", "Payment");
}
