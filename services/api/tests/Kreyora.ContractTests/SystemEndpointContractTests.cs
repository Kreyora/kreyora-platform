using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kreyora.ContractTests;

public class SystemEndpointContractTests : IClassFixture<WebApplicationFactory<Kreyora.WebApi.Program>>
{
    private readonly HttpClient _client;

    public SystemEndpointContractTests(WebApplicationFactory<WebApi.Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SystemInfo_ReturnsExpectedShape()
    {
        var response = await _client.GetAsync("/v1/system/info");
        response.EnsureSuccessStatusCode();

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("name", out _), "Missing 'name' property");
        Assert.True(root.TryGetProperty("version", out _), "Missing 'version' property");
        Assert.True(root.TryGetProperty("environment", out _), "Missing 'environment' property");
    }

    [Fact]
    public async Task SystemInfo_ReturnsCorrectContentType()
    {
        var response = await _client.GetAsync("/v1/system/info");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SystemInfo_ReportsApiVersion()
    {
        var response = await _client.GetAsync("/v1/system/info");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("api-supported-versions"));
    }
}
