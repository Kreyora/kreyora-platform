using Kreyora.Application.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.IntegrationTests.Fixtures;

public sealed class DevelopmentWebApplicationFactory : WebApplicationFactory<Kreyora.WebApi.Program>
{
    private readonly string? _originalDatabaseConnectionString = Environment.GetEnvironmentVariable("Database__ConnectionString");
    private readonly string? _originalKreyoraConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__kreyora");

    public DevelopmentWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Database__ConnectionString", string.Empty);
        Environment.SetEnvironmentVariable("ConnectionStrings__kreyora", string.Empty);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
            services.AddAuthorization();
            services.AddScoped<ITenantContextResolutionService, NoOpTenantContextResolutionService>();
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = string.Empty,
                ["ConnectionStrings:kreyora"] = string.Empty,
                ["Cors:AllowedOrigins:0"] = "https://test.kreyora.test",
                ["PublicStorefront:PlatformBaseDomain"] = "kreyora.test",
                ["PublicStorefront:EnableDevelopmentSlugRoutes"] = "true",
                ["Email:Smtp:ApplicationName"] = "Kreyora Test",
                ["Email:Smtp:Host"] = "smtp.kreyora.test",
                ["Email:Smtp:Port"] = "587",
                ["Email:Smtp:Username"] = string.Empty,
                ["Email:Smtp:Password"] = string.Empty,
                ["Email:Smtp:Security"] = "StartTls",
                ["Email:Smtp:SenderEmail"] = "no-reply@kreyora.test",
                ["Email:Smtp:SenderDisplayName"] = "Kreyora Test",
                ["Email:Smtp:ApplicationPublicUrl"] = "http://localhost:5030",
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("Database__ConnectionString", _originalDatabaseConnectionString);
            Environment.SetEnvironmentVariable("ConnectionStrings__kreyora", _originalKreyoraConnectionString);
        }

        base.Dispose(disposing);
    }
}
