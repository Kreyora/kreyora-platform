using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kreyora.IntegrationTests;

public class ProblemResponseTests : IClassFixture<WebApplicationFactory<Kreyora.WebApi.Program>>
{
    private readonly HttpClient _client;

    public ProblemResponseTests(WebApplicationFactory<WebApi.Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NonExistentEndpoint_Returns404()
    {
        var response = await _client.GetAsync("/v1/system/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Response_ContainsCorrelationId_InHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/system/info");
        request.Headers.Add("X-Correlation-ID", "problem-test-123");

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.Equal("problem-test-123", response.Headers.GetValues("X-Correlation-ID").First());
    }
}
