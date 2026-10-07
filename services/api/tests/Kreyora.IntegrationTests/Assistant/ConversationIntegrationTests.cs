using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Conversations;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Common;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant.Orchestration;
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
/// M09-S07 (ADR-022) over real PostgreSQL with the full DI host and real signed webhook ingress and processing: the
/// inbound trigger, debounce and duplicates, ownership races at every stage up to the delivery claim, release, busy
/// retries and crash restarts, the guard's second pass, native-app replies, the sweeper, the staff queue and isolation.
/// A recording scheduler captures the jobs and the tests run them explicitly, so every interleaving is deterministic.
/// </summary>
public sealed class ConversationIntegrationTests : IClassFixture<PostgresFixture>
{
    private const string AppSecret = "s07_test_app_secret";
    private readonly PostgresFixture fixture;

    public ConversationIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    // ---- trigger, duplicates, debounce ----

    [Fact]
    public async Task ACustomerDm_SchedulesOneDebouncedTurn_ThatAnswersOnce_EvenWhenDeliveredTwice()
    {
        var shop = await ReadyShopAsync("s07-trigger");
        await using var rig = await RigAsync([shop]);
        var ids = await IdsAsync(shop);
        rig.Fake.Enqueue(Text("Namaste! Kasari help garu?"));
        var mid = Mid();
        var body = Body(ids, CustomerText(ids, mid, "hello"));

        await WebhookAsync(rig, body);
        var retried = await WebhookAsync(rig, body); // the provider retried the same delivery
        await WebhookAsync(rig, Body(ids, CustomerText(ids, mid, "hello"))); // or sent the same message in a new delivery

        Assert.True(retried.IsDuplicate);
        var scheduled = Assert.Single(rig.Scheduler.Turns);
        Assert.Equal((shop.TenantId, shop.ConversationId, TimeSpan.FromSeconds(4), 0), (scheduled.TenantId, scheduled.ConversationId, scheduled.Delay, scheduled.Attempt));
        var reply = Assert.Single(await RunTurnsAsync(rig))!;
        Assert.Equal(AssistantTurnOutcome.Replied, reply.Outcome);

        var rerun = await TurnJob(rig).RunAsync(shop.TenantId, shop.ConversationId, scheduled.MessageId, 0); // Hangfire retried the job
        Assert.True(rerun!.Replayed);

        await DeliverAsync(rig, reply.OutboundMessageId!);
        var sent = Assert.Single(rig.Graph.Sent);
        Assert.Equal(("Namaste! Kasari help garu?", ids.Customer), (sent.Text, sent.Recipient));
        await using var db = Db();
        Assert.Equal(1, await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId));
    }

    [Fact]
    public async Task ReactionsReadsAndEchoes_NeverStartATurn()
    {
        var shop = await ReadyShopAsync("s07-noise");
        await using var rig = await RigAsync([shop]);
        var ids = await IdsAsync(shop);
        var mid = Mid();
        await WebhookAsync(rig, Body(ids, CustomerText(ids, mid, "hi")));
        rig.Scheduler.Turns.Clear();

        await WebhookAsync(rig, Body(ids, Reaction(ids, mid), Read(ids, mid), Echo(ids, Mid(), "Hajur, cha.")));

        Assert.Empty(rig.Scheduler.Turns);
        var check = Assert.Single(rig.Scheduler.Checks); // the echo only gets the app-reply check
        Assert.Equal(TimeSpan.FromSeconds(30), check.Delay);
    }

    [Fact]
    public async Task ABurstOfMessages_GetsOneAnswer_ThatSeesThemAll()
    {
        var shop = await ReadyShopAsync("s07-burst");
        await using var rig = await RigAsync([shop]);
        var ids = await IdsAsync(shop);
        string[] texts = ["namaste", "red kurta cha?", "size M chahiyo"];
        foreach (var text in texts) await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), text)));
        rig.Fake.Enqueue(Text("Hajur, red kurta M size ma cha."));

        var results = await RunTurnsAsync(rig);

        Assert.Equal([AssistantTurnOutcome.Superseded, AssistantTurnOutcome.Superseded, AssistantTurnOutcome.Replied], results.Select(r => r!.Outcome));
        var request = Assert.Single(rig.Fake.Requests);
        var seen = string.Join("\n", request.Messages.Select(m => m.Content));
        Assert.All(texts, t => Assert.Contains(t, seen, StringComparison.Ordinal));
        Assert.Equal(1, await AutomationCountAsync(shop));
    }

    // ---- ownership: a person wins at every stage ----

    [Fact]
    public async Task AStaffReplyBeforeTheTurnRuns_KeepsTheAssistantSilent()
    {
        var shop = await ReadyShopAsync("s07-staff-first");
        await using var rig = await RigAsync([shop]);
        var ids = await IdsAsync(shop);
        await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), "kurta kati ho?")));

        await StaffReplyAsync(rig, shop, "Hajur, ma herchu.");
        var result = Assert.Single(await RunTurnsAsync(rig))!;

        Assert.Equal((AssistantTurnOutcome.Blocked, AssistantTurnReasons.AutomationPaused), (result.Outcome, result.ReasonCode));
        Assert.Empty(rig.Fake.Requests);
        Assert.Equal(0, await AutomationCountAsync(shop));
    }

    [Fact]
    public async Task AStaffReplyDuringTheModelCall_StopsTheAssistantsReply()
    {
        var shop = await ReadyShopAsync("s07-staff-during");
        var effect = new SideEffect();
        await using var rig = await RigAsync([shop], configureServices: s => s.AddSingleton<IAiChatClient>(new SideEffectChatClient(effect)));
        effect.Action = () => StaffReplyAsync(rig, shop, "Hajur, ma herchu.");
        var ids = await IdsAsync(shop);
        await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), "hello")));

        var result = Assert.Single(await RunTurnsAsync(rig))!;

        Assert.Equal(AssistantTurnReasons.TakenOverDuringTurn, result.ReasonCode);
        Assert.Null(result.OutboundMessageId);
        Assert.Equal(0, await AutomationCountAsync(shop));
        await using var db = Db();
        Assert.Equal(OutboundMessageOrigin.Staff, (await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.ConversationId == shop.ConversationId)).Origin);
    }

    [Fact]
    public async Task AStaffReplyAfterTheAssistantsReplyIsQueued_CancelsIt_AndOnlyTheStaffMessageIsDelivered()
    {
        var shop = await ReadyShopAsync("s07-staff-after");
        await using var rig = await RigAsync([shop]);
        var reply = await AnsweredAsync(rig, shop);

        await StaffReplyAsync(rig, shop, "Hajur, ma herchu.");
        await DeliverAllAsync(rig, shop);

        Assert.Equal(OutboundMessageStatus.Cancelled, await OutboundStatusAsync(reply.OutboundMessageId!));
        Assert.Equal(["Hajur, ma herchu."], rig.Graph.Sent.Select(s => s.Text));
        var audit = await TakeoverAuditAsync(shop);
        Assert.Equal(("staff_reply", 1), (audit.Trigger, audit.Cancelled));
    }

    [Fact]
    public async Task ATakeoverAfterTheReplyIsQueued_CancelsIt_AndDeliverySendsNothing()
    {
        var shop = await ReadyShopAsync("s07-after-enqueue");
        await using var rig = await RigAsync([shop]);
        var reply = await AnsweredAsync(rig, shop);

        await TakeOverAsync(rig, shop);
        await DeliverAsync(rig, reply.OutboundMessageId!);

        Assert.Empty(rig.Graph.Sent);
        Assert.Equal(OutboundMessageStatus.Cancelled, await OutboundStatusAsync(reply.OutboundMessageId!));
        var audit = await TakeoverAuditAsync(shop);
        Assert.Equal(("manual", 1, 0), (audit.Trigger, audit.Cancelled, audit.InFlight));
    }

    [Fact]
    public async Task ATakeoverCommittingBetweenTheDeliveryGateAndTheClaim_WinsOnTheRowVersion()
    {
        var shop = await ReadyShopAsync("s07-claim-race");
        await using var rig = await RigAsync([shop]);
        var reply = await AnsweredAsync(rig, shop);

        // The delivery job reads "automated" from the gate; the takeover commits before it claims the message.
        rig.Gate.Between = () => TakeOverAsync(rig, shop);
        await DeliverAsync(rig, reply.OutboundMessageId!);

        Assert.True(rig.Gate.Fired);
        Assert.Empty(rig.Graph.Sent);
        Assert.Equal(OutboundMessageStatus.Cancelled, await OutboundStatusAsync(reply.OutboundMessageId!));
        await using var db = Db();
        Assert.False(await db.OutboundDeliveryAttempts.IgnoreQueryFilters().AnyAsync(a => a.OutboundMessageId == reply.OutboundMessageId)); // never claimed
    }

    [Fact]
    public async Task AReplyAlreadyBeingSent_WhenTheTakeoverLands_IsReportedAsInFlight()
    {
        var shop = await ReadyShopAsync("s07-in-flight");
        await using var rig = await RigAsync([shop]);
        var reply = await AnsweredAsync(rig, shop);

        rig.Graph.DuringSend = () => TakeOverAsync(rig, shop); // the claim committed first: the provider call is under way
        await DeliverAsync(rig, reply.OutboundMessageId!);

        Assert.Single(rig.Graph.Sent);
        var audit = await TakeoverAuditAsync(shop);
        Assert.Equal((0, 1), (audit.Cancelled, audit.InFlight)); // the team is told one message was already on its way
    }

    [Fact]
    public async Task ADeliveryRetriedAfterATransientProviderError_SendsTheReplyOnce()
    {
        var shop = await ReadyShopAsync("s07-delivery-retry");
        await using var rig = await RigAsync([shop]);
        var reply = await AnsweredAsync(rig, shop);
        rig.Graph.Script.Enqueue(InstagramSendResult.Failed(InstagramSendOutcome.Transient, "613", "throttled"));

        await DeliverAsync(rig, reply.OutboundMessageId!);
        Assert.Equal(OutboundMessageStatus.Failed, await OutboundStatusAsync(reply.OutboundMessageId!)); // retryable
        await DeliverAsync(rig, reply.OutboundMessageId!);
        await DeliverAsync(rig, reply.OutboundMessageId!); // a duplicate retry job

        Assert.Equal(OutboundMessageStatus.Sent, await OutboundStatusAsync(reply.OutboundMessageId!));
        Assert.Equal(2, rig.Graph.Sent.Count); // one throttled attempt, one delivery
        await using var db = Db();
        Assert.Equal(1, await db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId && m.Origin == MessageOrigin.Automation));
    }

    [Fact]
    public async Task TwoConversationsInOneShop_AreAnsweredConcurrently()
    {
        var shop = await ReadyShopAsync("s07-two-chats");
        await using var rig = await RigAsync([shop]);
        var first = await ConversationWithMessagesAsync(shop, [DateTimeOffset.UtcNow]);
        var second = await ConversationWithMessagesAsync(shop, [DateTimeOffset.UtcNow]);
        rig.Fake.Enqueue(Text("Namaste!"));
        rig.Fake.Enqueue(Text("Namaste!"));

        var results = await Task.WhenAll(
            TurnJob(rig).RunAsync(shop.TenantId, first.ConversationId, first.Messages[0], 0),
            TurnJob(rig).RunAsync(shop.TenantId, second.ConversationId, second.Messages[0], 0));

        Assert.All(results, r => Assert.Equal(AssistantTurnOutcome.Replied, r!.Outcome)); // within the shop cap of two
        Assert.Empty(rig.Scheduler.Turns);
    }

    // ---- release ----

    [Fact]
    public async Task MessagesFromBeforeARelease_StayWithThePerson_AndTheNextOneIsAnswered()
    {
        var shop = await ReadyShopAsync("s07-release");
        await using var rig = await RigAsync([shop]);
        var ids = await IdsAsync(shop);
        await TakeOverAsync(rig, shop);
        await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), "hello?"))); // arrives while a person owns the chat
        var waiting = Assert.Single(rig.Scheduler.Turns);
        rig.Scheduler.Turns.Clear();

        using var client = rig.Host.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"/v1/conversations/{shop.ConversationId}/release", shop.TenantId, TenantRole.Viewer)).StatusCode);
        await JsonAsync(client, HttpMethod.Post, $"/v1/conversations/{shop.ConversationId}/release", shop.TenantId, TenantRole.Operator);

        var late = await TurnJob(rig).RunAsync(waiting.TenantId, waiting.ConversationId, waiting.MessageId, 0); // its job runs after the release
        Assert.Equal(AssistantTurnReasons.ReceivedBeforeRelease, late!.ReasonCode);
        Assert.Empty(rig.Fake.Requests);

        rig.Fake.Enqueue(Text("Namaste! Kasari help garu?"));
        await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), "hello again")));
        Assert.Equal(AssistantTurnOutcome.Replied, Assert.Single(await RunTurnsAsync(rig))!.Outcome);
        await using var db = Db();
        Assert.NotNull((await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).AutomationResumedAt);
    }

    // ---- retries ----

    [Fact]
    public async Task ABusyConversation_RetriesWithBackoff_UpToTheLimit()
    {
        var shop = await ReadyShopAsync("s07-busy");
        await using var rig = await RigAsync([shop], o => o.Orchestration.MaxBusyRetries = 2);
        await AddTurnAsync(shop, "other-turn-running", DateTimeOffset.UtcNow);
        var message = await CustomerSaysAsync(shop, "hi");
        var job = TurnJob(rig);

        Assert.Equal(AssistantTurnReasons.ConversationBusy, (await job.RunAsync(shop.TenantId, shop.ConversationId, message, 0))!.ReasonCode);
        Assert.Equal((1, TimeSpan.FromSeconds(5)), Next(rig));
        await job.RunAsync(shop.TenantId, shop.ConversationId, message, 1);
        Assert.Equal((2, TimeSpan.FromSeconds(10)), Next(rig));
        await job.RunAsync(shop.TenantId, shop.ConversationId, message, 2);
        Assert.Empty(rig.Scheduler.Turns); // the limit: the sweeper is the safety net from here

        await using (var db = TenantDb(shop.TenantId, out var scope))
        using (scope)
        {
            await db.AssistantTurns.Where(t => t.TurnKey == "other-turn-running").ExecuteDeleteAsync();
        }

        rig.Fake.Enqueue(Text("Namaste!"));
        Assert.Equal(AssistantTurnOutcome.Replied, (await job.RunAsync(shop.TenantId, shop.ConversationId, message, 0))!.Outcome);
    }

    [Fact]
    public async Task ACrashedTurn_IsRestartedByItsRetry_AndAnswersOnce()
    {
        var shop = await ReadyShopAsync("s07-crash");
        await using var rig = await RigAsync([shop]);
        var message = await CustomerSaysAsync(shop, "hello");
        await AddTurnAsync(shop, $"{shop.ConversationId}:{message}", DateTimeOffset.UtcNow.AddMinutes(-10), message); // the worker died mid-turn
        rig.Fake.Enqueue(Text("Namaste!"));

        var result = await TurnJob(rig).RunAsync(shop.TenantId, shop.ConversationId, message, 0);

        Assert.Equal(AssistantTurnOutcome.Replied, result!.Outcome);
        Assert.False(result.Replayed);
        await using var db = Db();
        Assert.Equal(1, await db.AssistantTurns.IgnoreQueryFilters().CountAsync(t => t.TriggerMessageId == message));
        Assert.Equal(1, await AutomationCountAsync(shop));
    }

    // ---- the guard's second pass and entitlement ----

    [Theory]
    [InlineData("connection", AssistantTurnReasons.ConnectionUnavailable)]
    [InlineData("entitlement", AssistantTurnReasons.NotEntitled)]
    [InlineData("spam", AssistantTurnReasons.CustomerSafety)]
    [InlineData("erased", AssistantTurnReasons.CustomerSafety)]
    public async Task TheGuard_RunsAgainRightBeforeEnqueue(string change, string reason)
    {
        var shop = await ReadyShopAsync($"s07-guard-{change}");
        var effect = new SideEffect();
        var entitlement = new SwitchableEntitlement();
        await using var rig = await RigAsync([shop], configureServices: s =>
        {
            s.AddSingleton<IAiChatClient>(new SideEffectChatClient(effect));
            s.AddSingleton<IAssistantEntitlementQuery>(entitlement);
        });
        effect.Action = change switch
        {
            "connection" => () => UpdateAsync(shop, db => db.ChannelConnections.Where(c => c.Id == shop.ConnectionId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ChannelConnectionStatus.Disabled))),
            "entitlement" => () => { entitlement.Entitled = false; return Task.CompletedTask; },
            "spam" => () => UpdateAsync(shop, db => db.Conversations.Where(c => c.Id == shop.ConversationId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ConversationStatus.Spam))),
            _ => () => UpdateAsync(shop, db => db.CustomerChannelIdentities.Where(i => i.Id == shop.IdentityId).ExecuteUpdateAsync(s => s.SetProperty(i => i.ErasedAt, DateTimeOffset.UtcNow)))
        };
        var message = await CustomerSaysAsync(shop, "hello");

        var result = await TurnJob(rig).RunAsync(shop.TenantId, shop.ConversationId, message, 0);

        Assert.Equal((AssistantTurnOutcome.Blocked, reason), (result!.Outcome, result.ReasonCode)); // the model ran; the change landed during it
        await using var db = Db();
        Assert.Equal(0, await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId));
    }

    [Fact]
    public async Task AShopNotOnTheAllowlist_GetsNoJobs_AndAStrayTurnIsBlockedBeforeTheModel()
    {
        var shop = await ReadyShopAsync("s07-not-entitled");
        await using var rig = await RigAsync([]);
        var ids = await IdsAsync(shop);

        await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), "hello"), Echo(ids, Mid(), "Hajur?")));

        Assert.Empty(rig.Scheduler.Turns);
        Assert.Empty(rig.Scheduler.Checks); // M08 behaviour stays: app replies do not take over where the assistant is off
        await using var db = Db();
        var message = await db.Messages.IgnoreQueryFilters().Where(m => m.ConversationId == shop.ConversationId && m.Origin == MessageOrigin.Customer).Select(m => m.Id).SingleAsync();
        var stray = await TurnJob(rig).RunAsync(shop.TenantId, shop.ConversationId, message, 0); // e.g. scheduled before the operator removed the shop
        Assert.Equal(AssistantTurnReasons.NotEntitled, stray!.ReasonCode);
        Assert.Empty(rig.Fake.Requests);
        Assert.Equal(AutomationMode.Automated, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).AutomationMode);

        using var client = rig.Host.CreateClient();
        var readiness = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/readiness", shop.TenantId, TenantRole.Viewer);
        Assert.False(readiness["isActive"]!.GetValue<bool>());
        Assert.False(readiness["checks"]!.AsArray().Single(c => c!["code"]!.GetValue<string>() == "ai_entitled")!["passed"]!.GetValue<bool>());
    }

    // ---- native-app replies ----

    [Fact]
    public async Task AReplyTypedInTheInstagramApp_TakesTheChatOver_AndCancelsQueuedAssistantReplies()
    {
        var shop = await ReadyShopAsync("s07-app-reply");
        await using var rig = await RigAsync([shop]);
        var ids = await IdsAsync(shop);
        var reply = await AnsweredAsync(rig, shop);

        await WebhookAsync(rig, Body(ids, Echo(ids, Mid(), "Hajur, ma aafai herchu.")));
        var check = Assert.Single(rig.Scheduler.Checks);
        Assert.Equal(TimeSpan.FromSeconds(30), check.Delay);

        Assert.True(await CheckJob(rig).RunAsync(check.TenantId, check.MessageId));
        Assert.False(await CheckJob(rig).RunAsync(check.TenantId, check.MessageId)); // a retried check changes nothing

        await using (var db = Db())
        {
            Assert.Equal(AutomationMode.HumanTakeover, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).AutomationMode);
            Assert.Equal(OutboundMessageStatus.Cancelled, await OutboundStatusAsync(reply.OutboundMessageId!));
        }

        var audit = await TakeoverAuditAsync(shop);
        Assert.Equal(("native_app_reply", 1, CommerceActorKind.CommerceSystem), (audit.Trigger, audit.Cancelled, audit.ActorKind));

        await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), "ok thank you")));
        Assert.Equal(AssistantTurnReasons.AutomationPaused, Assert.Single(await RunTurnsAsync(rig))!.ReasonCode);
    }

    [Fact]
    public async Task TheEchoOfOurOwnReply_NeverTakesOver_EvenWhenItArrivesBeforeTheSendIsRecorded()
    {
        var shop = await ReadyShopAsync("s07-own-echo");
        await using var rig = await RigAsync([shop]);
        var ids = await IdsAsync(shop);
        var reply = await AnsweredAsync(rig, shop);
        var providerId = Mid();
        rig.Graph.NextMessageId = providerId;
        rig.Graph.DuringSend = async () => { await WebhookAsync(rig, Body(ids, Echo(ids, providerId, "Namaste!"))); }; // echo overtakes the send result

        await DeliverAsync(rig, reply.OutboundMessageId!);

        var check = Assert.Single(rig.Scheduler.Checks);
        Assert.False(await CheckJob(rig).RunAsync(check.TenantId, check.MessageId));
        await using (var db = Db())
        {
            Assert.Equal(AutomationMode.Automated, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).AutomationMode);
        }

        rig.Scheduler.Checks.Clear();
        await WebhookAsync(rig, Body(ids, Echo(ids, providerId, "Namaste!"))); // a late echo of the recorded send
        Assert.Empty(rig.Scheduler.Checks);
    }

    // ---- sweeper ----

    [Fact]
    public async Task TheSweeper_SchedulesOnlyUnansweredMessagesThatLostTheirTrigger()
    {
        var shop = await ReadyShopAsync("s07-sweep");
        var disabled = await ReadyShopAsync("s07-sweep-disabled");
        var outsider = await ReadyShopAsync("s07-sweep-outsider");
        await SetPolicyEnabledAsync(disabled, false);
        await using var rig = await RigAsync([shop, disabled]);
        var now = DateTimeOffset.UtcNow;

        var lost = await ConversationWithMessagesAsync(shop, [now.AddMinutes(-2)]);
        var answered = await ConversationWithMessagesAsync(shop, [now.AddMinutes(-2)]);
        await AddTurnAsync(shop, "answered", now.AddMinutes(-2), answered.Messages[0], finished: true, conversationId: answered.ConversationId);
        var takenOver = await ConversationWithMessagesAsync(shop, [now.AddMinutes(-2)], c => c.TakeOver());
        var burst = await ConversationWithMessagesAsync(shop, [now.AddMinutes(-5), now.AddMinutes(-2)]);
        var tooRecent = await ConversationWithMessagesAsync(shop, [now.AddSeconds(-10)]);
        var tooOld = await ConversationWithMessagesAsync(shop, [now.AddMinutes(-45)]);
        var beforeRelease = await ConversationWithMessagesAsync(shop, [now.AddMinutes(-3)], c => { c.TakeOver(); c.Release(now.AddMinutes(-1)); });
        var policyOff = await ConversationWithMessagesAsync(disabled, [now.AddMinutes(-2)]);
        var notEntitled = await ConversationWithMessagesAsync(outsider, [now.AddMinutes(-2)]);

        await SweeperJob(rig).SweepAsync();

        var swept = rig.Scheduler.Turns.ToList();
        Assert.Equal(new[] { lost.Messages[0], burst.Messages[1] }.Order(), swept.Select(t => t.MessageId).Order());
        Assert.All(swept, t => Assert.Equal((shop.TenantId, TimeSpan.Zero), (t.TenantId, t.Delay)));
        Assert.DoesNotContain(swept, t => new[] { answered, takenOver, tooRecent, tooOld, beforeRelease, policyOff, notEntitled }.Any(c => c.ConversationId == t.ConversationId));

        rig.Fake.Enqueue(Text("Namaste!"));
        rig.Fake.Enqueue(Text("Namaste!"));
        Assert.All(await RunTurnsAsync(rig), r => Assert.Equal(AssistantTurnOutcome.Replied, r!.Outcome));
        await SweeperJob(rig).SweepAsync();
        Assert.Empty(rig.Scheduler.Turns); // answered: never swept again
    }

    // ---- staff queue ----

    [Fact]
    public async Task TheNeedsPersonQueue_ListsWaitingCustomers_ByLongestWait_WithTheReason()
    {
        var shop = await ReadyShopAsync("s07-queue");
        var other = await ReadyShopAsync("s07-queue-other");
        await using var rig = await RigAsync([shop, other]);
        var now = DateTimeOffset.UtcNow;

        // Escalated by the assistant; its hand-off notice went out; the customer wrote again since.
        var escalated = await TimelineAsync(shop, now, ("customer", -30), ("escalate", -29), ("automation", -29), ("customer", -5));
        // Taken over by staff after the assistant answered; the customer then wrote twice.
        var waiting = await TimelineAsync(shop, now, ("customer", -60), ("automation", -59), ("takeover", -58), ("customer", -20), ("customer", -10));
        await TimelineAsync(shop, now, ("customer", -40), ("takeover", -39), ("staff", -35)); // answered by staff
        await TimelineAsync(shop, now, ("customer", -50), ("escalate", -49), ("staff", -45)); // escalated, then answered
        await TimelineAsync(shop, now, ("customer", -55), ("takeover", -54), ("app", -53)); // answered from the Instagram app
        await TimelineAsync(shop, now, ("customer", -70)); // still the assistant's
        await TimelineAsync(shop, now, ("customer", -80), ("takeover", -79), ("spam", 0)); // spam
        var foreign = await TimelineAsync(other, now, ("customer", -90), ("takeover", -89));

        using var client = rig.Host.CreateClient();
        var queue = (await JsonAsync(client, HttpMethod.Get, "/v1/conversations?needsPerson=true", shop.TenantId, TenantRole.Viewer))["items"]!.AsArray();

        Assert.Equal([escalated, waiting], queue.Select(i => i!["id"]!.GetValue<string>()));
        Assert.Equal("complaint", queue[0]!["escalationCategory"]!.GetValue<string>());
        AssertClose(now.AddMinutes(-29), queue[0]!["waitingSince"]!.GetValue<DateTimeOffset>()); // since the escalation, not the latest message
        AssertClose(now.AddMinutes(-20), queue[1]!["waitingSince"]!.GetValue<DateTimeOffset>()); // since the first unanswered message
        var otherQueue = (await JsonAsync(client, HttpMethod.Get, "/v1/conversations?needsPerson=true", other.TenantId, TenantRole.Viewer))["items"]!.AsArray();
        Assert.Equal([foreign], otherQueue.Select(i => i!["id"]!.GetValue<string>()));
    }

    // ---- isolation ----

    [Fact]
    public async Task Jobs_RunOnlyInsideTheirTenant_AndNotForSuspendedShops()
    {
        var shop = await ReadyShopAsync("s07-iso-a");
        var other = await ReadyShopAsync("s07-iso-b");
        await using var rig = await RigAsync([shop, other]);
        var otherIds = await IdsAsync(other);
        var otherMessage = await CustomerSaysAsync(other, "hi");

        var foreign = await TurnJob(rig).RunAsync(shop.TenantId, other.ConversationId, otherMessage, 0);
        Assert.Equal(AssistantTurnReasons.ConversationNotFound, foreign!.ReasonCode);

        await WebhookAsync(rig, Body(otherIds, Echo(otherIds, Mid(), "Hajur?")));
        var check = Assert.Single(rig.Scheduler.Checks);
        Assert.False(await CheckJob(rig).RunAsync(shop.TenantId, check.MessageId)); // another tenant's echo is invisible

        await UpdateAsync(shop, db => db.Tenants.Where(t => t.Id == shop.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Suspended)));
        var suspendedMessage = await CustomerSaysAsync(shop, "hello");
        Assert.Null(await TurnJob(rig).RunAsync(shop.TenantId, shop.ConversationId, suspendedMessage, 0));

        Assert.Empty(rig.Fake.Requests);
        await using var db = Db();
        Assert.Equal(AutomationMode.Automated, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == other.ConversationId)).AutomationMode);
        Assert.False(await db.AssistantTurns.IgnoreQueryFilters().AnyAsync(t => t.TriggerMessageId == otherMessage || t.TriggerMessageId == suspendedMessage));
    }

    // ---- helpers: host ----

    private async Task<Rig> RigAsync(Shop[] entitled, Action<AiOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
    {
        var scheduler = new RecordingScheduler();
        var graph = new RecordingGraph();
        var gate = new GateHook();
        var host = new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage(), aiEnabled: true, services =>
        {
            services.PostConfigure<AiOptions>(o =>
            {
                foreach (var shop in entitled)
                {
                    o.DataPolicy.SyntheticTenantIds.Add(shop.TenantId);
                    o.Entitlements.AllowedTenantIds.Add(shop.TenantId);
                }

                configure?.Invoke(o);
            });
            services.PostConfigure<InstagramWebhookOptions>(o => o.AppSecret = AppSecret);
            services.AddSingleton<IAssistantTurnScheduler>(scheduler);
            services.AddSingleton<IInstagramGraphClient>(graph);
            services.AddScoped<IConversationGate>(sp => new HookedGate(ActivatorUtilities.CreateInstance<ConversationGate>(sp), gate));
            configureServices?.Invoke(services);
        });

        // The entitled shops' connections can send: credentials sealed with the host's own key.
        var encryption = host.Services.GetRequiredService<ISecretEncryptionService>();
        foreach (var shop in entitled)
        {
            await using var db = TenantDb(shop.TenantId, out var scope);
            using (scope)
            {
                (await db.ChannelConnections.SingleAsync(c => c.Id == shop.ConnectionId)).UpdateCredentials(encryption.Encrypt("page_token_for_tests"));
                await db.SaveChangesAsync();
            }
        }

        return new Rig(host, scheduler, graph, gate);
    }

    private static AssistantTurnJob TurnJob(Rig rig) => rig.Host.Services.GetRequiredService<AssistantTurnJob>();

    private static NativeReplyCheckJob CheckJob(Rig rig) => rig.Host.Services.GetRequiredService<NativeReplyCheckJob>();

    private static AssistantTurnSweepJob SweeperJob(Rig rig) => rig.Host.Services.GetRequiredService<AssistantTurnSweepJob>();

    private static async Task<List<AssistantTurnResult?>> RunTurnsAsync(Rig rig)
    {
        var results = new List<AssistantTurnResult?>();
        while (rig.Scheduler.Turns.TryDequeue(out var turn))
        {
            results.Add(await TurnJob(rig).RunAsync(turn.TenantId, turn.ConversationId, turn.MessageId, turn.Attempt));
        }

        return results;
    }

    private static (int Attempt, TimeSpan Delay) Next(Rig rig)
    {
        Assert.True(rig.Scheduler.Turns.TryDequeue(out var next));
        Assert.Empty(rig.Scheduler.Turns);
        return (next.Attempt, next.Delay);
    }

    /// <summary>A customer DM through the webhook, answered by a scheduled turn whose reply is queued (not yet delivered).</summary>
    private async Task<AssistantTurnResult> AnsweredAsync(Rig rig, Shop shop)
    {
        var ids = await IdsAsync(shop);
        rig.Fake.Enqueue(Text("Namaste! Kasari help garu?"));
        await WebhookAsync(rig, Body(ids, CustomerText(ids, Mid(), "hello")));
        var reply = Assert.Single(await RunTurnsAsync(rig))!;
        Assert.Equal(AssistantTurnOutcome.Replied, reply.Outcome);
        Assert.Equal(OutboundMessageStatus.Queued, await OutboundStatusAsync(reply.OutboundMessageId!));
        return reply;
    }

    private static async Task DeliverAsync(Rig rig, string outboundId)
    {
        using var scope = rig.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IOutboundMessageService>().ProcessDeliveryAsync(outboundId);
    }

    private async Task DeliverAllAsync(Rig rig, Shop shop)
    {
        await using var db = Db();
        var ids = await db.OutboundMessages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == shop.ConversationId && (m.Status == OutboundMessageStatus.Queued || m.Status == OutboundMessageStatus.Failed))
            .OrderBy(m => m.QueuedAt).Select(m => m.Id).ToListAsync();
        foreach (var id in ids) await DeliverAsync(rig, id);
    }

    private static Task<ConversationDetailItem?> TakeOverAsync(Rig rig, Shop shop) =>
        rig.Host.AsTenantAsync(shop.TenantId, async sp =>
        {
            var result = await sp.GetRequiredService<IConversationInboxService>().TakeOverAsync(shop.ConversationId);
            Assert.True(result.IsSuccess, result.Error?.Detail);
            return result.Value;
        });

    private static Task<MessageItem?> StaffReplyAsync(Rig rig, Shop shop, string text) =>
        rig.Host.AsTenantAsync(shop.TenantId, async sp =>
        {
            var result = await sp.GetRequiredService<IConversationReplyService>().SendStaffReplyAsync(shop.ConversationId, text, $"staff-{Guid.NewGuid():N}");
            Assert.True(result.IsSuccess, result.Error?.Detail);
            return result.Value;
        });

    // ---- helpers: webhooks ----

    private static async Task<WebhookIngressResult> WebhookAsync(Rig rig, string body)
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

        Assert.True(result.IsSuccess, result.ErrorReason);
        if (result is { EventId: not null, IsDuplicate: false })
        {
            using var scope = rig.Host.Services.CreateScope();
            var processed = await scope.ServiceProvider.GetRequiredService<IWebhookProcessingService>().ProcessWebhookEventAsync(result.EventId);
            Assert.True(processed.Succeeded);
        }

        return result;
    }

    private static string Mid() => $"mid_{Guid.NewGuid():N}";

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Body(ChannelIds ids, params object[] messaging) =>
        JsonSerializer.Serialize(new { @object = "instagram", entry = new[] { new { id = ids.Account, time = Now(), messaging } } });

    private static object CustomerText(ChannelIds ids, string mid, string text) =>
        new { sender = new { id = ids.Customer }, recipient = new { id = ids.Account }, timestamp = Now(), message = new { mid, text } };

    private static object Echo(ChannelIds ids, string mid, string text) =>
        new { sender = new { id = ids.Account }, recipient = new { id = ids.Customer }, timestamp = Now(), message = new { mid, text, is_echo = true } };

    private static object Reaction(ChannelIds ids, string mid) =>
        new { sender = new { id = ids.Customer }, recipient = new { id = ids.Account }, timestamp = Now(), reaction = new { mid, action = "react", reaction = "love", emoji = "❤" } };

    private static object Read(ChannelIds ids, string mid) =>
        new { sender = new { id = ids.Customer }, recipient = new { id = ids.Account }, timestamp = Now(), read = new { mid } };

    private async Task<ChannelIds> IdsAsync(Shop shop)
    {
        await using var db = Db();
        var account = await db.ChannelConnections.IgnoreQueryFilters().Where(c => c.Id == shop.ConnectionId).Select(c => c.ExternalAccountId).SingleAsync();
        var customer = await db.CustomerChannelIdentities.IgnoreQueryFilters().Where(i => i.Id == shop.IdentityId).Select(i => i.ExternalUserId).SingleAsync();
        return new ChannelIds(account, customer);
    }

    // ---- helpers: data ----

    private async Task<Shop> ReadyShopAsync(string prefix)
    {
        var shop = await SeedShopAsync(fixture, prefix);
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            var policy = AssistantPolicy.CreateDefault(shop.TenantId);
            db.AssistantPolicies.Add(policy);
            await db.SaveChangesAsync();
            await db.AssistantPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.ReviewedAt, DateTimeOffset.UtcNow));
        }

        return shop;
    }

    private Task SetPolicyEnabledAsync(Shop shop, bool enabled) =>
        UpdateAsync(shop, db => db.AssistantPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.Enabled, enabled)));

    private async Task UpdateAsync(Shop shop, Func<AppDbContext, Task<int>> update)
    {
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            await update(db);
        }
    }

    private async Task<string> CustomerSaysAsync(Shop shop, string text)
    {
        await Task.Delay(15); // strictly later than anything before
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            var now = DateTimeOffset.UtcNow;
            var message = Message.CreateInboundText(shop.TenantId, shop.ConversationId, shop.ConnectionId, null, Mid(), text, now, now);
            db.Messages.Add(message);
            (await db.Conversations.SingleAsync(c => c.Id == shop.ConversationId)).RecordInboundMessage(now);
            await db.SaveChangesAsync();
            return message.Id;
        }
    }

    private async Task AddTurnAsync(Shop shop, string key, DateTimeOffset startedAt, string? triggerMessageId = null, bool finished = false, string? conversationId = null)
    {
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            var turn = AssistantTurn.Start(shop.TenantId, conversationId ?? shop.ConversationId, triggerMessageId, key, false, startedAt);
            if (finished) turn.Finish(AssistantTurnOutcome.Replied, "replied", startedAt);
            db.AssistantTurns.Add(turn);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>A new customer's conversation in the shop, with customer messages received at the given times.</summary>
    private async Task<(string ConversationId, string[] Messages)> ConversationWithMessagesAsync(Shop shop, DateTimeOffset[] receivedAt, Action<Conversation>? arrange = null)
    {
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            var identity = Kreyora.Domain.Customers.CustomerChannelIdentity.Create(shop.TenantId, shop.ConnectionId, ChannelType.Instagram, $"igsid_{Guid.NewGuid():N}", receivedAt[0]);
            var conversation = Conversation.Start(shop.TenantId, shop.ConnectionId, shop.StoreId, identity.Id, ChannelType.Instagram);
            var messages = receivedAt.Select(at => Message.CreateInboundText(shop.TenantId, conversation.Id, shop.ConnectionId, null, Mid(), "hello", at, at)).ToArray();
            foreach (var at in receivedAt) conversation.RecordInboundMessage(at);
            arrange?.Invoke(conversation);
            db.AddRange(identity, conversation);
            db.Messages.AddRange(messages);
            await db.SaveChangesAsync();
            return (conversation.Id, messages.Select(m => m.Id).ToArray());
        }
    }

    /// <summary>A new customer's conversation built from timeline steps at minute offsets from <paramref name="now"/>.</summary>
    private async Task<string> TimelineAsync(Shop shop, DateTimeOffset now, params (string Step, int Minutes)[] steps)
    {
        await using var db = TenantDb(shop.TenantId, out var scope);
        using (scope)
        {
            var identity = Kreyora.Domain.Customers.CustomerChannelIdentity.Create(shop.TenantId, shop.ConnectionId, ChannelType.Instagram, $"igsid_{Guid.NewGuid():N}", now.AddHours(-2));
            var conversation = Conversation.Start(shop.TenantId, shop.ConnectionId, shop.StoreId, identity.Id, ChannelType.Instagram);
            db.AddRange(identity, conversation);
            foreach (var (step, minutes) in steps)
            {
                var at = now.AddMinutes(minutes);
                switch (step)
                {
                    case "customer":
                        db.Messages.Add(Message.CreateInboundText(shop.TenantId, conversation.Id, shop.ConnectionId, null, Mid(), "hello?", at, at));
                        conversation.RecordInboundMessage(at);
                        break;
                    case "automation" or "staff":
                        db.Messages.Add(Message.CreateOutboundText(shop.TenantId, conversation.Id, shop.ConnectionId,
                            step == "staff" ? MessageOrigin.Staff : MessageOrigin.Automation, Mid(), "reply", at, at));
                        conversation.RecordOutboundMessage(at);
                        break;
                    case "app":
                        db.Messages.Add(Message.CreateProviderNativeEcho(shop.TenantId, conversation.Id, shop.ConnectionId, null, Mid(), "reply", null, null, at, at));
                        conversation.RecordOutboundMessage(at);
                        break;
                    case "takeover":
                        conversation.TakeOver();
                        break;
                    case "escalate":
                        conversation.Escalate("complaint", at);
                        break;
                    case "spam":
                        conversation.ApplyStatusAction(ConversationStatusAction.MarkSpam);
                        break;
                }
            }

            await db.SaveChangesAsync();
            return conversation.Id;
        }
    }

    private async Task<int> AutomationCountAsync(Shop shop)
    {
        await using var db = Db();
        return await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId && m.Origin == OutboundMessageOrigin.Automation);
    }

    private async Task<OutboundMessageStatus> OutboundStatusAsync(string id)
    {
        await using var db = Db();
        return await db.OutboundMessages.IgnoreQueryFilters().Where(m => m.Id == id).Select(m => m.Status).SingleAsync();
    }

    private async Task<(string Trigger, int Cancelled, int InFlight, CommerceActorKind ActorKind)> TakeoverAuditAsync(Shop shop)
    {
        await using var db = Db();
        var audit = await db.AuditEvents.IgnoreQueryFilters().SingleAsync(a => a.TenantId == shop.TenantId && a.Action == "conversations.takeover" && a.TargetId == shop.ConversationId);
        using var json = JsonDocument.Parse(audit.Metadata!);
        var root = json.RootElement;
        return (root.GetProperty("trigger").GetString()!, root.GetProperty("automationMessagesCancelled").GetInt32(), root.GetProperty("automationMessagesInFlight").GetInt32(), audit.ActorKind);
    }

    private static void AssertClose(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True((expected - actual).Duration() < TimeSpan.FromSeconds(1), $"expected about {expected:O}, got {actual:O}");

    private static AiChatResult Text(string text) =>
        AiChatResult.Success(text, [], AiFinishReason.Stop, new AiUsage(100, 20), "fake", "fake-model", TimeSpan.FromMilliseconds(5));

    private AppDbContext Db() => fixture.CreateDbContext(new TenantContextAccessor());

    private AppDbContext TenantDb(string tenantId, out IDisposable scope)
    {
        var accessor = new TenantContextAccessor();
        scope = accessor.BeginScope(new TenantContext(tenantId, "u", "m", TenantRole.Owner));
        return fixture.CreateDbContext(accessor);
    }

    // ---- test doubles ----

    private sealed record ChannelIds(string Account, string Customer);

    private sealed record ScheduledTurn(string TenantId, string ConversationId, string MessageId, TimeSpan Delay, int Attempt);

    private sealed record ScheduledCheck(string TenantId, string MessageId, TimeSpan Delay);

    /// <summary>A host plus the doubles its jobs, provider and delivery gate report to.</summary>
    private sealed class Rig(AssistantTestHost host, RecordingScheduler scheduler, RecordingGraph graph, GateHook gate) : IAsyncDisposable
    {
        public AssistantTestHost Host => host;

        public RecordingScheduler Scheduler => scheduler;

        public RecordingGraph Graph => graph;

        public GateHook Gate => gate;

        public FakeAiChatClient Fake => host.Services.GetRequiredService<FakeAiChatClient>();

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }

    /// <summary>Captures scheduled jobs so the test decides when (and in which order) they run.</summary>
    private sealed class RecordingScheduler : IAssistantTurnScheduler
    {
        public ConcurrentQueue<ScheduledTurn> Turns { get; } = new();

        public ConcurrentQueue<ScheduledCheck> Checks { get; } = new();

        public void ScheduleTurn(string tenantId, string conversationId, string messageId, TimeSpan delay, int attempt = 0) =>
            Turns.Enqueue(new ScheduledTurn(tenantId, conversationId, messageId, delay, attempt));

        public void ScheduleNativeReplyCheck(string tenantId, string messageId, TimeSpan delay) =>
            Checks.Enqueue(new ScheduledCheck(tenantId, messageId, delay));
    }

    /// <summary>Stand-in for the Graph API: records sends, can run a concurrent action mid-send, returns chosen IDs.</summary>
    private sealed class RecordingGraph : IInstagramGraphClient
    {
        public ConcurrentQueue<(string Recipient, string Text)> SentLog { get; } = new();

        public List<(string Recipient, string Text)> Sent => [.. SentLog];

        public ConcurrentQueue<InstagramSendResult> Script { get; } = new();

        public Func<Task>? DuringSend { get; set; }

        public string? NextMessageId { get; set; }

        public Task<InstagramValidationResult> ValidatePageLinkAsync(string pageAccessToken, string pageId, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstagramValidationResult> ValidateAccountAsync(string pageAccessToken, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<InstagramSendResult> SendTextAsync(string pageAccessToken, string recipientId, string text, string? messagingTag = null, CancellationToken cancellationToken = default)
        {
            SentLog.Enqueue((recipientId, text));
            if (DuringSend is { } action)
            {
                DuringSend = null;
                await action();
            }

            return Script.TryDequeue(out var scripted) ? scripted : InstagramSendResult.Sent(NextMessageId ?? Mid());
        }
    }

    /// <summary>When armed, runs a concurrent action after the real gate answered: the answer is stale by the claim.</summary>
    private sealed class GateHook
    {
        public Func<Task>? Between { get; set; }

        public bool Fired { get; set; }
    }

    private sealed class HookedGate(IConversationGate inner, GateHook hook) : IConversationGate
    {
        public async Task<ConversationGateResult> CheckSendPermissionAsync(string tenantId, string connectionId, string? conversationId, OutboundMessageOrigin origin, CancellationToken cancellationToken = default)
        {
            var result = await inner.CheckSendPermissionAsync(tenantId, connectionId, conversationId, origin, cancellationToken);
            if (hook.Between is { } between)
            {
                hook.Between = null;
                hook.Fired = true;
                await between();
            }

            return result;
        }
    }

    private sealed class SideEffect
    {
        public Func<Task> Action { get; set; } = () => Task.CompletedTask;
    }

    /// <summary>A model that triggers a side effect while "thinking", then answers.</summary>
    private sealed class SideEffectChatClient(SideEffect effect) : IAiChatClient
    {
        public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
        {
            await effect.Action();
            return Text("Namaste! Kasari help garu?");
        }
    }

    private sealed class SwitchableEntitlement : IAssistantEntitlementQuery
    {
        public bool Entitled { get; set; } = true;

        public bool IsEntitled(string tenantId) => Entitled;
    }
}
