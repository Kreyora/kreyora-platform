using Microsoft.AspNetCore.Mvc.Testing;

namespace Kreyora.IntegrationTests;

public class WebApiSmokeTests : IClassFixture<WebApplicationFactory<Kreyora.WebApi.Program>>
{
    private readonly WebApplicationFactory<WebApi.Program> _factory;

    public WebApiSmokeTests(WebApplicationFactory<WebApi.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SystemInfo_ReturnsSuccess()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/system/info");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Kreyora API", content);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
