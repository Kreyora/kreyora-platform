using System.Text.Json;

namespace Kreyora.ContractTests;

public class SystemEndpointContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemEndpointContractTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task SystemInfo_EchoesCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/system/info");
        request.Headers.Add("X-Correlation-ID", correlationId);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        var echoedId = response.Headers.GetValues("X-Correlation-ID").First();
        Assert.Equal(correlationId, echoedId);
    }

    [Fact]
    public async Task SystemInfo_GeneratesCorrelationIdWhenNotProvided()
    {
        var response = await _client.GetAsync("/v1/system/info");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        var generatedId = response.Headers.GetValues("X-Correlation-ID").First();
        Assert.False(string.IsNullOrWhiteSpace(generatedId));
    }
}
