using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Conversations;

/// <summary>
/// M08-S04 inbox API over real HTTP and PostgreSQL: authentication, role policies, antiforgery,
/// cross-tenant not-found, paging, keyset timeline, preview truncation, and response minimization.
/// </summary>
public sealed class ConversationEndpointTests : IClassFixture<PostgresFixture>
{
    private const string RoleHeader = "X-Test-Role";
    private readonly PostgresFixture fixture;

    public ConversationEndpointTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Unauthenticated_IsRejected()
    {
        await MigrateAsync();
        await using var factory = new InboxFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/conversations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanRead_ButCannotMarkRead_OperatorCanMarkRead()
    {
        var seeded = await SeedAsync("s04-http-roles", messages: 2);
        await using var factory = new InboxFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var list = await client.SendAsync(Request(HttpMethod.Get, "/v1/conversations", seeded.TenantId, TenantRole.Viewer));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var viewerToken = await CsrfAsync(client, seeded.TenantId, TenantRole.Viewer);
        var viewerPost = Request(HttpMethod.Post, $"/v1/conversations/{seeded.ConversationId}/read", seeded.TenantId, TenantRole.Viewer);
        viewerPost.Headers.Add("X-CSRF-Token", viewerToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(viewerPost)).StatusCode);

        var operatorToken = await CsrfAsync(client, seeded.TenantId, TenantRole.Operator);
        var operatorPost = Request(HttpMethod.Post, $"/v1/conversations/{seeded.ConversationId}/read", seeded.TenantId, TenantRole.Operator);
        operatorPost.Headers.Add("X-CSRF-Token", operatorToken);
        var marked = await client.SendAsync(operatorPost);

        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        using var body = JsonDocument.Parse(await marked.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task MarkRead_WithoutAntiforgeryToken_IsRejected()
    {
        var seeded = await SeedAsync("s04-http-csrf", messages: 1);
        await using var factory = new InboxFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(
            Request(HttpMethod.Post, $"/v1/conversations/{seeded.ConversationId}/read", seeded.TenantId, TenantRole.Operator));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        Assert.Equal(1, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == seeded.ConversationId)).UnreadCount);
    }

    [Fact]
    public async Task OtherTenantsConversation_IsNotFoundEverywhere_AndNeverListed()
    {
        var victim = await SeedAsync("s04-http-victim", messages: 1);
        var attacker = await SeedAsync("s04-http-attacker", messages: 1);
        await using var factory = new InboxFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var detail = await client.SendAsync(Request(HttpMethod.Get, $"/v1/conversations/{victim.ConversationId}", attacker.TenantId, TenantRole.Owner));
        var messages = await client.SendAsync(Request(HttpMethod.Get, $"/v1/conversations/{victim.ConversationId}/messages", attacker.TenantId, TenantRole.Owner));
        var token = await CsrfAsync(client, attacker.TenantId, TenantRole.Owner);
        var markRead = Request(HttpMethod.Post, $"/v1/conversations/{victim.ConversationId}/read", attacker.TenantId, TenantRole.Owner);
        markRead.Headers.Add("X-CSRF-Token", token);
        var marked = await client.SendAsync(markRead);
        var list = await client.SendAsync(Request(HttpMethod.Get, "/v1/conversations?pageSize=100", attacker.TenantId, TenantRole.Owner));
        var listBody = await list.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, messages.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, marked.StatusCode);
        Assert.DoesNotContain(victim.ConversationId, listBody, StringComparison.Ordinal);
        Assert.Contains(attacker.ConversationId, listBody, StringComparison.Ordinal);

        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        Assert.Equal(1, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == victim.ConversationId)).UnreadCount);
    }

    [Fact]
    public async Task List_FiltersPagesAndTruncatesPreview_WithoutRawProviderFields()
    {
        var seeded = await SeedAsync("s04-http-list", messages: 1, lastText: new string('x', 400), extraConversations: 2);
        await using var factory = new InboxFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var page = await client.SendAsync(Request(HttpMethod.Get, "/v1/conversations?page=1&pageSize=2", seeded.TenantId, TenantRole.Viewer));
        var unreadOnly = await client.SendAsync(Request(HttpMethod.Get, "/v1/conversations?unreadOnly=true&pageSize=100", seeded.TenantId, TenantRole.Viewer));
        var spamOnly = await client.SendAsync(Request(HttpMethod.Get, "/v1/conversations?status=Spam", seeded.TenantId, TenantRole.Viewer));

        var pageText = await page.Content.ReadAsStringAsync();
        using var pageJson = JsonDocument.Parse(pageText);
        Assert.Equal(3, pageJson.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, pageJson.RootElement.GetProperty("items").GetArrayLength());

        using var unreadJson = JsonDocument.Parse(await unreadOnly.Content.ReadAsStringAsync());
        var newest = unreadJson.RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == seeded.ConversationId);
        var preview = newest.GetProperty("lastMessagePreview").GetString()!;
        Assert.Equal(141, preview.Length);
        Assert.EndsWith("…", preview, StringComparison.Ordinal);
        Assert.StartsWith("Instagram user ·", newest.GetProperty("customerLabel").GetString(), StringComparison.Ordinal);

        using var spamJson = JsonDocument.Parse(await spamOnly.Content.ReadAsStringAsync());
        Assert.Equal(0, spamJson.RootElement.GetProperty("totalCount").GetInt32());

        foreach (var forbidden in new[] { "rawPayload", "externalUserId", "reactorChannelId", "igsid_" })
        {
            Assert.DoesNotContain(forbidden, pageText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Messages_KeysetPagesBackwardsInChronologicalOrder()
    {
        var seeded = await SeedAsync("s04-http-timeline", messages: 5);
        await using var factory = new InboxFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var latest = await GetJsonAsync(client, $"/v1/conversations/{seeded.ConversationId}/messages?pageSize=2", seeded.TenantId);
        var cursor = latest.RootElement.GetProperty("nextBeforeMessageId").GetString();
        var older = await GetJsonAsync(client, $"/v1/conversations/{seeded.ConversationId}/messages?pageSize=2&before={cursor}", seeded.TenantId);
        var oldestCursor = older.RootElement.GetProperty("nextBeforeMessageId").GetString();
        var oldest = await GetJsonAsync(client, $"/v1/conversations/{seeded.ConversationId}/messages?pageSize=2&before={oldestCursor}", seeded.TenantId);

        Assert.Equal("m3|m4", Texts(latest));
        Assert.Equal("m1|m2", Texts(older));
        Assert.Equal("m0", Texts(oldest));
        // Null members are omitted by the API's JSON options: no cursor means no older page.
        Assert.False(oldest.RootElement.TryGetProperty("nextBeforeMessageId", out var end) && end.ValueKind != JsonValueKind.Null);

        var foreignCursor = await client.SendAsync(Request(HttpMethod.Get,
            $"/v1/conversations/{seeded.ConversationId}/messages?before=01H00000000000000000000000", seeded.TenantId, TenantRole.Viewer));
        Assert.Equal(HttpStatusCode.BadRequest, foreignCursor.StatusCode);
    }

    // ---------- helpers ----------

    private static string Texts(JsonDocument page) =>
        string.Join("|", page.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("text").GetString()));

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, string tenantId)
    {
        var response = await client.SendAsync(Request(HttpMethod.Get, path, tenantId, TenantRole.Viewer));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string tenantId, TenantRole role)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Auth", "yes");
        request.Headers.Add(RoleHeader, role.ToString());
        request.Headers.Add(TenantContextMiddleware.TenantHeaderName, tenantId);
        return request;
    }

    private static async Task<string> CsrfAsync(HttpClient client, string tenantId, TenantRole role)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/csrf");
        request.Headers.Add("X-Test-Auth", "yes");
        request.Headers.Add(RoleHeader, role.ToString());
        request.Headers.Add(TenantContextMiddleware.TenantHeaderName, tenantId);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private async Task MigrateAsync()
    {
        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        await db.Database.MigrateAsync();
    }

    private sealed record Seeded(string TenantId, string ConversationId);

    /// <summary>Seeds one tenant with an Instagram connection and conversations directly through the domain model.</summary>
    private async Task<Seeded> SeedAsync(string prefix, int messages, string? lastText = null, int extraConversations = 0)
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner));
        var connection = ChannelConnection.Create(tenant.Id, ChannelType.Instagram, "igid_" + Guid.NewGuid().ToString("N")[..12], "Inbox IG");
        db.ChannelConnections.Add(connection);

        var baseTime = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        for (var c = 0; c < extraConversations; c++)
        {
            AddConversation(db, tenant.Id, connection.Id, $"igsid_extra_{c}_{Guid.NewGuid():N}"[..24], 1, baseTime.AddDays(-1 - c), null);
        }

        var conversationId = AddConversation(db, tenant.Id, connection.Id, "igsid_main_" + Guid.NewGuid().ToString("N")[..8], messages, baseTime, lastText);
        await db.SaveChangesAsync();
        return new Seeded(tenant.Id, conversationId);
    }

    private static string AddConversation(AppDbContext db, string tenantId, string connectionId, string externalUserId, int messages, DateTimeOffset start, string? lastText)
    {
        var identity = CustomerChannelIdentity.Create(tenantId, connectionId, ChannelType.Instagram, externalUserId, start);
        var conversation = Conversation.Start(tenantId, connectionId, null, identity.Id, ChannelType.Instagram);
        db.CustomerChannelIdentities.Add(identity);
        db.Conversations.Add(conversation);
        for (var i = 0; i < messages; i++)
        {
            var at = start.AddMinutes(i);
            var text = i == messages - 1 && lastText != null ? lastText : $"m{i}";
            db.Messages.Add(Message.CreateInboundText(tenantId, conversation.Id, connectionId, null, $"mid_{Guid.NewGuid():N}", text, at, at));
            conversation.RecordInboundMessage(at);
        }

        return conversation.Id;
    }

    private sealed class InboxFactory(string connectionString) : WebApplicationFactory<Kreyora.WebApi.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:ConnectionString", connectionString);
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
                ["Email:Smtp:ApplicationPublicUrl"] = "https://seller.kreyora.test"
            }));
            builder.ConfigureServices(services =>
            {
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
            });
        }
    }

    /// <summary>Simulates verified membership: the test chooses the caller's role per request.</summary>
    private sealed class HeaderRoleResolver(IHttpContextAccessor httpContextAccessor) : ITenantContextResolutionService
    {
        public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);

        public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers[RoleHeader].ToString();
            return Task.FromResult<TenantContext?>(Enum.TryParse<TenantRole>(header, out var role)
                ? new TenantContext(tenantId, userId, "membership", role)
                : null);
        }

        public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantContext?>(null);

        public Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantContext?>(null);
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "InboxTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Request.Headers.ContainsKey("X-Test-Auth")
                ? Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "01H00000000000000000000004")], SchemeName)),
                    SchemeName)))
                : Task.FromResult(AuthenticateResult.NoResult());
    }
}
