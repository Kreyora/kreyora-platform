using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Authorization;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Tenancy;

public class PolicyEndpointTests
{
    [Fact]
    public async Task PermissionsEndpoint_UsesVerifiedTenantContext_NotCookieRoleClaims()
    {
        await using var factory = new PolicyFactory(TenantRole.Owner);
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/permissions");
        request.Headers.Add("X-Test-Auth", "yes");
        request.Headers.Add(TenantContextMiddleware.TenantHeaderName, PolicyFactory.TenantId);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(TenantPermissions.BillingManage, body);
    }

    [Fact]
    public async Task TenantEndpoints_RejectUnauthenticatedAndViewerPrivilegedCalls()
    {
        await using var ownerFactory = new PolicyFactory(TenantRole.Owner);
        using var unauthenticated = ownerFactory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.GetAsync("/v1/permissions")).StatusCode);

        await using var viewerFactory = new PolicyFactory(TenantRole.Viewer);
        using var viewer = viewerFactory.CreateClient();
        var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/audit-events");
        auditRequest.Headers.Add("X-Test-Auth", "yes");
        auditRequest.Headers.Add(TenantContextMiddleware.TenantHeaderName, PolicyFactory.TenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(auditRequest)).StatusCode);

        var grantRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/support-access-grants") { Content = new StringContent("{}") };
        grantRequest.Headers.Add("X-Test-Auth", "yes");
        grantRequest.Headers.Add(TenantContextMiddleware.TenantHeaderName, PolicyFactory.TenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(grantRequest)).StatusCode);
    }

    [Fact]
    public async Task TenantEndpoints_RejectMissingWorkspaceSelection()
    {
        await using var factory = new PolicyFactory(TenantRole.Owner);
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/permissions");
        request.Headers.Add("X-Test-Auth", "yes");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    private sealed class PolicyFactory(TenantRole role) : WebApplicationFactory<Kreyora.WebApi.Program>
    {
        public const string TenantId = "01H00000000000000000000000";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Smtp:ApplicationName"] = "Kreyora Test",
                ["Email:Smtp:Host"] = "smtp.kreyora.test",
                ["Email:Smtp:Port"] = "587",
                ["Email:Smtp:SenderEmail"] = "no-reply@kreyora.test",
                ["Email:Smtp:SenderDisplayName"] = "Kreyora Test",
                ["Email:Smtp:ApplicationPublicUrl"] = "https://seller.kreyora.test"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.AddAuthorization(options =>
                {
                    foreach (var permission in TenantPermissions.All)
                    {
                        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantPermissionRequirement(permission)));
                    }
                });
                services.AddScoped<ITenantPermissionAuthorizer, TenantPermissionAuthorizer>();
                services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
                services.AddScoped<ITenantContextResolutionService>(_ => new StaticResolver(role));
            });
        }
    }

    private sealed class StaticResolver(TenantRole role) : ITenantContextResolutionService
    {
        public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);
        public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantContext?>(tenantId == PolicyFactory.TenantId ? new TenantContext(tenantId, userId, "membership", role) : null);
        public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
        public Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "PolicyTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Request.Headers.ContainsKey("X-Test-Auth")
                ? Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "01H00000000000000000000004")], SchemeName)), SchemeName)))
                : Task.FromResult(AuthenticateResult.NoResult());
    }
}
