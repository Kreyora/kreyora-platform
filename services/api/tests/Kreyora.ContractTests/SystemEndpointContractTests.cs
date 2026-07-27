using Microsoft.AspNetCore.Mvc.Testing;
using System.Text.Json;

namespace Kreyora.ContractTests;

public class SystemEndpointContractTests : IClassFixture<WebApplicationFactory<Kreyora.WebApi.Program>>
{
    private readonly WebApplicationFactory<WebApi.Program> _factory;

    public SystemEndpointContractTests(WebApplicationFactory<WebApi.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SystemInfo_ReturnsExpectedShape()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/system/info");
        response.EnsureSuccessStatusCode();

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("name", out _), "Missing 'name' property");
        Assert.True(root.TryGetProperty("version", out _), "Missing 'version' property");
        Assert.True(root.TryGetProperty("environment", out _), "Missing 'environment' property");
    }
}
