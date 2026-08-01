using Kreyora.IntegrationTests.Fixtures;

namespace Kreyora.IntegrationTests;

public class WebApiSmokeTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public WebApiSmokeTests(TestWebApplicationFactory factory)
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
