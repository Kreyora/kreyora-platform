using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Conversations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kreyora.IntegrationTests.Assistant.AssistantHttp;
using static Kreyora.IntegrationTests.Assistant.AssistantShopSeed;

namespace Kreyora.IntegrationTests.Assistant;

/// <summary>
/// M09-S08 over real PostgreSQL: the usage summary (aggregates only, tenant-scoped) and customer names (Q8) — looked up
/// after a real signed webhook commits, staff-only, never sent to the assistant, throttled, erased with the identity.
/// </summary>
public sealed class UsageAndProfileTests : IClassFixture<PostgresFixture>
{
    private const string AppSecret = "s08_test_app_secret";
    private readonly PostgresFixture fixture;

    public UsageAndProfileTests(PostgresFixture fixture) => this.fixture = fixture;

    // ---- usage ----

    [Fact]
    public async Task Usage_AggregatesByUtcDay_SeparatesThePlayground_AndIsTenantScoped()
    {
        var shop = await SeedShopAsync(fixture, "s08-usage");
        var other = await SeedShopAsync(fixture, "s08-usage-other");
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero).AddHours(1);
        await AddTurnAsync(shop, today, AssistantTurnOutcome.Replied, calls: 3, cost: 120);
        await AddTurnAsync(shop, today, AssistantTurnOutcome.Escalated, calls: 1, cost: 40);
        await AddTurnAsync(shop, today, AssistantTurnOutcome.Blocked, calls: 0, cost: 0);
        await AddTurnAsync(shop, today, AssistantTurnOutcome.Replied, calls: 2, cost: 80, playground: true);
        await AddTurnAsync(shop, today.AddDays(-1), AssistantTurnOutcome.Fallback, calls: 1, cost: 10);
        await AddTurnAsync(shop, today.AddDays(-10), AssistantTurnOutcome.Replied, calls: 1, cost: 10); // outside the window
        await AddTurnAsync(other, today, AssistantTurnOutcome.Replied, calls: 5, cost: 999);
        await using var factory = new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage(), aiEnabled: true);
        using var client = factory.CreateClient();

        var usage = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/usage?days=3", shop.TenantId, TenantRole.Viewer);

        var daily = usage["daily"]!.AsArray();
        Assert.Equal(3, daily.Count);
        var day = daily[^1]!;
        Assert.Equal(3, day["turns"]!.GetValue<int>()); // customer turns only
        Assert.Equal((1, 1, 1), (day["replied"]!.GetValue<int>(), day["handedOff"]!.GetValue<int>(), day["blocked"]!.GetValue<int>()));
        Assert.Equal(1, day["playgroundTurns"]!.GetValue<int>());
        Assert.Equal(6, day["modelCalls"]!.GetValue<int>()); // playground calls count against the budget too
        Assert.Equal(0.00024m, day["estimatedCostUsd"]!.GetValue<decimal>());
        Assert.Equal(1, daily[^2]!["handedOff"]!.GetValue<int>());
        Assert.Equal(3, usage["turnsToday"]!.GetValue<int>()); // turns that called the model, as the daily cap counts
        Assert.Equal(150, usage["dailyTurnCap"]!.GetValue<int>());
        Assert.True(usage["platformEnabled"]!.GetValue<bool>());
        Assert.DoesNotContain("999", usage.ToJsonString(), StringComparison.Ordinal);

        Assert.Equal(30, (await JsonAsync(client, HttpMethod.Get, "/v1/assistant/usage?days=99", shop.TenantId, TenantRole.Viewer))["daily"]!.AsArray().Count);
        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "/v1/assistant/usage");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(anonymous)).StatusCode);
    }

    // ---- customer names ----

    [Fact]
    public async Task ANewCustomer_GetsTheirNameAfterTheWebhookCommits_AndTheInboxShowsIt()
    {
        var shop = await SeedShopAsync(fixture, "s08-names");
        var graph = new ProfileGraph { Next = InstagramProfileResult.Found("Test Customer", "test.customer") };
        await using var rig = await RigAsync(shop, graph);
        var ids = await IdsAsync(shop);

        await WebhookAsync(rig, Body(ids, Mid(), "hello"));
        await WebhookAsync(rig, Body(ids, Mid(), "hello again")); // a second delivery before the lookup ran

        var lookups = rig.Jobs.Created.Where(j => j.Job.Type == typeof(CustomerProfileLookupJob)).ToList();
        Assert.Equal(2, lookups.Count);
        Assert.All(lookups, l => Assert.InRange((((ScheduledState)l.State).EnqueueAt - DateTime.UtcNow).TotalSeconds, 1, 6)); // the 5 s delay
        var job = rig.Host.Services.GetRequiredService<CustomerProfileLookupJob>();
        Assert.Equal("updated", await job.RunAsync(shop.TenantId, shop.IdentityId));
        Assert.Equal("not_due", await job.RunAsync(shop.TenantId, shop.IdentityId)); // the duplicate is a cheap no-op
        Assert.Equal("1234567890", Assert.Single(graph.Calls)); // one provider call, with the customer's scoped ID

        using var client = rig.Host.CreateClient();
        var list = await JsonAsync(client, HttpMethod.Get, "/v1/conversations", shop.TenantId, TenantRole.Viewer);
        var item = list["items"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == shop.ConversationId)!;
        Assert.Equal(("Test Customer", "test.customer"), (item["customerLabel"]!.GetValue<string>(), item["customerUsername"]!.GetValue<string>()));
        var detail = await JsonAsync(client, HttpMethod.Get, $"/v1/conversations/{shop.ConversationId}", shop.TenantId, TenantRole.Viewer);
        Assert.Equal("test.customer", detail["customer"]!["username"]!.GetValue<string>());

        rig.Jobs.Created.Clear();
        await WebhookAsync(rig, Body(ids, Mid(), "third message"));
        Assert.DoesNotContain(rig.Jobs.Created, j => j.Job.Type == typeof(CustomerProfileLookupJob)); // checked recently
    }

    [Fact]
    public async Task ARefusedLookup_KeepsTheMaskedLabel_AndATransientOneIsRetriedLater()
    {
        var shop = await SeedShopAsync(fixture, "s08-names-refused");
        var graph = new ProfileGraph { Next = InstagramProfileResult.Failed(InstagramValidationKind.Throttled, "613") };
        await using var rig = await RigAsync(shop, graph);
        var job = rig.Host.Services.GetRequiredService<CustomerProfileLookupJob>();

        Assert.Equal("provider_transient", await job.RunAsync(shop.TenantId, shop.IdentityId));
        Assert.Null((await IdentityAsync(shop)).ProfileCheckedAt); // not stamped: the next message tries again

        graph.Next = InstagramProfileResult.Failed(InstagramValidationKind.PermissionDenied, "10");
        Assert.Equal("provider_permissiondenied", await job.RunAsync(shop.TenantId, shop.IdentityId));
        var identity = await IdentityAsync(shop);
        Assert.NotNull(identity.ProfileCheckedAt);
        Assert.Null(identity.DisplayName);
        Assert.Equal("not_due", await job.RunAsync(shop.TenantId, shop.IdentityId));

        using var client = rig.Host.CreateClient();
        var detail = await JsonAsync(client, HttpMethod.Get, $"/v1/conversations/{shop.ConversationId}", shop.TenantId, TenantRole.Viewer);
        Assert.StartsWith("Instagram user ·", detail["customer"]!["customerLabel"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lookups_AreOffByDefault_SkipErasedCustomers_AndStayInTheirTenant()
    {
        var shop = await SeedShopAsync(fixture, "s08-names-guards");
        var other = await SeedShopAsync(fixture, "s08-names-guards-b");
        var graph = new ProfileGraph { Next = InstagramProfileResult.Found("Test Customer", "test.customer") };

        await using (var off = await RigAsync(shop, graph, enabled: false))
        {
            await WebhookAsync(off, Body(await IdsAsync(shop), Mid(), "hello"));
            Assert.DoesNotContain(off.Jobs.Created, j => j.Job.Type == typeof(CustomerProfileLookupJob));
            Assert.Equal("disabled", await off.Host.Services.GetRequiredService<CustomerProfileLookupJob>().RunAsync(shop.TenantId, shop.IdentityId));
        }

        await using var rig = await RigAsync(shop, graph);
        var job = rig.Host.Services.GetRequiredService<CustomerProfileLookupJob>();
        Assert.Equal("not_found", await job.RunAsync(shop.TenantId, other.IdentityId)); // another tenant's customer
        await UpdateAsync(shop, db => db.CustomerChannelIdentities.Where(i => i.Id == shop.IdentityId).ExecuteUpdateAsync(s => s.SetProperty(i => i.ErasedAt, DateTimeOffset.UtcNow)));
        Assert.Equal("erased", await job.RunAsync(shop.TenantId, shop.IdentityId));
        Assert.Empty(graph.Calls);
    }

    [Fact]
    public async Task CustomerNames_AreNeverSentToTheAssistantModel()
    {
        var shop = await SeedShopAsync(fixture, "s08-names-ai");
        await using (var db = TenantDb(shop.TenantId, out var scope))
        using (scope)
        {
            db.AssistantPolicies.Add(AssistantPolicy.CreateDefault(shop.TenantId));
            await db.SaveChangesAsync();
            await db.AssistantPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.ReviewedAt, DateTimeOffset.UtcNow));
            (await db.CustomerChannelIdentities.SingleAsync(i => i.Id == shop.IdentityId)).ApplyProfile("Zebulon Quartzfield", "zq.unique.handle", DateTimeOffset.UtcNow);
            var now = DateTimeOffset.UtcNow;
            db.Messages.Add(Message.CreateInboundText(shop.TenantId, shop.ConversationId, shop.ConnectionId, null, Mid(), "red kurta kati ho?", now, now));
            (await db.Conversations.SingleAsync(c => c.Id == shop.ConversationId)).RecordInboundMessage(now);
            await db.SaveChangesAsync();
        }

        await using var factory = new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage(), aiEnabled: true, services =>
            services.PostConfigure<AiOptions>(o =>
            {
                o.DataPolicy.SyntheticTenantIds.Add(shop.TenantId);
                o.Entitlements.AllowedTenantIds.Add(shop.TenantId);
            }));
        var fake = factory.Services.GetRequiredService<FakeAiChatClient>();
        fake.Enqueue(Calls("SearchProducts", """{"query":"red kurta"}"""));
        fake.Enqueue(AiChatResult.Success("Red Cotton Kurta cha.", [], AiFinishReason.Stop, new AiUsage(100, 20), "fake", "fake-model", TimeSpan.FromMilliseconds(5)));
        await using var check = Db();
        var trigger = await check.Messages.IgnoreQueryFilters().Where(m => m.ConversationId == shop.ConversationId).Select(m => m.Id).SingleAsync();

        var result = await factory.AsSystemAsync(shop.TenantId, sp => sp.GetRequiredService<IAssistantTurnService>().RunAsync(shop.ConversationId, trigger));

        Assert.Equal(AssistantTurnOutcome.Replied, result.Outcome);
        var everything = string.Join("\n", fake.Requests.SelectMany(r => r.Messages).Select(m => m.Content));
        Assert.DoesNotContain("Zebulon", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zq.unique.handle", everything, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ----

    private async Task<Rig> RigAsync(Shop shop, ProfileGraph graph, bool enabled = true)
    {
        var jobs = new RecordingJobClient();
        var host = new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage(), aiEnabled: false, services =>
        {
            services.PostConfigure<InstagramWebhookOptions>(o => o.AppSecret = AppSecret);
            services.PostConfigure<InstagramMessagingOptions>(o => o.ProfileLookupEnabled = enabled);
            services.AddSingleton<IInstagramGraphClient>(graph);
            services.AddSingleton<IBackgroundJobClient>(jobs);
        });
        var encryption = host.Services.GetRequiredService<ISecretEncryptionService>();
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            (await db.ChannelConnections.SingleAsync(c => c.Id == shop.ConnectionId)).UpdateCredentials(encryption.Encrypt("page_token_for_tests"));
            // A numeric scoped ID, as Instagram sends.
            await db.CustomerChannelIdentities.Where(i => i.Id == shop.IdentityId).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExternalUserId, "1234567890"));
            await db.SaveChangesAsync();
        }

        return new Rig(host, jobs);
    }

    private static async Task WebhookAsync(Rig rig, string body)
    {
        WebhookIngressResult result;
        using (var scope = rig.Host.Services.CreateScope())
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
            result = await scope.ServiceProvider.GetRequiredService<IWebhookIngressService>().HandleWebhookAsync(new WebhookIngressCommand(
                ChannelType.Instagram, null, "POST", "/v1/webhooks/instagram",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = "application/json",
                    [InstagramChannelProvider.SignatureHeader] = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)))
                },
                new Dictionary<string, string>(), Encoding.UTF8.GetBytes(body), "application/json", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
        }

        Assert.True(result is { IsSuccess: true, EventId: not null }, result.ErrorReason);
        using var processing = rig.Host.Services.CreateScope();
        Assert.True((await processing.ServiceProvider.GetRequiredService<IWebhookProcessingService>().ProcessWebhookEventAsync(result.EventId!)).Succeeded);
    }

    private static string Mid() => $"mid_{Guid.NewGuid():N}";

    private static string Body((string Account, string Customer) ids, string mid, string text)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return JsonSerializer.Serialize(new
        {
            @object = "instagram",
            entry = new[] { new { id = ids.Account, time = now, messaging = new[] { new { sender = new { id = ids.Customer }, recipient = new { id = ids.Account }, timestamp = now, message = new { mid, text } } } } }
        });
    }

    private async Task<(string Account, string Customer)> IdsAsync(Shop shop)
    {
        await using var db = Db();
        return (await db.ChannelConnections.IgnoreQueryFilters().Where(c => c.Id == shop.ConnectionId).Select(c => c.ExternalAccountId).SingleAsync(),
            await db.CustomerChannelIdentities.IgnoreQueryFilters().Where(i => i.Id == shop.IdentityId).Select(i => i.ExternalUserId).SingleAsync());
    }

    private async Task<Kreyora.Domain.Customers.CustomerChannelIdentity> IdentityAsync(Shop shop)
    {
        await using var db = Db();
        return await db.CustomerChannelIdentities.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == shop.IdentityId);
    }

    private async Task AddTurnAsync(Shop shop, DateTimeOffset at, AssistantTurnOutcome outcome, int calls, long cost, bool playground = false)
    {
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            var turn = AssistantTurn.Start(shop.TenantId, playground ? null : shop.ConversationId, null, $"usage-{Guid.NewGuid():N}", playground, at);
            for (var i = 0; i < calls; i++) turn.RecordModelCall(new TurnModelCall("Primary", "fake", "fake-model", 5, 10, 10, "stop"), cost / calls);
            turn.Finish(outcome, outcome.ToString().ToLowerInvariant(), at.AddSeconds(2));
            db.AssistantTurns.Add(turn);
            await db.SaveChangesAsync();
        }
    }

    private async Task UpdateAsync(Shop shop, Func<AppDbContext, Task<int>> update)
    {
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            await update(db);
        }
    }

    private static AiChatResult Calls(string name, string arguments) =>
        AiChatResult.Success(null, [new AiToolCall($"call-{Guid.NewGuid():N}", name, arguments)], AiFinishReason.ToolCalls, new AiUsage(100, 20), "fake", "fake-model", TimeSpan.FromMilliseconds(5));

    private AppDbContext Db() => fixture.CreateDbContext(new TenantContextAccessor());

    private AppDbContext TenantDb(string tenantId, out IDisposable scope)
    {
        var accessor = new TenantContextAccessor();
        scope = accessor.BeginScope(new TenantContext(tenantId, "u", "m", TenantRole.Owner));
        return fixture.CreateDbContext(accessor);
    }

    private sealed class Rig(AssistantTestHost host, RecordingJobClient jobs) : IAsyncDisposable
    {
        public AssistantTestHost Host => host;

        public RecordingJobClient Jobs => jobs;

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }

    /// <summary>Records every Hangfire job instead of storing it, so the test runs the ones it cares about.</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public ConcurrentBag<(Job Job, IState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add((job, state));
            return Guid.NewGuid().ToString("N");
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class ProfileGraph : IInstagramGraphClient
    {
        public InstagramProfileResult Next { get; set; } = InstagramProfileResult.Failed(InstagramValidationKind.Unknown, null);

        public ConcurrentQueue<string> CallLog { get; } = new();

        public List<string> Calls => [.. CallLog];

        public Task<InstagramValidationResult> ValidatePageLinkAsync(string pageAccessToken, string pageId, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstagramValidationResult> ValidateAccountAsync(string pageAccessToken, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstagramSendResult> SendTextAsync(string pageAccessToken, string recipientId, string text, string? messagingTag = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(InstagramSendResult.Sent(Mid()));

        public Task<InstagramProfileResult> GetUserProfileAsync(string pageAccessToken, string scopedUserId, CancellationToken cancellationToken = default)
        {
            CallLog.Enqueue(scopedUserId);
            return Task.FromResult(Next);
        }
    }
}
