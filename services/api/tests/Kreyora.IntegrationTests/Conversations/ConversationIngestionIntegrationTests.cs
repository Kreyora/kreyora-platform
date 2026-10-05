using System.Security.Cryptography;
using System.Text;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Conversations;
using Kreyora.Application.Integrations;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Conversations;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Kreyora.IntegrationTests.Conversations;

/// <summary>
/// M08-S04: signed Instagram deliveries flow through real ingress → processing → conversation ingestion on
/// PostgreSQL (Testcontainers). Signed fixtures with test-only secrets; no real identifiers.
/// </summary>
public sealed class ConversationIngestionIntegrationTests : IClassFixture<PostgresFixture>
{
    private const string AppSecret = "s04_app_secret";

    private readonly PostgresFixture fixture;
    private readonly ITestOutputHelper output;

    public ConversationIngestionIntegrationTests(PostgresFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    [Fact]
    public async Task FirstDm_CreatesIdentityConversationAndMessage_WithStoreFromConnection()
    {
        var env = await ArrangeAsync("s04-first", withStore: true);

        await env.DeliverAsync(Body(env.Igid, Msg("mid_first", "igsid_c1", "Do you have size M?", 1000)));

        var identity = await env.Db.CustomerChannelIdentities.IgnoreQueryFilters().SingleAsync(i => i.ConnectionId == env.ConnectionId);
        var conversation = await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId);
        var message = await env.Db.Messages.IgnoreQueryFilters().SingleAsync(m => m.ConnectionId == env.ConnectionId);

        Assert.Equal("igsid_c1", identity.ExternalUserId);
        Assert.Equal(env.TenantId, conversation.TenantId);
        Assert.Equal(identity.Id, conversation.CustomerChannelIdentityId);
        Assert.Equal(ConversationStatus.New, conversation.Status);
        Assert.Equal(AutomationMode.Automated, conversation.AutomationMode);
        Assert.Equal(env.StoreId, conversation.StoreId);
        Assert.Equal(1, conversation.UnreadCount);
        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(MessageOrigin.Customer, message.Origin);
        Assert.Equal("Do you have size M?", message.Text);
        Assert.Equal("mid_first", message.ProviderMessageId);
        Assert.NotNull(message.InboundEventId);
    }

    [Fact]
    public async Task SecondDm_ReusesIdentityAndConversation_AndCountsUnread()
    {
        var env = await ArrangeAsync("s04-second");

        await env.DeliverAsync(Body(env.Igid, Msg("mid_a", "igsid_c2", "hello", 1000)));
        await env.DeliverAsync(Body(env.Igid, Msg("mid_b", "igsid_c2", "anyone?", 2000)));

        Assert.Equal(1, await env.Db.CustomerChannelIdentities.IgnoreQueryFilters().CountAsync(i => i.ConnectionId == env.ConnectionId));
        var conversation = await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId);
        Assert.Equal(2, conversation.UnreadCount);
        Assert.Equal(2, await env.Db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == conversation.Id));
    }

    [Fact]
    public async Task ExactAndRebatchedDuplicates_ProduceOneMessage()
    {
        var env = await ArrangeAsync("s04-dup");
        var item = Msg("mid_dup", "igsid_c3", "once", 1000);

        await env.DeliverAsync(Body(env.Igid, item, entryTime: 1));
        await env.DeliverAsync(Body(env.Igid, item, entryTime: 1));   // exact redelivery
        await env.DeliverAsync(Body(env.Igid, item, entryTime: 99));  // different wrapper bytes

        Assert.Equal(1, await env.Db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConnectionId == env.ConnectionId));
        Assert.Equal(1, (await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId)).UnreadCount);
    }

    [Fact]
    public async Task OutOfOrderMessages_TimelineFollowsProviderTime()
    {
        var env = await ArrangeAsync("s04-order");

        await env.DeliverAsync(Body(env.Igid, Msg("mid_later", "igsid_c4", "second", 5000)));
        await env.DeliverAsync(Body(env.Igid, Msg("mid_earlier", "igsid_c4", "first", 1000)));

        var conversation = await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId);
        var page = await env.QueryAsync(q => q.GetMessagesAsync(conversation.Id, null, 50));

        Assert.Equal("first|second", string.Join("|", page.Items.Select(m => m.Text)));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(5000), conversation.LastMessageAt);
    }

    [Fact]
    public async Task OutOfOrderReaction_NewerUnreactWinsOverLateOlderReact()
    {
        var env = await ArrangeAsync("s04-react");
        await env.DeliverAsync(Body(env.Igid, Msg("mid_c5", "igsid_c5", "hi", 1000)));

        await env.DeliverAsync(Body(env.Igid, Reaction("mid_c5", "igsid_c5", "unreact", 3000)));
        await env.DeliverAsync(Body(env.Igid, Reaction("mid_c5", "igsid_c5", "react", 2000)));

        var reaction = await env.Db.MessageReactions.IgnoreQueryFilters().SingleAsync(r => r.ConnectionId == env.ConnectionId);
        Assert.True(reaction.IsRemoved);

        var conversation = await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId);
        var page = await env.QueryAsync(q => q.GetMessagesAsync(conversation.Id, null, 50));
        Assert.Empty(Assert.Single(page.Items).Reactions);
    }

    [Fact]
    public async Task SeenReceipts_UpdateWatermark_AndAdvanceExistingOutboundMessageOnly()
    {
        var env = await ArrangeAsync("s04-seen");
        await env.DeliverAsync(Body(env.Igid, Msg("mid_c6", "igsid_c6", "hi", 1000)));
        var conversation = await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId);

        // Receipt for a message Kreyora does not know: watermark only.
        await env.DeliverAsync(Body(env.Igid, Seen("mid_unknown_out", "igsid_c6", 2000)));

        using (env.Accessor.BeginScope(OwnerContext(env.TenantId)))
        {
            env.Db.Messages.Add(Message.CreateOutboundText(env.TenantId, conversation.Id, env.ConnectionId, MessageOrigin.Staff,
                "mid_staff_reply", "Yes, size M is available", DateTimeOffset.FromUnixTimeMilliseconds(2500), DateTimeOffset.UtcNow));
            await env.Db.SaveChangesAsync();
        }

        await env.DeliverAsync(Body(env.Igid, Seen("mid_staff_reply", "igsid_c6", 4000)));

        env.Db.ChangeTracker.Clear();
        var refreshed = await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == conversation.Id);
        var reply = await env.Db.Messages.IgnoreQueryFilters().SingleAsync(m => m.ProviderMessageId == "mid_staff_reply");
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(4000), refreshed.CustomerLastReadAt);
        Assert.Equal(MessageDeliveryStatus.Read, reply.DeliveryStatus);
        Assert.Equal(1, refreshed.UnreadCount);
    }

    [Fact]
    public async Task ConcurrentFirstMessages_FromOneNewCustomer_YieldOneIdentityAndConversation()
    {
        var env = await ArrangeAsync("s04-race");
        var eventA = await env.IngestOnlyAsync(Body(env.Igid, Msg("mid_race_a", "igsid_race", "one", 1000)));
        var eventB = await env.IngestOnlyAsync(Body(env.Igid, Msg("mid_race_b", "igsid_race", "two", 1001)));

        var outcomes = await Task.WhenAll(ProcessInFreshContextAsync(eventA), ProcessInFreshContextAsync(eventB));
        output.WriteLine($"first pass: {string.Join(", ", outcomes)}");

        // Simulate the job retry for any attempt that lost the race.
        foreach (var eventId in new[] { eventA, eventB })
        {
            var outcome = await ProcessInFreshContextAsync(eventId);
            output.WriteLine($"retry {eventId[..6]}: {outcome}");
        }

        await using var check = fixture.CreateDbContext(new TenantContextAccessor());
        Assert.Equal(1, await check.CustomerChannelIdentities.IgnoreQueryFilters().CountAsync(i => i.ConnectionId == env.ConnectionId));
        var conversation = await check.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId);
        Assert.Equal(2, await check.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == conversation.Id));
        Assert.Equal(2, conversation.UnreadCount);
        Assert.All(await check.WebhookEvents.IgnoreQueryFilters().Where(e => e.ConnectionId == env.ConnectionId).ToListAsync(),
            e => Assert.Equal(WebhookProcessingStatus.Processed, e.ProcessingStatus));
    }

    [Theory]
    [InlineData("Resolved", ConversationStatus.New, 1)]
    [InlineData("Closed", ConversationStatus.New, 1)]
    [InlineData("Spam", ConversationStatus.Spam, 0)]
    public async Task InboundOnExistingConversation_FollowsAdr016StatusRules(string current, ConversationStatus expected, int unread)
    {
        var env = await ArrangeAsync("s04-status");
        await env.DeliverAsync(Body(env.Igid, Msg("mid_s1", "igsid_s", "hi", 1000)));
        var conversationId = (await env.Db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.ConnectionId == env.ConnectionId)).Id;
        await env.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE conversations SET status = {current}, unread_count = 0 WHERE id = {conversationId}");
        env.Db.ChangeTracker.Clear();

        await env.DeliverAsync(Body(env.Igid, Msg("mid_s2", "igsid_s", "again", 2000)));

        var conversation = await env.Db.Conversations.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == conversationId);
        Assert.Equal(expected, conversation.Status);
        Assert.Equal(unread, conversation.UnreadCount);
        Assert.Equal(2, await env.Db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task SameCustomerIdOnTwoTenantsConnections_StaysSeparate()
    {
        var envA = await ArrangeAsync("s04-iso-a");
        var envB = await ArrangeAsync("s04-iso-b");

        await envA.DeliverAsync(Body(envA.Igid, Msg("mid_iso_a", "igsid_shared", "to A", 1000)));
        await envB.DeliverAsync(Body(envB.Igid, Msg("mid_iso_b", "igsid_shared", "to B", 1000)));

        var identities = await envA.Db.CustomerChannelIdentities.IgnoreQueryFilters()
            .Where(i => i.ExternalUserId == "igsid_shared" && (i.TenantId == envA.TenantId || i.TenantId == envB.TenantId))
            .ToListAsync();
        Assert.Equal(2, identities.Select(i => i.TenantId).Distinct().Count());

        var conversationsA = await envA.QueryAsync(q => q.ListConversationsAsync(new ConversationQuery()));
        Assert.Single(conversationsA.Items);
        Assert.Equal("to A", conversationsA.Items[0].LastMessagePreview);
    }

    [Fact]
    public async Task IngestionFailure_RollsBackInboundEventAndConversationRows_AndLeavesEventRetryable()
    {
        var env = await ArrangeAsync("s04-atomic");
        var eventId = await env.IngestOnlyAsync(Body(env.Igid, Msg("mid_atomic", "igsid_atomic", "x", 1000)));

        var processing = env.Processing(new ThrowingIngestion());
        var result = await processing.ProcessWebhookEventAsync(eventId);

        await using var check = fixture.CreateDbContext(new TenantContextAccessor());
        Assert.False(result.Succeeded);
        Assert.Equal(WebhookProcessingStatus.Failed,
            (await check.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == eventId)).ProcessingStatus);
        Assert.Equal(0, await check.InboundEvents.IgnoreQueryFilters().CountAsync(i => i.ConnectionId == env.ConnectionId));
        Assert.Equal(0, await check.Conversations.IgnoreQueryFilters().CountAsync(c => c.ConnectionId == env.ConnectionId));
        Assert.Equal(0, await check.CustomerChannelIdentities.IgnoreQueryFilters().CountAsync(i => i.ConnectionId == env.ConnectionId));

        // The retry with the real ingestion succeeds and nothing was half-written before.
        env.Db.ChangeTracker.Clear();
        Assert.True((await env.Processing().ProcessWebhookEventAsync(eventId)).Succeeded);
        Assert.Equal(1, await check.Messages.IgnoreQueryFilters().CountAsync(m => m.ConnectionId == env.ConnectionId));
    }

    [Fact]
    public async Task OwnerErasure_RedactsContentRemovesReactionsAudits_AndIsIdempotent_OperatorForbidden()
    {
        var env = await ArrangeAsync("s04-erase");
        await env.DeliverAsync(Body(env.Igid, Msg("mid_e1", "igsid_erase", "my address is secret", 1000)));
        await env.DeliverAsync(Body(env.Igid, Reaction("mid_e1", "igsid_erase", "react", 2000)));
        var identityId = (await env.Db.CustomerChannelIdentities.IgnoreQueryFilters().SingleAsync(i => i.ConnectionId == env.ConnectionId)).Id;

        var operatorResult = await env.PrivacyAsync(TenantRole.Operator, identityId);
        Assert.Equal(403, operatorResult.Error!.Status);

        var first = await env.PrivacyAsync(TenantRole.Owner, identityId);
        var second = await env.PrivacyAsync(TenantRole.Owner, identityId);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.MessagesRedacted);
        Assert.Equal(1, first.Value.ReactionsRemoved);
        Assert.True(second.Value!.AlreadyErased);
        Assert.Equal(0, second.Value.MessagesRedacted);

        await using var check = fixture.CreateDbContext(new TenantContextAccessor());
        var message = await check.Messages.IgnoreQueryFilters().SingleAsync(m => m.ConnectionId == env.ConnectionId);
        Assert.Null(message.Text);
        Assert.NotNull(message.RedactedAt);
        Assert.Equal(0, await check.MessageReactions.IgnoreQueryFilters().CountAsync(r => r.ConnectionId == env.ConnectionId));
        var audits = await check.AuditEvents.IgnoreQueryFilters()
            .Where(a => a.TenantId == env.TenantId && a.Action == "conversations.identity.erased")
            .ToListAsync();
        var audit = Assert.Single(audits);
        Assert.DoesNotContain("secret", audit.Metadata ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("igsid_erase", audit.Metadata ?? string.Empty, StringComparison.Ordinal);
    }

    // ---------- helpers ----------

    private async Task<string> ProcessInFreshContextAsync(string eventId)
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        try
        {
            var result = await CreateProcessing(db, accessor, new ConversationIngestionService(db, NullLogger<ConversationIngestionService>.Instance))
                .ProcessWebhookEventAsync(eventId);
            return result.Succeeded ? "processed" : $"failed:{result.Status}";
        }
        catch (Exception ex)
        {
            return $"threw:{ex.GetType().Name}";
        }
    }

    private async Task<Env> ArrangeAsync(string prefix, bool withStore = false)
    {
        var accessor = new TenantContextAccessor();
        var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        string? storeId = null;
        var igid = "igid_" + prefix + "_" + Guid.NewGuid().ToString("N")[..8];
        string connectionId;
        using (accessor.BeginScope(OwnerContext(tenant.Id)))
        {
            if (withStore)
            {
                var store = Store.Create(tenant.Id, new StoreSettings(
                    "S04 Store", $"{prefix}-{Guid.NewGuid():N}"[..20], null, StoreThemePreset.Default, null,
                    "Owner", "owner@example.com", null, null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
                db.Stores.Add(store);
                await db.SaveChangesAsync();
                storeId = store.Id;
            }

            var connection = ChannelConnection.Create(tenant.Id, ChannelType.Instagram, igid, "S04 IG", storeId: storeId);
            db.ChannelConnections.Add(connection);
            await db.SaveChangesAsync();
            connectionId = connection.Id;
        }

        return new Env(this, db, accessor, tenant.Id, connectionId, igid, storeId);
    }

    private static WebhookProcessingService CreateProcessing(AppDbContext db, TenantContextAccessor accessor, IConversationIngestionService ingestion)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("s04"), authorizer);
        return new WebhookProcessingService(
            db, accessor, authorizer, audit, Registry(), NullLogger<WebhookProcessingService>.Instance,
            serviceProvider: null, conversationIngestion: ingestion);
    }

    private static ChannelProviderRegistry Registry() => new(new IChannelProvider[]
    {
        new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions { AppSecret = AppSecret }))
    });

    private static string Body(string igid, string items, long entryTime = 1729500000000) =>
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + igid + "\",\"time\":" + entryTime + ",\"messaging\":[" + items + "]}]}";

    private static string Msg(string mid, string sender, string text, long timestamp) =>
        "{\"sender\":{\"id\":\"" + sender + "\"},\"timestamp\":" + timestamp + ",\"message\":{\"mid\":\"" + mid + "\",\"text\":\"" + text + "\"}}";

    private static string Seen(string mid, string reader, long timestamp) =>
        "{\"sender\":{\"id\":\"" + reader + "\"},\"timestamp\":" + timestamp + ",\"read\":{\"mid\":\"" + mid + "\"}}";

    private static string Reaction(string mid, string reactor, string action, long timestamp) =>
        "{\"sender\":{\"id\":\"" + reactor + "\"},\"timestamp\":" + timestamp + ",\"reaction\":{\"mid\":\"" + mid +
        "\",\"action\":\"" + action + "\"" + (action == "react" ? ",\"reaction\":\"love\",\"emoji\":\"\\u2764\"" : "") + "}}";

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static AesGcmSecretEncryptionService Encryption() =>
        new(Options.Create(new SecretEncryptionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            DefaultKeyVersion = "v1",
            VersionedKeys = []
        }));

    private static TenantContext OwnerContext(string tenantId) => RoleContext(tenantId, TenantRole.Owner);

    private static TenantContext RoleContext(string tenantId, TenantRole role) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", role);

    private sealed class Env(
        ConversationIngestionIntegrationTests owner,
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        string connectionId,
        string igid,
        string? storeId)
    {
        public AppDbContext Db => db;
        public TenantContextAccessor Accessor => accessor;
        public string TenantId => tenantId;
        public string ConnectionId => connectionId;
        public string Igid => igid;
        public string? StoreId => storeId;

        public WebhookProcessingService Processing(IConversationIngestionService? ingestion = null) =>
            CreateProcessing(db, accessor, ingestion ?? new ConversationIngestionService(db, NullLogger<ConversationIngestionService>.Instance));

        public async Task<string> IngestOnlyAsync(string body)
        {
            var ingress = new WebhookIngressService(db, Registry(), Encryption(), accessor, NullLogger<WebhookIngressService>.Instance);
            var result = await ingress.HandleWebhookAsync(new WebhookIngressCommand(
                Channel: ChannelType.Instagram,
                ConnectionId: null,
                Method: "POST",
                Path: "/v1/webhooks/instagram",
                Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = "application/json",
                    [InstagramChannelProvider.SignatureHeader] = Sign(body)
                },
                QueryParameters: new Dictionary<string, string>(),
                RawBody: Encoding.UTF8.GetBytes(body),
                ContentType: "application/json",
                CorrelationId: Guid.NewGuid().ToString("N"),
                ReceivedAt: DateTimeOffset.UtcNow));
            Assert.True(result.IsSuccess);
            return result.EventId ?? string.Empty;
        }

        /// <summary>Ingests and processes; duplicates are acknowledged by ingress and not reprocessed.</summary>
        public async Task DeliverAsync(string body)
        {
            var eventId = await IngestOnlyAsync(body);
            var alreadyProcessed = await db.WebhookEvents.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(e => e.Id == eventId && e.ProcessingStatus == WebhookProcessingStatus.Processed);
            if (string.IsNullOrEmpty(eventId) || alreadyProcessed)
            {
                return;
            }

            var result = await Processing().ProcessWebhookEventAsync(eventId);
            owner.output.WriteLine($"processed {eventId[..6]}: {result.Succeeded} {result.ErrorMessage}");
            Assert.True(result.Succeeded);
        }

        public async Task<T> QueryAsync<T>(Func<ConversationQueryService, Task<Kreyora.Application.Models.Result<T>>> query)
        {
            using var scope = accessor.BeginScope(OwnerContext(tenantId));
            var service = new ConversationQueryService(db, accessor, new TenantPermissionAuthorizer(accessor));
            var result = await query(service);
            Assert.True(result.IsSuccess, result.Error?.Detail);
            return result.Value!;
        }

        public async Task<Kreyora.Application.Models.Result<IdentityErasureResult>> PrivacyAsync(TenantRole role, string identityId)
        {
            using var scope = accessor.BeginScope(RoleContext(tenantId, role));
            db.ChangeTracker.Clear();
            var authorizer = new TenantPermissionAuthorizer(accessor);
            var service = new ConversationPrivacyService(db, accessor, authorizer, new AuditEventService(db, accessor, new Correlation("s04-erase"), authorizer));
            return await service.EraseIdentityAsync(identityId);
        }
    }

    private sealed class ThrowingIngestion : IConversationIngestionService
    {
        public Task IngestAsync(InboundEvent inboundEvent, NormalizedInboundPayload payload, ChannelConnection connection, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated transient ingestion failure.");
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
