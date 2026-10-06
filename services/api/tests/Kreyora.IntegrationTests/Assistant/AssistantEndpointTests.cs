using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kreyora.Application.Assistant;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Storefront;
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

namespace Kreyora.IntegrationTests.Assistant;

/// <summary>
/// M09-S02 over real HTTP and PostgreSQL: policy defaults/validation/concurrency/audit, the knowledge lifecycle
/// (submit, upload, import, approve, reject, supersede, delete), the approved-only query, roles, cross-tenant
/// isolation, storage purge and readiness.
/// </summary>
public sealed class AssistantEndpointTests : IClassFixture<PostgresFixture>
{
    private const string RoleHeader = "X-Test-Role";
    private readonly PostgresFixture fixture;

    public AssistantEndpointTests(PostgresFixture fixture) => this.fixture = fixture;

    // ---- policy ----

    [Fact]
    public async Task Unauthenticated_IsRejected()
    {
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/assistant/policy")).StatusCode);
    }

    [Fact]
    public async Task Policy_DefaultsAreSafe_ViewerReads_OnlyOwnerAdminWrite_AndChangesAreAuditedWithoutValues()
    {
        var tenant = await SeedTenantAsync("s02-policy");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        var policy = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/policy", tenant, TenantRole.Viewer);
        Assert.True(policy["enabled"]!.GetValue<bool>());
        Assert.Null(policy["reviewedAt"]?.GetValue<string?>());
        Assert.Equal("askForDetails", policy["unrecognizedMediaBehavior"]!.GetValue<string>());
        Assert.DoesNotContain(policy["allowedTools"]!.AsArray().Select(t => t!.GetValue<string>()), AssistantPolicy.WriteTools.Contains);
        Assert.Contains("refund_or_exchange", policy["fixedEscalationCategories"]!.AsArray().Select(t => t!.GetValue<string>()));

        policy["brandNote"] = "Secret brand voice: always say hajur";
        policy["replyStyle"] = "alwaysRomanized";
        foreach (var role in new[] { TenantRole.Viewer, TenantRole.Operator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Put, "/v1/assistant/policy", tenant, role, policy)).StatusCode);
        }

        var saved = await SendAsync(client, HttpMethod.Put, "/v1/assistant/policy", tenant, TenantRole.Admin, policy);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = JsonNode.Parse(await saved.Content.ReadAsStringAsync())!;
        Assert.Equal("alwaysRomanized", body["replyStyle"]!.GetValue<string>());
        Assert.NotNull(body["reviewedAt"]?.GetValue<string>());

        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        var audit = await db.AuditEvents.IgnoreQueryFilters().SingleAsync(a => a.TenantId == tenant && a.Action == "assistant.policy.updated");
        Assert.Contains("brandNote", audit.Metadata);
        Assert.Contains("replyStyle", audit.Metadata);
        Assert.DoesNotContain("hajur", audit.Metadata, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Policy_StaleVersion_Is409_InvalidValues_Are400WithFieldErrors_MissingCsrf_Is400()
    {
        var tenant = await SeedTenantAsync("s02-policy-invalid");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var policy = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/policy", tenant, TenantRole.Owner);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, "/v1/assistant/policy", tenant, TenantRole.Owner, policy)).StatusCode);
        var stale = await SendAsync(client, HttpMethod.Put, "/v1/assistant/policy", tenant, TenantRole.Owner, policy); // old version
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("assistant_policy_changed", await stale.Content.ReadAsStringAsync());

        var fresh = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/policy", tenant, TenantRole.Owner);
        fresh["allowedTools"] = new JsonArray("CreateCheckoutLink");
        fresh["maxToolSteps"] = 99;
        fresh["supportedLanguages"] = new JsonArray("fr");
        var invalid = await SendAsync(client, HttpMethod.Put, "/v1/assistant/policy", tenant, TenantRole.Owner, fresh);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = JsonNode.Parse(await invalid.Content.ReadAsStringAsync())!;
        Assert.Equal("urn:kreyora:problem:assistant_policy_invalid", problem["type"]!.GetValue<string>());
        var errors = problem["errors"]!.AsObject().Select(e => e.Key).ToList();
        Assert.Contains("allowedTools", errors);
        Assert.Contains("maxToolSteps", errors);
        Assert.Contains("supportedLanguages", errors);

        var noCsrf = Request(HttpMethod.Put, "/v1/assistant/policy", tenant, TenantRole.Owner);
        noCsrf.Content = JsonContent.Create(fresh);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noCsrf)).StatusCode);
    }

    // ---- knowledge lifecycle ----

    [Fact]
    public async Task Knowledge_SubmitApproveSupersedeReject_OnlyTheActiveVersionIsEverRetrievable()
    {
        var tenant = await SeedTenantAsync("s02-lifecycle");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        var created = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge", tenant, TenantRole.Owner,
            new { title = "Delivery FAQ", category = "delivery", text = "Kathmandu delivery takes 1-2 days. Fee NPR 100." });
        var documentId = created["id"]!.GetValue<string>();
        var v1 = created["pendingVersions"]![0]!["id"]!.GetValue<string>();
        Assert.Null(created["activeVersion"]);
        Assert.Empty(await ActiveAsync(factory, tenant)); // pending content is never retrievable

        var detail = await JsonAsync(client, HttpMethod.Get, $"/v1/assistant/knowledge/{documentId}/versions/{v1}", tenant, TenantRole.Viewer);
        Assert.Contains("1-2 days", detail["text"]!.GetValue<string>());

        await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions/{v1}/approve", tenant, TenantRole.Owner);
        Assert.Contains("1-2 days", Assert.Single(await ActiveAsync(factory, tenant)).Text);

        var withV2 = await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions", tenant, TenantRole.Admin,
            new { text = "Kathmandu delivery takes 1 day now. Fee NPR 100." });
        var v2 = withV2["pendingVersions"]![0]!["id"]!.GetValue<string>();
        Assert.Contains("1-2 days", Assert.Single(await ActiveAsync(factory, tenant)).Text); // v1 keeps serving until v2 is approved

        var approvedV2 = await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions/{v2}/approve", tenant, TenantRole.Owner);
        Assert.Equal(v2, approvedV2["activeVersion"]!["id"]!.GetValue<string>());
        Assert.Contains("1 day now", Assert.Single(await ActiveAsync(factory, tenant)).Text);

        var withV3 = await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions", tenant, TenantRole.Owner,
            new { text = "Wrong information: free delivery everywhere!" });
        var v3 = withV3["pendingVersions"]![0]!["id"]!.GetValue<string>();
        await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions/{v3}/reject", tenant, TenantRole.Owner, new { note = "Not true" });
        Assert.Contains("1 day now", Assert.Single(await ActiveAsync(factory, tenant)).Text);

        foreach (var (versionId, action) in new[] { (v3, "approve"), (v1, "approve"), (v2, "reject") })
        {
            var refused = await SendAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions/{versionId}/{action}", tenant, TenantRole.Owner, new { note = "x" });
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains("knowledge_invalid_transition", await refused.Content.ReadAsStringAsync());
        }

        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        var states = await db.KnowledgeDocumentVersions.IgnoreQueryFilters().Where(v => v.DocumentId == documentId).OrderBy(v => v.VersionNumber).Select(v => v.State).ToListAsync();
        Assert.Equal([KnowledgeVersionState.Superseded, KnowledgeVersionState.Active, KnowledgeVersionState.Rejected], states);
        var actions = await db.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == tenant && a.TargetId == documentId).Select(a => a.Action).ToListAsync();
        Assert.Equal(3, actions.Count(a => a == "assistant.knowledge.submitted"));
        Assert.Equal(2, actions.Count(a => a == "assistant.knowledge.approved"));
        Assert.Single(actions, a => a == "assistant.knowledge.rejected");
        Assert.DoesNotContain(await db.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == tenant).Select(a => a.Metadata).ToListAsync(),
            m => m != null && m.Contains("delivery", StringComparison.OrdinalIgnoreCase) && m.Contains("days", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConcurrentApprovals_OfTwoPendingVersions_OnlyOneWins()
    {
        var tenant = await SeedTenantAsync("s02-race");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var created = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge", tenant, TenantRole.Owner,
            new { title = "Returns", category = "returns", text = "Returns within 7 days." });
        var documentId = created["id"]!.GetValue<string>();
        var a = created["pendingVersions"]![0]!["id"]!.GetValue<string>();
        var b = (await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions", tenant, TenantRole.Owner,
            new { text = "Returns within 14 days." }))["pendingVersions"]!.AsArray().Last()!["id"]!.GetValue<string>();

        var tokenA = await CsrfAsync(client, tenant, TenantRole.Owner);
        var tokenB = await CsrfAsync(client, tenant, TenantRole.Owner);
        var results = await Task.WhenAll(
            client.SendAsync(Post($"/v1/assistant/knowledge/{documentId}/versions/{a}/approve", tenant, tokenA)),
            client.SendAsync(Post($"/v1/assistant/knowledge/{documentId}/versions/{b}/approve", tenant, tokenB)));

        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        Assert.Equal(1, await db.KnowledgeDocumentVersions.IgnoreQueryFilters().CountAsync(v => v.DocumentId == documentId && v.State == KnowledgeVersionState.Active));
        var document = await db.KnowledgeDocuments.IgnoreQueryFilters().SingleAsync(d => d.Id == documentId);
        var active = await db.KnowledgeDocumentVersions.IgnoreQueryFilters().SingleAsync(v => v.DocumentId == documentId && v.State == KnowledgeVersionState.Active);
        Assert.Equal(active.Id, document.ActiveVersionId);
    }

    // ---- uploads, idempotency, deletion ----

    [Fact]
    public async Task Upload_AcceptsUtf8TextAndMarkdown_RefusesPdfWordBinaryAndOversize_AndStoresOriginalsPrivately()
    {
        var tenant = await SeedTenantAsync("s02-upload");
        var storage = new InMemoryStorage();
        await using var factory = await FactoryAsync(storage: storage);
        using var client = factory.CreateClient();

        var ok = await UploadAsync(client, tenant, "delivery.md", Encoding.UTF8.GetBytes("# Delivery\nPokhara 2-3 days"), "delivery");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var created = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!;
        Assert.Equal("delivery.md", created["pendingVersions"]![0]!["originalFileName"]!.GetValue<string>());
        var key = Assert.Single(storage.Objects.Keys);
        Assert.StartsWith($"tenants/{tenant}/assistant-knowledge/", key);

        foreach (var (name, bytes, code) in new (string, byte[], string)[]
        {
            ("faq.pdf", Encoding.ASCII.GetBytes("%PDF-1.7 fake"), "knowledge_unsupported_type"),
            ("faq.docx", [0x50, 0x4B, 0x03, 0x04, 0x01], "knowledge_unsupported_type"),
            ("faq.txt", [0x48, 0xC3, 0x28], "knowledge_invalid_encoding"),
            ("faq.txt", Encoding.UTF8.GetBytes(new string('a', KnowledgeText.MaxUploadBytes + 1)), "knowledge_too_large"),
        })
        {
            var refused = await UploadAsync(client, tenant, name, bytes, "faq");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains(code, await refused.Content.ReadAsStringAsync());
        }

        Assert.Single(storage.Objects); // refused uploads never reach storage
    }

    [Fact]
    public async Task RetriedSubmission_WithTheSameIdempotencyKey_CreatesOneVersion()
    {
        var tenant = await SeedTenantAsync("s02-idem");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var body = new { title = "Payment", category = "payment", text = "COD in Kathmandu and Pokhara." };

        var first = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge", tenant, TenantRole.Owner, body, idempotencyKey: "kn-1");
        var second = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge", tenant, TenantRole.Owner, body, idempotencyKey: "kn-1");

        Assert.Equal(first["id"]!.GetValue<string>(), second["id"]!.GetValue<string>());
        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        Assert.Equal(1, await db.KnowledgeDocumentVersions.IgnoreQueryFilters().CountAsync(v => v.TenantId == tenant));
    }

    [Fact]
    public async Task Delete_PurgesOriginals_ClearsText_AndHidesEverything()
    {
        var tenant = await SeedTenantAsync("s02-delete");
        var storage = new InMemoryStorage();
        await using var factory = await FactoryAsync(storage: storage);
        using var client = factory.CreateClient();
        var uploaded = JsonNode.Parse(await (await UploadAsync(client, tenant, "faq.txt", Encoding.UTF8.GetBytes("Shop opens at 9."), "faq")).Content.ReadAsStringAsync())!;
        var documentId = uploaded["id"]!.GetValue<string>();
        var versionId = uploaded["pendingVersions"]![0]!["id"]!.GetValue<string>();
        await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions/{versionId}/approve", tenant, TenantRole.Owner);
        Assert.Single(await ActiveAsync(factory, tenant));

        var deleted = await SendAsync(client, HttpMethod.Delete, $"/v1/assistant/knowledge/{documentId}", tenant, TenantRole.Owner);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Empty(storage.Objects);
        Assert.Empty(await ActiveAsync(factory, tenant));
        Assert.Empty((await JsonAsync(client, HttpMethod.Get, "/v1/assistant/knowledge", tenant, TenantRole.Owner)).AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/v1/assistant/knowledge/{documentId}/versions/{versionId}", tenant, TenantRole.Owner)).StatusCode);
        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        var version = await db.KnowledgeDocumentVersions.IgnoreQueryFilters().SingleAsync(v => v.Id == versionId);
        Assert.Equal(KnowledgeVersionState.Deleted, version.State);
        Assert.Null(version.ContentText);
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenant && a.Action == "assistant.knowledge.deleted"));
    }

    // ---- isolation and roles ----

    [Fact]
    public async Task OtherWorkspaces_CannotSee_ApproveOrDelete_AndRetrievalIsTenantScoped()
    {
        var victim = await SeedTenantAsync("s02-victim");
        var attacker = await SeedTenantAsync("s02-attacker");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var created = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge", victim, TenantRole.Owner,
            new { title = "Private FAQ", category = "faq", text = "Victim shop secret info." });
        var documentId = created["id"]!.GetValue<string>();
        var versionId = created["pendingVersions"]![0]!["id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions/{versionId}/approve", attacker, TenantRole.Owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/v1/assistant/knowledge/{documentId}/versions/{versionId}", attacker, TenantRole.Owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Delete, $"/v1/assistant/knowledge/{documentId}", attacker, TenantRole.Owner)).StatusCode);
        Assert.DoesNotContain("Private FAQ", (await JsonAsync(client, HttpMethod.Get, "/v1/assistant/knowledge", attacker, TenantRole.Owner)).ToJsonString());

        await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{documentId}/versions/{versionId}/approve", victim, TenantRole.Owner);
        Assert.Single(await ActiveAsync(factory, victim));
        Assert.Empty(await ActiveAsync(factory, attacker));
    }

    [Fact]
    public async Task Operators_AndViewers_ReadOnly()
    {
        var tenant = await SeedTenantAsync("s02-roles");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var body = new { title = "FAQ", category = "faq", text = "Hello" };

        foreach (var role in new[] { TenantRole.Operator, TenantRole.Viewer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/v1/assistant/knowledge", tenant, role, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/v1/assistant/knowledge/import-store-policies", tenant, role)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/v1/assistant/knowledge", tenant, role)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/v1/assistant/readiness", tenant, role)).StatusCode);
        }
    }

    // ---- store policy import ----

    [Fact]
    public async Task StorePolicyImport_CreatesDraftsForReview_SkipsUnchanged_AndVersionsChanges()
    {
        var tenant = await SeedTenantAsync("s02-import", store: true);
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        var imported = (await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge/import-store-policies", tenant, TenantRole.Owner)).AsArray();
        Assert.Equal(2, imported.Count); // returns + payment (terms empty)
        Assert.All(imported, d => Assert.Null(d!["activeVersion"]));
        Assert.Empty(await ActiveAsync(factory, tenant)); // never auto-approved

        Assert.Empty((await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge/import-store-policies", tenant, TenantRole.Owner)).AsArray());

        var accessor = new TenantContextAccessor();
        await using (var db = fixture.CreateDbContext(accessor))
        {
            using var scope = accessor.BeginScope(new TenantContext(tenant, "u", "m", TenantRole.Owner));
            var store = await db.Stores.SingleAsync(s => s.TenantId == tenant);
            store.UpdateSettings(Settings(tenant, returns: "Returns within 10 days, unused items only."));
            await db.SaveChangesAsync();
        }

        var reimported = (await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge/import-store-policies", tenant, TenantRole.Owner)).AsArray();
        var returns = Assert.Single(reimported);
        Assert.Equal("returns", returns!["storePolicyKind"]!.GetValue<string>());
        Assert.Equal(2, returns["pendingVersions"]!.AsArray().Count);
    }

    // ---- readiness ----

    [Fact]
    public async Task Readiness_ReportsEachCheck_AndStaysInactiveUntilEverythingRequiredPasses()
    {
        var tenant = await SeedTenantAsync("s02-readiness");
        await using var factory = await FactoryAsync(aiEnabled: true);
        using var client = factory.CreateClient();

        var before = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/readiness", tenant, TenantRole.Viewer);
        Assert.False(before["isActive"]!.GetValue<bool>());
        Assert.True(before["platformEnabled"]!.GetValue<bool>());
        Assert.False(Check(before, "store_ready"));
        Assert.Contains("store_missing", before["checks"]!.AsArray().Single(c => c!["code"]!.GetValue<string>() == "store_ready")!["blockers"]!.ToJsonString());
        Assert.False(Check(before, "channel_connected"));
        Assert.False(Check(before, "policy_reviewed"));
        Assert.False(Check(before, "knowledge_approved"));

        var accessor = new TenantContextAccessor();
        await using (var db = fixture.CreateDbContext(accessor))
        {
            using var scope = accessor.BeginScope(new TenantContext(tenant, "u", "m", TenantRole.Owner));
            db.ChannelConnections.Add(ChannelConnection.Create(tenant, ChannelType.Instagram, "igid_" + Guid.NewGuid().ToString("N")[..12], "IG"));
            await db.SaveChangesAsync();
        }

        var policy = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/policy", tenant, TenantRole.Owner);
        await JsonAsync(client, HttpMethod.Put, "/v1/assistant/policy", tenant, TenantRole.Owner, policy);
        var created = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/knowledge", tenant, TenantRole.Owner, new { title = "FAQ", category = "faq", text = "Open 9-7." });
        await JsonAsync(client, HttpMethod.Post, $"/v1/assistant/knowledge/{created["id"]}/versions/{created["pendingVersions"]![0]!["id"]}/approve", tenant, TenantRole.Owner);

        var after = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/readiness", tenant, TenantRole.Viewer);
        Assert.True(Check(after, "channel_connected"));
        Assert.True(Check(after, "policy_reviewed"));
        Assert.True(Check(after, "knowledge_approved"));
        Assert.False(Check(after, "store_ready"));
        Assert.False(after["isActive"]!.GetValue<bool>()); // the store is still not ready to accept orders
    }

    // ---- helpers ----

    private static bool Check(JsonNode readiness, string code) =>
        readiness["checks"]!.AsArray().Single(c => c!["code"]!.GetValue<string>() == code)!["passed"]!.GetValue<bool>();

    private static async Task<IReadOnlyList<ApprovedKnowledgeItem>> ActiveAsync(AssistantFactory factory, string tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        using var tenantScope = accessor.BeginScope(new TenantContext(tenantId, "u", "m", TenantRole.Owner));
        return await scope.ServiceProvider.GetRequiredService<IApprovedKnowledgeQuery>().GetActiveAsync();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string tenant, string fileName, byte[] bytes, string category)
    {
        var request = Request(HttpMethod.Post, "/v1/assistant/knowledge/upload", tenant, TenantRole.Owner);
        request.Headers.Add("X-CSRF-Token", await CsrfAsync(client, tenant, TenantRole.Owner));
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(category), "category");
        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task<JsonNode> JsonAsync(HttpClient client, HttpMethod method, string path, string tenant, TenantRole role, object? body = null, string? idempotencyKey = null)
    {
        var response = await SendAsync(client, method, path, tenant, role, body, idempotencyKey);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} → {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string tenant, TenantRole role, object? body = null, string? idempotencyKey = null)
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

    private static HttpRequestMessage Post(string path, string tenant, string csrf)
    {
        var request = Request(HttpMethod.Post, path, tenant, TenantRole.Owner);
        request.Headers.Add("X-CSRF-Token", csrf);
        return request;
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
        var response = await client.SendAsync(Request(HttpMethod.Get, "/v1/auth/csrf", tenantId, role));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static StoreSettings Settings(string tenant, string? returns = "Returns within 7 days.") => new(
        "Demo Boutique", "demo-" + tenant[^8..].ToLowerInvariant(), null, StoreThemePreset.Default, null, null, null, null, null, null, null, null,
        TermsPolicy: null, PrivacyPolicy: null, ReturnsPolicy: returns, PaymentPolicy: "COD in Kathmandu and Pokhara; QR elsewhere.");

    private async Task<string> SeedTenantAsync(string prefix, bool store = false)
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        if (store)
        {
            using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "u", "m", TenantRole.Owner));
            db.Stores.Add(Store.Create(tenant.Id, Settings(tenant.Id)));
            await db.SaveChangesAsync();
        }

        return tenant.Id;
    }

    private async Task<AssistantFactory> FactoryAsync(InMemoryStorage? storage = null, bool aiEnabled = false)
    {
        await using (var db = fixture.CreateDbContext(new TenantContextAccessor()))
        {
            await db.Database.MigrateAsync();
        }

        return new AssistantFactory(fixture.ConnectionString, storage ?? new InMemoryStorage(), aiEnabled);
    }

    private sealed class InMemoryStorage : IPrivateObjectStorage
    {
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

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
            Objects.TryRemove(objectKey, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class AssistantFactory(string connectionString, InMemoryStorage storage, bool aiEnabled) : WebApplicationFactory<Kreyora.WebApi.Program>
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
                ["Email:Smtp:ApplicationPublicUrl"] = "https://seller.kreyora.test",
                ["Ai:Enabled"] = aiEnabled ? "true" : "false"
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
            });
        }
    }

    private sealed class HeaderRoleResolver(IHttpContextAccessor httpContextAccessor) : ITenantContextResolutionService
    {
        public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);

        public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers[RoleHeader].ToString();
            return Task.FromResult<TenantContext?>(Enum.TryParse<TenantRole>(header, out var role) ? new TenantContext(tenantId, userId, "membership", role) : null);
        }

        public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);

        public Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
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
