using System.Net;
using Kreyora.IntegrationTests.Fixtures;

namespace Kreyora.IntegrationTests;

public sealed class CatalogInventoryEndpointAuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client;

    public CatalogInventoryEndpointAuthorizationTests(TestWebApplicationFactory factory)
    {
        client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/v1/catalog/products")]
    [InlineData("/v1/inventory/variants/01H00000000000000000000000")]
    [InlineData("/v1/media/products/01H00000000000000000000000")]
    [InlineData("/v1/store")]
    public async Task TenantOwnedEndpoints_RequireASelectedTenantContextBeforeDispatch(string path)
    {
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
