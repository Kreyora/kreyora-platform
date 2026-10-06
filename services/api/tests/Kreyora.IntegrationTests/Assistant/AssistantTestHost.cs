using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Assistant;

/// <summary>
/// Shared test host for assistant tests (M09-S02/S03): real API + PostgreSQL, header-chosen role, in-memory private
/// storage, configurable AI switch/mode. The Hangfire server is off so tests drive indexing deterministically.
/// </summary>
internal sealed class AssistantTestHost(string connectionString, InMemoryStorage storage, bool aiEnabled = false, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Kreyora.WebApi.Program>
{
    public const string RoleHeader = "X-Test-Role";

    public InMemoryStorage Storage => storage;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", connectionString);
        builder.UseSetting("BackgroundJobs:ServerEnabled", "false"); // read during Program start-up, so it must be a host setting
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = connectionString,
            ["PublicStorefront:PlatformBaseDomain"] = "kreyora.test",
            ["PublicStorefront:EnableDevelopmentSlugRoutes"] = "true",
            ["Email:Smtp:ApplicationName"] = "Kreyora Test",
            ["Email:Smtp:Host"] = "smtp.kreyora.test",
            ["Email:Smtp:Port"] = "587",
            ["Email:Smtp:Security"] = "StartTls",
            ["Email:Smtp:SenderEmail"] = "no-reply@kreyora.test",
            ["Email:Smtp:SenderDisplayName"] = "Kreyora Test",
            ["Email:Smtp:ApplicationPublicUrl"] = "https://seller.kreyora.test",
            ["Ai:Enabled"] = aiEnabled ? "true" : "false",
            ["Ai:Mode"] = "Fake",
            ["BackgroundJobs:ServerEnabled"] = "false"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPrivateObjectStorage>(storage);
            services.PostConfigure<AntiforgeryOptions>(options => options.Cookie.SecurePolicy = CookieSecurePolicy.None);
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
            services.AddScoped<ITenantContextResolutionService, HeaderRoleResolver>();
            configureServices?.Invoke(services);
        });
    }

    /// <summary>Runs <paramref name="work"/> in a DI scope as the given tenant's owner.</summary>
    public async Task<T> AsTenantAsync<T>(string tenantId, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .BeginScope(new TenantContext(tenantId, "01H00000000000000000000004", "membership", TenantRole.Owner));
        return await work(scope.ServiceProvider);
    }

    private sealed class HeaderRoleResolver(IHttpContextAccessor httpContextAccessor, AppDbContext dbContext) : ITenantContextResolutionService
    {
        public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);

        public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers[RoleHeader].ToString();
            return Task.FromResult<TenantContext?>(Enum.TryParse<TenantRole>(header, out var role) ? new TenantContext(tenantId, userId, "membership", role) : null);
        }

        public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);

        // Background jobs use the real rule: only active tenants run.
        public Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default) =>
            new TenantContextResolutionService(dbContext).ResolveBackgroundContextAsync(tenantId, cancellationToken);
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AssistantTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Request.Headers.ContainsKey("X-Test-Auth")
                ? Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "01H00000000000000000000004")], SchemeName)), SchemeName)))
                : Task.FromResult(AuthenticateResult.NoResult());
    }
}

/// <summary>HTTP helpers for assistant tests: test auth, header-chosen role and tenant, and CSRF tokens.</summary>
internal static class AssistantHttp
{
    public static async Task<JsonNode> JsonAsync(HttpClient client, HttpMethod method, string path, string tenant, TenantRole role, object? body = null, string? idempotencyKey = null)
    {
        var response = await SendAsync(client, method, path, tenant, role, body, idempotencyKey);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} → {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string tenant, TenantRole role, object? body = null, string? idempotencyKey = null)
    {
        var request = Request(method, path, tenant, role);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-CSRF-Token", await CsrfAsync(client, tenant, role));
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = body is JsonNode node ? new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") : JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    public static HttpRequestMessage Request(HttpMethod method, string path, string tenantId, TenantRole role)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Auth", "yes");
        request.Headers.Add(AssistantTestHost.RoleHeader, role.ToString());
        request.Headers.Add(TenantContextMiddleware.TenantHeaderName, tenantId);
        return request;
    }

    public static async Task<string> CsrfAsync(HttpClient client, string tenantId, TenantRole role)
    {
        var response = await client.SendAsync(Request(HttpMethod.Get, "/v1/auth/csrf", tenantId, role));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }
}

/// <summary>In-memory private storage so tests can assert stored and purged originals.</summary>
internal sealed class InMemoryStorage : IPrivateObjectStorage
{
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    public bool FailDeletes { get; set; }

    public async Task PutAsync(StorageObjectWrite request, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        Objects[request.ObjectKey] = buffer.ToArray();
    }

    public Task<StorageObjectMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Objects.TryGetValue(objectKey, out var bytes) ? new StorageObjectMetadata(objectKey, "text/plain", bytes.Length) : null);

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Objects.TryGetValue(objectKey, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        if (FailDeletes) throw new IOException("Simulated storage outage.");
        Objects.TryRemove(objectKey, out _);
        return Task.CompletedTask;
    }
}
