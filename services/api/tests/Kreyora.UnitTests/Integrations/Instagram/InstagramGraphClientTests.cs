using System.Net;
using System.Text;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Infrastructure.Integrations.Instagram;
using Microsoft.Extensions.Options;

namespace Kreyora.UnitTests.Integrations.Instagram;

public sealed class InstagramGraphClientTests
{
    [Fact]
    public async Task ValidatePageLink_WithMatchingLink_ReturnsValid()
    {
        var client = CreateClient(Queue(
            Json("{\"id\":\"page_1\",\"instagram_business_account\":{\"id\":\"igsid_1\"}}")));

        var result = await client.ValidatePageLinkAsync("pat_stub", "page_1", "igsid_1");

        Assert.True(result.IsValid);
        Assert.Equal(InstagramValidationKind.Valid, result.Kind);
    }

    [Fact]
    public async Task ValidatePageLink_WithMismatchedLink_ReturnsIdentityMismatch()
    {
        var client = CreateClient(Queue(
            Json("{\"id\":\"page_1\",\"instagram_business_account\":{\"id\":\"igsid_other\"}}")));

        var result = await client.ValidatePageLinkAsync("pat_stub", "page_1", "igsid_1");

        Assert.False(result.IsValid);
        Assert.Equal(InstagramValidationKind.IdentityMismatch, result.Kind);
    }

    [Fact]
    public async Task ValidateAccount_WithUsername_ReturnsValid()
    {
        var client = CreateClient(Queue(
            Json("{\"id\":\"igsid_1\",\"username\":\"test.shop\"}")));

        var result = await client.ValidateAccountAsync("pat_stub", "igsid_1");

        Assert.True(result.IsValid);
        Assert.Equal("test.shop", result.InstagramUsername);
    }

    [Theory]
    [InlineData(190, InstagramValidationKind.TokenExpired)]
    [InlineData(10, InstagramValidationKind.PermissionDenied)]
    [InlineData(613, InstagramValidationKind.Throttled)]
    [InlineData(80002, InstagramValidationKind.Throttled)]
    [InlineData(800, InstagramValidationKind.ProviderError)]
    public async Task ValidateAccount_WithGraphErrors_MapsKinds(int code, InstagramValidationKind expected)
    {
        var client = CreateClient(Queue(
            Json($"{{\"error\":{{\"message\":\"Denied.\",\"code\":{code}}}}}", HttpStatusCode.BadRequest)));

        var result = await client.ValidateAccountAsync("pat_stub", "igsid_1");

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Kind);
        Assert.Equal(code.ToString(System.Globalization.CultureInfo.InvariantCulture), result.ProviderErrorCode);
    }

    [Fact]
    public async Task ValidateAccount_WithServerError_ReturnsTransient()
    {
        var client = CreateClient(Queue(
            Json("{\"error\":{\"message\":\"Oops.\",\"code\":1}}", HttpStatusCode.InternalServerError)));

        var result = await client.ValidateAccountAsync("pat_stub", "igsid_1");

        Assert.Equal(InstagramValidationKind.Transient, result.Kind);
    }

    [Fact]
    public async Task ValidateAccount_WithTimeout_ReturnsTransient()
    {
        var handler = new FuncHandler((_, ct) => Task.Delay(5000, ct).ContinueWith<HttpResponseMessage>(
            _ => Json("{}"), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default));
        var client = new InstagramGraphClient(
            new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(20) },
            Options.Create(new InstagramGraphOptions()));

        var result = await client.ValidateAccountAsync("pat_stub", "igsid_1");

        Assert.Equal(InstagramValidationKind.Transient, result.Kind);
        Assert.Equal("timeout", result.ProviderErrorCode);
    }

    [Fact]
    public async Task Requests_UseBearerHeader_VersionedPath_AndNeverLeakTokenInUrl()
    {
        HttpRequestMessage? seen = null;
        var handler = new FuncHandler((request, _) =>
        {
            seen = request;
            return Task.FromResult(Json("{\"id\":\"igsid_1\",\"username\":\"test.shop\"}"));
        });
        var client = new InstagramGraphClient(new HttpClient(handler), Options.Create(new InstagramGraphOptions()));

        await client.ValidateAccountAsync("super_secret_pat", "igsid_1");

        Assert.NotNull(seen);
        Assert.Equal("Bearer", seen.Headers.Authorization?.Scheme);
        Assert.Equal("super_secret_pat", seen.Headers.Authorization?.Parameter);
        Assert.StartsWith("https://graph.facebook.com/v21.0/igsid_1", seen.RequestUri?.ToString());
        Assert.DoesNotContain("super_secret_pat", seen.RequestUri?.ToString());
    }

    [Fact]
    public async Task ValidateAccount_WithBlankToken_ReturnsPermissionDeniedWithoutHttpCall()
    {
        var calls = 0;
        var handler = new FuncHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json("{}"));
        });
        var client = new InstagramGraphClient(new HttpClient(handler), Options.Create(new InstagramGraphOptions()));

        var result = await client.ValidateAccountAsync("  ", "igsid_1");

        Assert.Equal(InstagramValidationKind.PermissionDenied, result.Kind);
        Assert.Equal(0, calls);
    }

    // ---- customer profile lookup (M09-S08 Q8) ----

    [Fact]
    public async Task GetUserProfile_ReadsNameAndUsername_WithTheBearerToken()
    {
        HttpRequestMessage? sent = null;
        var client = new InstagramGraphClient(new HttpClient(new FuncHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(Json("{\"name\":\"Test Customer\",\"username\":\"test.customer\",\"id\":\"1234\"}"));
        })), Options.Create(new InstagramGraphOptions()));

        var result = await client.GetUserProfileAsync("pat_stub", "1234");

        Assert.True(result.IsSuccess);
        Assert.Equal(("Test Customer", "test.customer"), (result.Name, result.Username));
        Assert.Contains("/1234?fields=name,username", sent!.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.DoesNotContain("pat_stub", sent.RequestUri.ToString(), StringComparison.Ordinal); // the token never goes in the URL
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"Denied.\",\"code\":10}}", HttpStatusCode.BadRequest, InstagramValidationKind.PermissionDenied)]
    [InlineData("{\"error\":{\"message\":\"Slow down.\",\"code\":613}}", HttpStatusCode.BadRequest, InstagramValidationKind.Throttled)]
    [InlineData("{\"id\":\"1234\"}", HttpStatusCode.OK, InstagramValidationKind.ProviderError)]
    [InlineData("not json", HttpStatusCode.OK, InstagramValidationKind.ProviderError)]
    public async Task GetUserProfile_Failures_KeepNoValues(string body, HttpStatusCode status, InstagramValidationKind expected)
    {
        var result = await CreateClient(Queue(Json(body, status))).GetUserProfileAsync("pat_stub", "1234");

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Kind);
        Assert.Null(result.Name);
        Assert.Null(result.Username);
    }

    [Theory]
    [InlineData("", "1234", InstagramValidationKind.PermissionDenied)]
    [InlineData("pat_stub", "12/../me", InstagramValidationKind.IdentityMismatch)]
    public async Task GetUserProfile_RejectsBadInput_WithoutCalling(string token, string scopedId, InstagramValidationKind expected)
    {
        var calls = 0;
        var client = new InstagramGraphClient(new HttpClient(new FuncHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json("{}"));
        })), Options.Create(new InstagramGraphOptions()));

        Assert.Equal(expected, (await client.GetUserProfileAsync(token, scopedId)).Kind);
        Assert.Equal(0, calls);
    }

    private static InstagramGraphClient CreateClient(Queue<HttpResponseMessage> responses) =>
        new(new HttpClient(new FuncHandler((request, _) =>
            Task.FromResult(responses.Count > 0 ? responses.Dequeue() : Json("{}")))),
            Options.Create(new InstagramGraphOptions()));

    private static Queue<HttpResponseMessage> Queue(params HttpResponseMessage[] responses) =>
        new(responses);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FuncHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
