using Microsoft.AspNetCore.Mvc.Testing;

namespace Kreyora.IntegrationTests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Kreyora.WebApi.Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<WebApi.Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", content);
    }

    [Fact]
    public async Task HealthLive_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", content);
    }

    [Fact]
    public async Task HealthReady_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/ready");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", content);
    }
}
