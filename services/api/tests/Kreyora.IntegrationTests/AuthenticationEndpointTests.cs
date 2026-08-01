using System.Net;
using System.Net.Http.Json;
using Kreyora.Application.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.IntegrationTests;

public class AuthenticationEndpointTests
{
    [Fact]
    public async Task Register_WithCsrfToken_InvokesTheAuthenticationService()
    {
        await using var factory = new WebApplicationFactory<Kreyora.WebApi.Program>()
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
        await using var factory = new WebApplicationFactory<Kreyora.WebApi.Program>()
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
