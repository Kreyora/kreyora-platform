using Kreyora.Domain.Catalog;

namespace Kreyora.UnitTests.Domain;

public class ProductTests
{
    private const string TenantId = "01J00000000000000000000001";

    [Fact]
    public void Create_NormalizesSlug_AndStartsAsDraft()
    {
        var product = Product.Create(TenantId, "  Classic Tee  ", "  Soft cotton  ", "  Classic-Tee  ");

        Assert.Equal("Classic Tee", product.Title);
        Assert.Equal("Soft cotton", product.Description);
        Assert.Equal("classic-tee", product.Slug);
        Assert.Equal("CLASSIC-TEE", product.NormalizedSlug);
        Assert.Equal(ProductPublishState.Draft, product.PublishState);
    }

    [Theory]
    [InlineData("classic--tee")]
    [InlineData("classic_tee")]
    [InlineData("classic tee")]
    public void Create_RejectsInvalidSlug(string slug)
    {
        Assert.Throws<ArgumentException>(() => Product.Create(TenantId, "Classic Tee", null, slug));
    }

    [Fact]
    public void Publish_RequiresAPublishedVariant_ThenSucceeds()
    {
        var product = Product.Create(TenantId, "Classic Tee", null, "classic-tee");
        product.AddVariant("TEE-S", "Small", null, 1_500m, 1_800m, false);

        Assert.Throws<InvalidOperationException>(product.Publish);

        product.UpdateVariant(product.Variants.Single().Id, "TEE-S", "Small", null, 1_500m, 1_800m, true);
        product.Publish();

        Assert.Equal(ProductPublishState.Published, product.PublishState);
    }

    [Fact]
    public void Variant_RejectsInvalidNprPrices_AndDuplicateOptionNames()
    {
        var product = Product.Create(TenantId, "Classic Tee", null, "classic-tee");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            product.AddVariant("TEE-S", "Small", null, 1_500.001m, null, true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            product.AddVariant("TEE-S", "Small", null, 1_500m, 1_499m, true));
        Assert.Throws<ArgumentException>(() =>
            product.AddVariant("TEE-S", "Small", new Dictionary<string, string>
            {
                ["Size"] = "S",
                ["size"] = "Small"
            }, 1_500m, null, true));
    }

    [Fact]
    public void Archive_IsTerminal()
    {
        var product = Product.Create(TenantId, "Classic Tee", null, "classic-tee");
        product.Archive();

        Assert.Throws<InvalidOperationException>(() => product.UpdateDetails("Updated Tee", null, "updated-tee"));
        Assert.Throws<InvalidOperationException>(() => product.AddVariant("TEE-S", "Small", null, 1_500m, null, true));
    }
}
