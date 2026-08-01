using Kreyora.Application.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.IntegrationTests.Fixtures;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Kreyora.WebApi.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<AntiforgeryOptions>(options =>
            {
                options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            });
            services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
            services.AddAuthorization();
            services.AddScoped<ITenantContextResolutionService, NoOpTenantContextResolutionService>();
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://test.kreyora.test",
                ["Email:Smtp:ApplicationName"] = "Kreyora Test",
                ["Email:Smtp:Host"] = "smtp.kreyora.test",
                ["Email:Smtp:Port"] = "587",
                ["Email:Smtp:Username"] = string.Empty,
                ["Email:Smtp:Password"] = string.Empty,
                ["Email:Smtp:Security"] = "StartTls",
                ["Email:Smtp:SenderEmail"] = "no-reply@kreyora.test",
                ["Email:Smtp:SenderDisplayName"] = "Kreyora Test",
                ["Email:Smtp:ApplicationPublicUrl"] = "https://seller.kreyora.test",
                ["Email:Smtp:PasswordResetTokenLifetimeMinutes"] = "60"
            });
        });
    }

    private sealed class NoOpTenantContextResolutionService : ITenantContextResolutionService
    {
        public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);

        public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantContext?>(null);

        public Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantContext?>(null);

        public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantContext?>(null);
    }
}
