using System.Net;
using Kreyora.IntegrationTests.Fixtures;

namespace Kreyora.IntegrationTests;

public sealed class ScalarEndpointTests : IClassFixture<DevelopmentWebApplicationFactory>, IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _developmentClient;
    private readonly HttpClient _testingClient;

    public ScalarEndpointTests(DevelopmentWebApplicationFactory developmentFactory, TestWebApplicationFactory testingFactory)
    {
        _developmentClient = developmentFactory.CreateClient();
        _testingClient = testingFactory.CreateClient();
    }

    [Fact]
    public async Task Development_ExposesScalarAndItsOpenApiDocument()
    {
        var scalarResponse = await _developmentClient.GetAsync("/scalar");
        var scalarPage = await scalarResponse.Content.ReadAsStringAsync();
        var openApiResponse = await _developmentClient.GetAsync("/openapi/v1.json");
        var openApiDocument = await openApiResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, scalarResponse.StatusCode);
        Assert.Equal("text/html", scalarResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Kreyora API", scalarPage, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal("application/json", openApiResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"openapi\"", openApiDocument, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonDevelopment_DoesNotExposeScalarOrOpenApi()
    {
        var scalarResponse = await _testingClient.GetAsync("/scalar");
        var openApiResponse = await _testingClient.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, scalarResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, openApiResponse.StatusCode);
    }
}
