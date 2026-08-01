using Kreyora.IntegrationTests.Fixtures;

namespace Kreyora.IntegrationTests;

public class CorrelationIdTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CorrelationIdTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Request_WithoutCorrelationHeader_GetsOneGenerated()
    {
        var response = await _client.GetAsync("/v1/system/info");

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        var correlationId = response.Headers.GetValues("X-Correlation-ID").First();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task Request_WithCorrelationHeader_EchoesItBack()
    {
        var expected = "test-correlation-12345";
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/system/info");
        request.Headers.Add("X-Correlation-ID", expected);

        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var actual = response.Headers.GetValues("X-Correlation-ID").First();
        Assert.Equal(expected, actual);
    }
}
