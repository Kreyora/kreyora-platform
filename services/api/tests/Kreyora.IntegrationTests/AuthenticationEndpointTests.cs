using System.Net;
using System.Net.Http.Json;
using Kreyora.Application.Authentication;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.IntegrationTests;

public class AuthenticationEndpointTests
{
    [Fact]
    public async Task Register_WithCsrfToken_InvokesTheAuthenticationService()
    {
        await using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddScoped<IAuthenticationService, SuccessfulAuthenticationService>();
                });
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/v1/auth/csrf");
        Assert.NotNull(csrf);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterOwnerRequest(
                "Registration Test",
                "registration-test@kreyora.local",
                "Temp!Kreyora2026",
                "Registration Workspace",
                "registration-workspace"))
        };
        request.Headers.Add("X-CSRF-Token", csrf!.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PasswordResetRequest_AlwaysReturnsTheSameGenericResponseWithoutTokenData()
    {
        await using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services => services.AddScoped<IAuthenticationService, SuccessfulAuthenticationService>());
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/v1/auth/csrf");
        Assert.NotNull(csrf);

        var knownResponse = await RequestResetAsync(client, csrf!.Token, "known@kreyora.test");
        var unknownResponse = await RequestResetAsync(client, csrf.Token, "unknown@kreyora.test");

        Assert.Equal(HttpStatusCode.Accepted, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        var knownBody = await knownResponse.Content.ReadAsStringAsync();
        var unknownBody = await unknownResponse.Content.ReadAsStringAsync();
        Assert.Equal(knownBody, unknownBody);
        Assert.Contains("If an account exists for that email address", knownBody);
        Assert.DoesNotContain("token", knownBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignInRateLimit_IsPerClientAddress_NotSharedByEveryone()
    {
        await using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddScoped<IAuthenticationService, SuccessfulAuthenticationService>();
                    services.AddSingleton<IStartupFilter, TestClientAddressStartupFilter>();
                });
            });

        using var first = await ClientFromAsync(factory, "203.0.113.10");
        using var second = await ClientFromAsync(factory, "203.0.113.20");

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(first)).StatusCode);
        }

        var sixthFromFirst = await SignInAsync(first);
        var firstFromSecond = await SignInAsync(second);

        Assert.Equal(HttpStatusCode.TooManyRequests, sixthFromFirst.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, firstFromSecond.StatusCode);
    }

    [Fact]
    public async Task RegistrationRateLimit_IsPerClientAddress_NotSharedByEveryone()
    {
        await using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddScoped<IAuthenticationService, SuccessfulAuthenticationService>();
                    services.AddSingleton<IStartupFilter, TestClientAddressStartupFilter>();
                });
            });

        using var first = await ClientFromAsync(factory, "203.0.113.30");
        using var second = await ClientFromAsync(factory, "203.0.113.40");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(first, attempt)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(first, 4)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(second, 5)).StatusCode);
    }

    private static async Task<HttpClient> ClientFromAsync(WebApplicationFactory<Kreyora.WebApi.Program> factory, string address)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.Header, address);
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/v1/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf!.Token);
        return client;
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client) =>
        client.PostAsJsonAsync("/v1/auth/sign-in", new SignInRequest("owner@kreyora.test", "Temp!Kreyora2026"));

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, int n) =>
        client.PostAsJsonAsync("/v1/auth/register", new RegisterOwnerRequest(
            $"Rate Test {n}", $"rate-{n}@kreyora.local", "Temp!Kreyora2026", $"Rate Workspace {n}", $"rate-workspace-{n}"));

    /// <summary>Test-only: lets each test client present its own remote address, as distinct real clients would.</summary>
    private sealed class TestClientAddressStartupFilter : IStartupFilter
    {
        public const string Header = "X-Test-Client-Address";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var value) && System.Net.IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private static Task<HttpResponseMessage> RequestResetAsync(HttpClient client, string csrfToken, string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/password-reset/request")
        {
            Content = JsonContent.Create(new { email })
        };
        request.Headers.Add("X-CSRF-Token", csrfToken);
        return client.SendAsync(request);
    }

    private sealed record CsrfResponse(string Token);

    private sealed class SuccessfulAuthenticationService : IAuthenticationService
    {
        public Task<RegistrationResult> RegisterOwnerAsync(RegisterOwnerRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new RegistrationResult(true, []));

        public Task<SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SignInResult(true, false));

        public Task SignOutAsync() => Task.CompletedTask;

        public Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<AuthenticatedUser?>(null);

        public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PasswordResetResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PasswordResetResult(true, []));
    }
}
