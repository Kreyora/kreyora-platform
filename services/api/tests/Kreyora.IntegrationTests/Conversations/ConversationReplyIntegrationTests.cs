using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Conversations;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Conversations;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Kreyora.IntegrationTests.Conversations;

/// <summary>
/// M08-S05 (ADR-017): staff replies through the durable outbox, takeover invariant, delivery semantics,
/// echoes, ownership operations. Real PostgreSQL (Testcontainers); the Instagram Graph API is a scripted
/// stub — no live provider calls. Races use forced interleaving (decorators/interceptors), not sleeps.
/// </summary>
public sealed class ConversationReplyIntegrationTests : IClassFixture<PostgresFixture>
{
    private const string AppSecret = "s05_app_secret";
    private static readonly byte[] MasterKey = RandomNumberGenerator.GetBytes(32);

    private readonly PostgresFixture fixture;
    private readonly ITestOutputHelper output;

    public ConversationReplyIntegrationTests(PostgresFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    // ---------- staff reply ----------

    [Fact]
    public async Task StaffReply_RecordsPendingRow_TakesOver_ThenDeliversWithActorAndProviderId()
    {
        var env = await ArrangeAsync("s05-reply");

        var reply = await env.ReplyAsync("Yes, size M is in stock", "key-1");
        Assert.True(reply.IsSuccess, reply.Error?.Detail);
        Assert.True(reply.Value!.IsPending);
        Assert.Equal(Env.OperatorUserId, reply.Value.ActorUserId);

        var conversation = await env.ConversationAsync();
        Assert.Equal(AutomationMode.HumanTakeover, conversation.AutomationMode);
        Assert.Equal(ConversationStatus.HumanAssigned, conversation.Status);
        var outbound = await env.Fresh(db => db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.ConnectionId == env.ConnectionId));
        Assert.Equal(OutboundMessageOrigin.Staff, outbound.Origin);
        Assert.Equal(Env.OperatorUserId, outbound.ActorUserId);
        Assert.Equal(env.ConversationId, outbound.ConversationId);
        Assert.Single(await env.AuditsAsync("conversations.takeover"));

        await env.DeliverAsync(outbound.Id);

        var row = await env.Fresh(db => db.Messages.IgnoreQueryFilters().SingleAsync(m => m.OutboundMessageId == outbound.Id));
        Assert.Equal("mid_sent_1", row.ProviderMessageId);
        Assert.Equal(MessageDeliveryStatus.Sent, row.DeliveryStatus);
        Assert.Equal(MessageOrigin.Staff, row.Origin);
        var call = Assert.Single(env.Graph.Calls);
        Assert.Equal("igsid_customer", call.Recipient);
        Assert.Equal("page_token_plain", call.Token);
        Assert.Null(call.Tag);
    }

    [Fact]
    public async Task StaffReply_SameIdempotencyKey_IsOneMessage()
    {
        var env = await ArrangeAsync("s05-idem");

        var first = await env.ReplyAsync("hello", "same-key");
        var second = await env.ReplyAsync("hello", "same-key");

        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(1, await env.Fresh(db => db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConnectionId == env.ConnectionId)));
        Assert.Equal(1, await env.Fresh(db => db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == env.ConversationId)));
    }

    [Theory]
    [InlineData("spam", ConversationDenialReasons.ConversationIsSpam)]
    [InlineData("inactive", ConversationDenialReasons.ConnectionInactive)]
    [InlineData("too_long", ConversationDenialReasons.TextTooLong)]
    [InlineData("empty", ConversationDenialReasons.TextRequired)]
    [InlineData("window_30h", ConversationDenialReasons.WindowClosedHumanAgentUnavailable)]
    [InlineData("window_8d", ConversationDenialReasons.WindowClosed)]
    public async Task StaffReply_Denials_HaveStableReasons_AndWriteNothing(string scenario, string expectedCode)
    {
        var env = await ArrangeAsync("s05-deny", lastCustomerMessageAgo: scenario switch
        {
            "window_30h" => TimeSpan.FromHours(30),
            "window_8d" => TimeSpan.FromDays(8),
            _ => TimeSpan.FromHours(1)
        });
        if (scenario == "spam")
        {
            await env.SqlAsync($"UPDATE conversations SET status = 'Spam' WHERE id = '{env.ConversationId}'");
        }

        if (scenario == "inactive")
        {
            await env.SqlAsync($"UPDATE channel_connections SET status = 'Disabled' WHERE id = '{env.ConnectionId}'");
        }

        var text = scenario switch { "too_long" => new string('x', 1001), "empty" => "   ", _ => "hello" };
        var result = await env.ReplyAsync(text, "deny-key");

        Assert.False(result.IsSuccess);
        Assert.Equal(ConversationDenialReasons.ProblemType(expectedCode), result.Error!.Type);
        Assert.Equal(0, await env.Fresh(db => db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConnectionId == env.ConnectionId)));
        Assert.Equal(0, await env.Fresh(db => db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == env.ConversationId)));
        Assert.Equal(AutomationMode.Automated, (await env.ConversationAsync()).AutomationMode);
        Assert.Empty(await env.AuditsAsync("conversations.takeover"));
    }

    [Fact]
    public async Task HumanAgentApproved_LateReply_SendsWithTag()
    {
        var env = await ArrangeAsync("s05-tag", lastCustomerMessageAgo: TimeSpan.FromHours(30), humanAgentApproved: true);

        var reply = await env.ReplyAsync("Sorry for the delay", "tag-key");
        Assert.True(reply.IsSuccess, reply.Error?.Detail);
        await env.DeliverAllAsync();

        Assert.Equal(ConversationGate.HumanAgentTag, Assert.Single(env.Graph.Calls).Tag);
    }

    [Fact]
    public async Task WindowClosingWhileQueued_FailsAtDelivery_WithoutCallingProvider()
    {
        var env = await ArrangeAsync("s05-window-queue", lastCustomerMessageAgo: TimeSpan.FromHours(23.9));
        var reply = await env.ReplyAsync("just in time?", "late-key");
        Assert.True(reply.IsSuccess);

        env.Clock.UtcNow = env.Clock.UtcNow.AddHours(2);
        await env.DeliverAllAsync();

        Assert.Empty(env.Graph.Calls);
        var outbound = await env.Fresh(db => db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.ConnectionId == env.ConnectionId));
        Assert.Equal(OutboundMessageStatus.DeadLetter, outbound.Status);
        var row = await env.Fresh(db => db.Messages.IgnoreQueryFilters().SingleAsync(m => m.OutboundMessageId == outbound.Id));
        Assert.Equal(MessageDeliveryStatus.Failed, row.DeliveryStatus);
        var attempt = await env.Fresh(db => db.OutboundDeliveryAttempts.IgnoreQueryFilters().SingleAsync(a => a.OutboundMessageId == outbound.Id));
        Assert.Equal(ConversationDenialReasons.WindowClosedHumanAgentUnavailable, attempt.ProviderErrorCode);
    }

    // ---------- takeover invariant ----------

    [Fact]
    public async Task Race_TakeoverCancelsQueuedAutomation_ProviderNeverCalled()
    {
        var env = await ArrangeAsync("s05-race-queued");
        var automationId = (await env.AutomationAsync("bot reply", "bot-1")).Value!;

        var takeover = await env.InboxAsync(s => s.TakeOverAsync(env.ConversationId));
        await env.DeliverAsync(automationId);

        Assert.True(takeover.IsSuccess);
        Assert.Empty(env.Graph.Calls);
        Assert.Equal(OutboundMessageStatus.Cancelled, await env.OutboundStatusAsync(automationId));
        var audit = Assert.Single(await env.AuditsAsync("conversations.takeover"));
        Assert.Equal(1, Metadata(audit, "automationMessagesCancelled"));
    }

    [Fact]
    public async Task Automation_IsDeniedAtEnqueue_AfterTakeover()
    {
        var env = await ArrangeAsync("s05-enqueue-denied");
        await env.InboxAsync(s => s.TakeOverAsync(env.ConversationId));

        var result = await env.AutomationAsync("bot reply", "bot-2");

        Assert.Equal(ConversationDenialReasons.ProblemType(ConversationDenialReasons.AutomationPausedByTakeover), result.Error!.Type);
    }

    [Fact]
    public async Task Race_AutomationThatSlippedPastTakeover_IsCancelledAtDelivery()
    {
        var env = await ArrangeAsync("s05-race-slipped");
        var automationId = (await env.AutomationAsync("bot reply", "bot-3")).Value!;
        // Simulate the enqueue committing after the takeover's cancellation scan.
        await env.SqlAsync($"UPDATE conversations SET automation_mode = 'HumanTakeover', status = 'HumanAssigned' WHERE id = '{env.ConversationId}'");

        await env.DeliverAsync(automationId);

        Assert.Empty(env.Graph.Calls);
        Assert.Equal(OutboundMessageStatus.Cancelled, await env.OutboundStatusAsync(automationId));
    }

    [Fact]
    public async Task Race_TakeoverBetweenGateReadAndMarkSending_DeliveryLosesAndNeverSends()
    {
        var env = await ArrangeAsync("s05-race-interleave");
        var automationId = (await env.AutomationAsync("bot reply", "bot-4")).Value!;

        // The delivery's gate reads "Automated"; before MarkSending is saved, a takeover commits elsewhere.
        var accessor = new TenantContextAccessor();
        await using var deliveryDb = fixture.CreateDbContext(accessor);
        var gate = new InterleavingGate(env.Gate(deliveryDb), async () =>
        {
            var takeover = await env.InboxAsync(s => s.TakeOverAsync(env.ConversationId));
            Assert.True(takeover.IsSuccess);
        });
        await env.Outbox(deliveryDb, accessor, gate).ProcessDeliveryAsync(automationId);

        output.WriteLine($"gate calls: {gate.Calls}; provider calls: {env.Graph.Calls.Count}");
        Assert.Equal(1, gate.Calls);
        Assert.Empty(env.Graph.Calls);
        Assert.Equal(OutboundMessageStatus.Cancelled, await env.OutboundStatusAsync(automationId));
    }

    [Fact]
    public async Task InFlightAutomationAtTakeover_IsReportedInAudit_NotHidden()
    {
        var env = await ArrangeAsync("s05-inflight");
        var automationId = (await env.AutomationAsync("bot reply", "bot-5")).Value!;
        await env.SqlAsync($"UPDATE outbound_messages SET status = 'Sending' WHERE id = '{automationId}'");

        await env.InboxAsync(s => s.TakeOverAsync(env.ConversationId));

        var audit = Assert.Single(await env.AuditsAsync("conversations.takeover"));
        Assert.Equal(1, Metadata(audit, "automationMessagesInFlight"));
        Assert.Equal(0, Metadata(audit, "automationMessagesCancelled"));
    }

    [Fact]
    public async Task Race_InboundMessageDuringStaffReply_BothPersist()
    {
        var env = await ArrangeAsync("s05-race-inbound");
        var inboundAt = env.Clock.UtcNow.AddMinutes(-1);

        // While the reply is being prepared, an inbound message updates the same conversation and commits first.
        var enqueuer = new InterleavingEnqueuer(env, async () =>
        {
            var accessor = new TenantContextAccessor();
            await using var other = fixture.CreateDbContext(accessor);
            using var scope = accessor.BeginScope(Env.Context(env.TenantId, TenantRole.Owner));
            var conversation = await other.Conversations.SingleAsync(c => c.Id == env.ConversationId);
            conversation.RecordInboundMessage(inboundAt);
            await other.SaveChangesAsync();
        });

        var reply = await env.ReplyAsync("reply during inbound", "inbound-race", enqueuer);

        output.WriteLine($"enqueue calls: {enqueuer.Calls}; success: {reply.IsSuccess} {reply.Error?.Detail}");
        Assert.True(reply.IsSuccess, reply.Error?.Detail);
        Assert.True(enqueuer.Calls >= 2, "the first attempt must have lost the race and retried");
        var conversation = await env.ConversationAsync();
        Assert.Equal(2, conversation.UnreadCount);
        Assert.Equal(inboundAt, conversation.LastCustomerMessageAt);
        Assert.Equal(AutomationMode.HumanTakeover, conversation.AutomationMode);
        Assert.Equal(1, await env.Fresh(db => db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConnectionId == env.ConnectionId)));
    }

    // ---------- delivery semantics ----------

    [Fact]
    public async Task ProviderRetry_ThrottleThenSuccess_OneTimelineRow_TwoAttempts()
    {
        var env = await ArrangeAsync("s05-retry");
        env.Graph.Enqueue(InstagramSendResult.Failed(InstagramSendOutcome.Transient, "613", "throttled"));
        env.Graph.Enqueue(InstagramSendResult.Sent("mid_after_retry"));
        await env.ReplyAsync("retry me", "retry-key");
        var outboundId = await env.Fresh(db => db.OutboundMessages.IgnoreQueryFilters().Where(m => m.ConnectionId == env.ConnectionId).Select(m => m.Id).SingleAsync());

        await env.DeliverAsync(outboundId);
        Assert.Equal(OutboundMessageStatus.Failed, await env.OutboundStatusAsync(outboundId));
        Assert.True((await env.Fresh(db => db.Messages.IgnoreQueryFilters().SingleAsync(m => m.OutboundMessageId == outboundId))).IsPending);

        await env.DeliverAsync(outboundId);

        Assert.Equal(2, env.Graph.Calls.Count);
        Assert.Equal(OutboundMessageStatus.Sent, await env.OutboundStatusAsync(outboundId));
        var rows = await env.Fresh(db => db.Messages.IgnoreQueryFilters().Where(m => m.ConversationId == env.ConversationId).ToListAsync());
        var row = Assert.Single(rows);
        Assert.Equal("mid_after_retry", row.ProviderMessageId);
        Assert.Equal(2, await env.Fresh(db => db.OutboundDeliveryAttempts.IgnoreQueryFilters().CountAsync(a => a.OutboundMessageId == outboundId)));
    }

    [Fact]
    public async Task AmbiguousTimeout_IsNotRetried_AndStaffRowShowsFailed()
    {
        var env = await ArrangeAsync("s05-unconfirmed");
        env.Graph.Enqueue(InstagramSendResult.Failed(InstagramSendOutcome.Unconfirmed, "timeout", "no answer"));
        await env.ReplyAsync("did it arrive?", "unconfirmed-key");
        var outboundId = await env.Fresh(db => db.OutboundMessages.IgnoreQueryFilters().Where(m => m.ConnectionId == env.ConnectionId).Select(m => m.Id).SingleAsync());

        await env.DeliverAsync(outboundId);
        await env.DeliverAsync(outboundId);

        Assert.Single(env.Graph.Calls);
        Assert.Equal(OutboundMessageStatus.DeadLetter, await env.OutboundStatusAsync(outboundId));
        Assert.Equal(MessageDeliveryStatus.Failed,
            (await env.Fresh(db => db.Messages.IgnoreQueryFilters().SingleAsync(m => m.OutboundMessageId == outboundId))).DeliveryStatus);
        Assert.Equal(ConversationDenialReasons.DeliveryUnconfirmed,
            (await env.Fresh(db => db.OutboundDeliveryAttempts.IgnoreQueryFilters().SingleAsync(a => a.OutboundMessageId == outboundId))).ProviderErrorCode);
    }

    [Fact]
    public async Task TokenExpiredOnSend_MarksConnectionExpired()
    {
        var env = await ArrangeAsync("s05-token");
        env.Graph.Enqueue(InstagramSendResult.Failed(InstagramSendOutcome.TokenExpired, "190", "expired"));
        await env.ReplyAsync("hello", "token-key");

        await env.DeliverAllAsync();

        var connection = await env.Fresh(db => db.ChannelConnections.IgnoreQueryFilters().SingleAsync(c => c.Id == env.ConnectionId));
        Assert.Equal(ChannelConnectionStatus.Expired, connection.Status);
    }

    // ---------- echoes ----------

    [Fact]
    public async Task Echo_FromInstagramApp_AppearsAsProviderNative_WithoutUnreadOrWindowChange()
    {
        var env = await ArrangeAsync("s05-echo-app");
        var before = await env.ConversationAsync();

        await env.DeliverWebhookAsync(EchoBody(env.Igid, "mid_app_echo", "Typed on my phone"));

        var conversation = await env.ConversationAsync();
        var echo = await env.Fresh(db => db.Messages.IgnoreQueryFilters().SingleAsync(m => m.ProviderMessageId == "mid_app_echo"));
        Assert.Equal(MessageOrigin.ProviderNative, echo.Origin);
        Assert.Equal(MessageDirection.Outbound, echo.Direction);
        Assert.Equal(before.UnreadCount, conversation.UnreadCount);
        Assert.Equal(before.LastCustomerMessageAt, conversation.LastCustomerMessageAt);
        Assert.Equal(AutomationMode.Automated, conversation.AutomationMode);
    }

    [Fact]
    public async Task Echo_OfKreyoraSend_AfterDelivery_IsIgnored()
    {
        var env = await ArrangeAsync("s05-echo-after");
        env.Graph.Enqueue(InstagramSendResult.Sent("mid_known"));
        await env.ReplyAsync("from Kreyora", "echo-after");
        await env.DeliverAllAsync();

        await env.DeliverWebhookAsync(EchoBody(env.Igid, "mid_known", "from Kreyora"));

        var rows = await env.Fresh(db => db.Messages.IgnoreQueryFilters().Where(m => m.ConversationId == env.ConversationId).ToListAsync());
        var row = Assert.Single(rows);
        Assert.Equal(MessageOrigin.Staff, row.Origin);
    }

    [Fact]
    public async Task Echo_ArrivingBeforeSendCompletes_MergesIntoOneStaffRow()
    {
        var env = await ArrangeAsync("s05-echo-before");
        env.Graph.Enqueue(InstagramSendResult.Sent("mid_raced"));
        await env.ReplyAsync("from Kreyora", "echo-before");

        await env.DeliverWebhookAsync(EchoBody(env.Igid, "mid_raced", "from Kreyora"));
        await env.DeliverAllAsync();

        var rows = await env.Fresh(db => db.Messages.IgnoreQueryFilters().Where(m => m.ConversationId == env.ConversationId).ToListAsync());
        var row = Assert.Single(rows);
        Assert.Equal(MessageOrigin.Staff, row.Origin);
        Assert.Equal("mid_raced", row.ProviderMessageId);
        Assert.Equal(Env.OperatorUserId, row.ActorUserId);
    }

    // ---------- ownership operations ----------

    [Fact]
    public async Task Race_ConcurrentReassignment_OneWins_OtherGets409_FinalStateMatchesAudit()
    {
        var env = await ArrangeAsync("s05-assign-race");
        var userA = await env.AddMemberAsync(MembershipStatus.Active);
        var userB = await env.AddMemberAsync(MembershipStatus.Active);

        // Assignment to A loads the conversation, then (during its membership check) B's assignment commits.
        var accessor = new TenantContextAccessor();
        var interceptor = new OnceBeforeQueryInterceptor("FROM memberships", async () =>
        {
            var winner = await env.InboxAsync(s => s.AssignAsync(env.ConversationId, userB));
            Assert.True(winner.IsSuccess, winner.Error?.Detail);
        });
        await using var loserDb = env.CreateDbContextWithInterceptor(accessor, interceptor);
        Result<ConversationDetailItem> loser;
        using (accessor.BeginScope(Env.Context(env.TenantId, TenantRole.Operator)))
        {
            loser = await env.Inbox(loserDb, accessor).AssignAsync(env.ConversationId, userA);
        }

        Assert.Equal(409, loser.Error!.Status);
        Assert.Equal(userB, (await env.ConversationAsync()).AssignedUserId);
        var audits = await env.AuditsAsync("conversations.assigned");
        Assert.Contains(userB, Assert.Single(audits).Metadata!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assign_RequiresActiveMemberOfThisTenant()
    {
        var env = await ArrangeAsync("s05-assign");
        var suspended = await env.AddMemberAsync(MembershipStatus.Suspended);
        var other = await ArrangeAsync("s05-assign-other");
        var foreignMember = await other.AddMemberAsync(MembershipStatus.Active);

        foreach (var userId in new[] { "01H00000000000000000000099", suspended, foreignMember })
        {
            var result = await env.InboxAsync(s => s.AssignAsync(env.ConversationId, userId));
            Assert.Equal(ConversationDenialReasons.ProblemType(ConversationDenialReasons.AssigneeNotMember), result.Error!.Type);
        }

        Assert.Null((await env.ConversationAsync()).AssignedUserId);
        Assert.Empty(await env.AuditsAsync("conversations.assigned"));
    }

    [Fact]
    public async Task Release_ResumesAutomation_AndStatusMachineIsEnforced()
    {
        var env = await ArrangeAsync("s05-status");
        await env.InboxAsync(s => s.TakeOverAsync(env.ConversationId));

        var released = await env.InboxAsync(s => s.ReleaseAsync(env.ConversationId));
        Assert.True(released.Value!.IsAutomationActive);
        Assert.Equal(ConversationStatus.BotActive, released.Value.Status);
        Assert.True((await env.AutomationAsync("bot is back", "bot-6")).IsSuccess);

        Assert.Equal(ConversationStatus.Resolved, (await env.InboxAsync(s => s.ChangeStatusAsync(env.ConversationId, ConversationStatusAction.Resolve))).Value!.Status);
        Assert.Equal(ConversationStatus.New, (await env.InboxAsync(s => s.ChangeStatusAsync(env.ConversationId, ConversationStatusAction.Reopen))).Value!.Status);
        Assert.Equal(ConversationStatus.Spam, (await env.InboxAsync(s => s.ChangeStatusAsync(env.ConversationId, ConversationStatusAction.MarkSpam))).Value!.Status);

        var invalid = await env.InboxAsync(s => s.ChangeStatusAsync(env.ConversationId, ConversationStatusAction.Resolve));
        Assert.Equal(409, invalid.Error!.Status);
        Assert.Equal(ConversationDenialReasons.ProblemType(ConversationDenialReasons.InvalidTransition), invalid.Error.Type);

        Assert.Equal(3, (await env.AuditsAsync("conversations.status_changed")).Count);
        Assert.Single(await env.AuditsAsync("conversations.released"));
    }

    [Fact]
    public async Task Labels_ReplaceSet_TrimmedAndDeduplicatedCaseInsensitively()
    {
        var env = await ArrangeAsync("s05-labels");

        await env.InboxAsync(s => s.SetLabelsAsync(env.ConversationId, ["VIP", "cod", " vip "]));
        var result = await env.InboxAsync(s => s.SetLabelsAsync(env.ConversationId, ["COD", "size-m"]));

        // "cod" already existed (case-insensitive match keeps its original casing); "VIP" was removed.
        Assert.Equal("cod|size-m", string.Join("|", result.Value!.Labels.Order(StringComparer.Ordinal)));

        var tooMany = await env.InboxAsync(s => s.SetLabelsAsync(env.ConversationId, Enumerable.Range(0, 21).Select(i => $"l{i}").ToList()));
        Assert.Equal(422, tooMany.Error!.Status);
    }

    // ---------- harness ----------

    private async Task<Env> ArrangeAsync(string prefix, TimeSpan? lastCustomerMessageAgo = null, bool humanAgentApproved = false)
    {
        var accessor = new TenantContextAccessor();
        var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var env = new Env(this, db, accessor, tenant.Id, clock, humanAgentApproved);
        await env.SeedAsync(prefix, clock.UtcNow - (lastCustomerMessageAgo ?? TimeSpan.FromHours(1)));
        return env;
    }

    private static int Metadata(Kreyora.Domain.Audit.AuditEvent audit, string property)
    {
        using var json = System.Text.Json.JsonDocument.Parse(audit.Metadata!);
        return json.RootElement.GetProperty(property).GetInt32();
    }

    private static string EchoBody(string igid, string mid, string text) =>
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + igid + "\",\"time\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
        ",\"messaging\":[{\"sender\":{\"id\":\"" + igid + "\"},\"recipient\":{\"id\":\"igsid_customer\"},\"timestamp\":" +
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ",\"message\":{\"mid\":\"" + mid + "\",\"text\":\"" + text + "\",\"is_echo\":true}}]}]}";

    private sealed class Env(
        ConversationReplyIntegrationTests owner,
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        MutableClock clock,
        bool humanAgentApproved)
    {
        public const string OperatorUserId = "01J00000000000000000000011";

        private readonly AesGcmSecretEncryptionService encryption = new(Options.Create(new SecretEncryptionOptions
        {
            MasterKey = Convert.ToBase64String(MasterKey),
            DefaultKeyVersion = "v1",
            VersionedKeys = []
        }));

        public string TenantId => tenantId;
        public MutableClock Clock => clock;
        public ScriptedGraph Graph { get; } = new();
        public string ConnectionId { get; private set; } = string.Empty;
        public string ConversationId { get; private set; } = string.Empty;
        public string Igid { get; private set; } = string.Empty;

        public static TenantContext Context(string tenantId, TenantRole role, string userId = OperatorUserId) =>
            new(tenantId, userId, "01J00000000000000000000002", role);

        public async Task SeedAsync(string prefix, DateTimeOffset lastCustomerMessageAt)
        {
            using var scope = accessor.BeginScope(Context(tenantId, TenantRole.Owner));
            Igid = "igid_" + prefix + "_" + Guid.NewGuid().ToString("N")[..8];
            var connection = ChannelConnection.Create(tenantId, ChannelType.Instagram, Igid, "S05 IG",
                encryptedCredentials: encryption.Encrypt("page_token_plain"));
            db.ChannelConnections.Add(connection);
            var identity = CustomerChannelIdentity.Create(tenantId, connection.Id, ChannelType.Instagram, "igsid_customer", lastCustomerMessageAt);
            var conversation = Conversation.Start(tenantId, connection.Id, null, identity.Id, ChannelType.Instagram);
            conversation.RecordInboundMessage(lastCustomerMessageAt);
            db.CustomerChannelIdentities.Add(identity);
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
            ConnectionId = connection.Id;
            ConversationId = conversation.Id;
        }

        public InstagramMessagingOptions Messaging => new() { HumanAgentTagApproved = humanAgentApproved };

        public ConversationGate Gate(AppDbContext context) => new(context, clock, Options.Create(Messaging));

        public ChannelProviderRegistry Registry() => new(new IChannelProvider[]
        {
            new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions { AppSecret = AppSecret }), null, Graph, encryption)
        });

        public OutboundMessageService Outbox(AppDbContext context, TenantContextAccessor contextAccessor, IConversationGate? gate = null)
        {
            var authorizer = new TenantPermissionAuthorizer(contextAccessor);
            return new OutboundMessageService(context, contextAccessor, authorizer,
                new AuditEventService(context, contextAccessor, new Correlation("s05"), authorizer), Registry(),
                gate ?? Gate(context), clock, NullLogger<OutboundMessageService>.Instance, new ConversationOutboundReconciler(context));
        }

        public ConversationInboxService Inbox(AppDbContext context, TenantContextAccessor contextAccessor)
        {
            var authorizer = new TenantPermissionAuthorizer(contextAccessor);
            return new ConversationInboxService(context, contextAccessor, authorizer, new ConversationQueryService(context, contextAccessor, authorizer),
                new AuditEventService(context, contextAccessor, new Correlation("s05"), authorizer), clock);
        }

        public ConversationReplyService Replies(AppDbContext context, TenantContextAccessor contextAccessor, IOutboundEnqueuer? enqueuer = null)
        {
            var authorizer = new TenantPermissionAuthorizer(contextAccessor);
            return new ConversationReplyService(context, contextAccessor, authorizer, enqueuer ?? Outbox(context, contextAccessor),
                new AuditEventService(context, contextAccessor, new Correlation("s05"), authorizer), clock, Options.Create(Messaging));
        }

        public async Task<Result<MessageItem>> ReplyAsync(string text, string key, IOutboundEnqueuer? enqueuer = null)
        {
            var contextAccessor = new TenantContextAccessor();
            await using var context = owner.fixture.CreateDbContext(contextAccessor);
            using var scope = contextAccessor.BeginScope(Context(tenantId, TenantRole.Operator));
            return await Replies(context, contextAccessor, enqueuer is InterleavingEnqueuer i ? i.Bind(context, contextAccessor) : enqueuer)
                .SendStaffReplyAsync(ConversationId, text, key);
        }

        public async Task<Result<string>> AutomationAsync(string text, string key)
        {
            var contextAccessor = new TenantContextAccessor();
            await using var context = owner.fixture.CreateDbContext(contextAccessor);
            using var scope = contextAccessor.BeginScope(new TenantContext(tenantId, null, null, null));
            return await Replies(context, contextAccessor).EnqueueAutomationReplyAsync(ConversationId, text, key);
        }

        public async Task<Result<ConversationDetailItem>> InboxAsync(Func<ConversationInboxService, Task<Result<ConversationDetailItem>>> action)
        {
            var contextAccessor = new TenantContextAccessor();
            await using var context = owner.fixture.CreateDbContext(contextAccessor);
            using var scope = contextAccessor.BeginScope(Context(tenantId, TenantRole.Operator));
            return await action(Inbox(context, contextAccessor));
        }

        public async Task DeliverAsync(string outboundId)
        {
            var contextAccessor = new TenantContextAccessor();
            await using var context = owner.fixture.CreateDbContext(contextAccessor);
            await Outbox(context, contextAccessor).ProcessDeliveryAsync(outboundId);
        }

        public async Task DeliverAllAsync()
        {
            var ids = await Fresh(d => d.OutboundMessages.IgnoreQueryFilters()
                .Where(m => m.ConnectionId == ConnectionId && (m.Status == OutboundMessageStatus.Queued || m.Status == OutboundMessageStatus.Failed))
                .Select(m => m.Id).ToListAsync());
            foreach (var id in ids)
            {
                await DeliverAsync(id);
            }
        }

        public async Task DeliverWebhookAsync(string body)
        {
            var contextAccessor = new TenantContextAccessor();
            await using var context = owner.fixture.CreateDbContext(contextAccessor);
            var ingress = new WebhookIngressService(context, Registry(), encryption, contextAccessor, NullLogger<WebhookIngressService>.Instance);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
            var result = await ingress.HandleWebhookAsync(new WebhookIngressCommand(
                ChannelType.Instagram, null, "POST", "/v1/webhooks/instagram",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = "application/json",
                    [InstagramChannelProvider.SignatureHeader] = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)))
                },
                new Dictionary<string, string>(), Encoding.UTF8.GetBytes(body), "application/json", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
            Assert.True(result.IsSuccess && result.EventId is not null);

            var authorizer = new TenantPermissionAuthorizer(contextAccessor);
            var processing = new WebhookProcessingService(context, contextAccessor, authorizer,
                new AuditEventService(context, contextAccessor, new Correlation("s05"), authorizer), Registry(),
                NullLogger<WebhookProcessingService>.Instance, null, new ConversationIngestionService(context, NullLogger<ConversationIngestionService>.Instance));
            Assert.True((await processing.ProcessWebhookEventAsync(result.EventId!)).Succeeded);
        }

        public async Task<string> AddMemberAsync(MembershipStatus status)
        {
            var email = $"u{Guid.NewGuid():N}@kreyora.test";
            var user = new ApplicationUser
            {
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = "Staff"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            using var scope = accessor.BeginScope(Context(tenantId, TenantRole.Owner));
            var membership = Membership.Grant(tenantId, user.Id, TenantRole.Operator);
            if (status == MembershipStatus.Suspended)
            {
                membership.Suspend(DateTimeOffset.UtcNow);
            }

            db.Memberships.Add(membership);
            await db.SaveChangesAsync();
            return user.Id;
        }

        public AppDbContext CreateDbContextWithInterceptor(TenantContextAccessor contextAccessor, IInterceptor interceptor) =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(owner.fixture.ConnectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .AddInterceptors(interceptor)
                .Options, contextAccessor);

        public async Task<T> Fresh<T>(Func<AppDbContext, Task<T>> query)
        {
            await using var context = owner.fixture.CreateDbContext(new TenantContextAccessor());
            return await query(context);
        }

        public Task<Conversation> ConversationAsync() =>
            Fresh(d => d.Conversations.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == ConversationId));

        public Task<OutboundMessageStatus> OutboundStatusAsync(string id) =>
            Fresh(d => d.OutboundMessages.IgnoreQueryFilters().Where(m => m.Id == id).Select(m => m.Status).SingleAsync());

        public Task<List<Kreyora.Domain.Audit.AuditEvent>> AuditsAsync(string action) =>
            Fresh(d => d.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == tenantId && a.Action == action).ToListAsync());

        public async Task SqlAsync(string sql)
        {
            await using var context = owner.fixture.CreateDbContext(new TenantContextAccessor());
#pragma warning disable EF1002 // test-only statements with generated IDs
            await context.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
        }
    }

    /// <summary>Stand-in for the Graph API: scripted outcomes, recorded calls, thread-safe.</summary>
    private sealed class ScriptedGraph : IInstagramGraphClient
    {
        private readonly ConcurrentQueue<InstagramSendResult> script = new();
        private int sent;

        public ConcurrentQueue<(string Token, string Recipient, string Text, string? Tag)> CallLog { get; } = new();
        public List<(string Token, string Recipient, string Text, string? Tag)> Calls => CallLog.ToList();

        public void Enqueue(InstagramSendResult result) => script.Enqueue(result);

        public Task<InstagramValidationResult> ValidatePageLinkAsync(string pageAccessToken, string pageId, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstagramValidationResult> ValidateAccountAsync(string pageAccessToken, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstagramSendResult> SendTextAsync(string pageAccessToken, string recipientId, string text, string? messagingTag = null, CancellationToken cancellationToken = default)
        {
            CallLog.Enqueue((pageAccessToken, recipientId, text, messagingTag));
            return Task.FromResult(script.TryDequeue(out var scripted)
                ? scripted
                : InstagramSendResult.Sent($"mid_sent_{Interlocked.Increment(ref sent)}"));
        }
    }

    /// <summary>Returns the real gate's (now stale) answer after running a concurrent action: forces the race window.</summary>
    private sealed class InterleavingGate(IConversationGate inner, Func<Task> between) : IConversationGate
    {
        public int Calls { get; private set; }

        public async Task<ConversationGateResult> CheckSendPermissionAsync(string tenantId, string connectionId, string? conversationId, OutboundMessageOrigin origin, CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = await inner.CheckSendPermissionAsync(tenantId, connectionId, conversationId, origin, cancellationToken);
            if (Calls == 1)
            {
                await between();
            }

            return result;
        }
    }

    /// <summary>Runs a concurrent commit during the first enqueue call, then delegates to the real enqueuer.</summary>
    private sealed class InterleavingEnqueuer(Env env, Func<Task> between) : IOutboundEnqueuer
    {
        private OutboundMessageService? inner;

        public int Calls { get; private set; }

        public InterleavingEnqueuer Bind(AppDbContext context, TenantContextAccessor contextAccessor)
        {
            inner = env.Outbox(context, contextAccessor);
            return this;
        }

        public async Task<OutboundEnqueueResult> EnqueueAsync(OutboundEnqueueRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1)
            {
                await between();
            }

            return await inner!.EnqueueAsync(request, cancellationToken);
        }
    }

    /// <summary>Runs an action once, just before the first command whose SQL contains the marker executes.</summary>
    private sealed class OnceBeforeQueryInterceptor(string marker, Func<Task> action) : DbCommandInterceptor
    {
        private int fired;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(marker, StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref fired, 1) == 0)
            {
                await action();
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class MutableClock(DateTimeOffset start) : ITimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = start;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
