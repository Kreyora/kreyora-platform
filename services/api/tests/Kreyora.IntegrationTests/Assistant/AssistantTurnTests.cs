using System.Net;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kreyora.IntegrationTests.Assistant.AssistantHttp;
using static Kreyora.IntegrationTests.Assistant.AssistantShopSeed;

namespace Kreyora.IntegrationTests.Assistant;

/// <summary>
/// M09-S06 with a deterministic fake model over real PostgreSQL (ADR-021): the bounded loop, budgets, timeouts,
/// malformed calls, hallucinated facts, provider failure and circuit breaker, kill switches, ownership re-checks,
/// data rule, pre-checks, hand-off, concurrency, redaction, the playground and the turn log API.
/// Turns run as the system (no member role), as S07's background jobs will.
/// </summary>
public sealed class AssistantTurnTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public AssistantTurnTests(PostgresFixture fixture) => this.fixture = fixture;

    // ---- the happy path ----

    [Fact]
    public async Task ATurn_UsesTools_ValidatesTheReply_SendsItThroughTheOutbox_AndLogsWithoutText()
    {
        var shop = await ReadyShopAsync("s06-happy");
        var logs = new CapturingSink();
        await using var factory = Host(shop, configureServices: services => services.AddSingleton<Serilog.Core.ILogEventSink>(logs));
        var fake = Fake(factory);
        fake.Enqueue(Calls(("SearchProducts", """{"query":"red kurta"}""")));
        fake.Enqueue(Calls(("GetPrice", $$"""{"productId":"{{shop.KurtaId}}"}""")));
        fake.Enqueue(Text("Red Cotton Kurta ko price NPR 2,500 ho."));
        var message = await CustomerSaysAsync(shop, "red kurta kati ho?");

        var result = await RunAsync(factory, shop, message);

        Assert.Equal(AssistantTurnOutcome.Replied, result.Outcome);
        Assert.NotNull(result.OutboundMessageId);
        await using var db = Db();
        var outbound = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == result.OutboundMessageId);
        Assert.Equal(OutboundMessageOrigin.Automation, outbound.Origin);
        Assert.Equal("Red Cotton Kurta ko price NPR 2,500 ho.", outbound.TextContent);
        var turn = await db.AssistantTurns.IgnoreQueryFilters().SingleAsync(t => t.Id == result.TurnId);
        Assert.Equal(3, turn.ModelCallCount);
        Assert.Equal(["SearchProducts", "GetPrice"], turn.ToolSteps.Select(s => s.Tool));
        Assert.StartsWith("assistant-system-v1+", turn.PromptVersion);
        Assert.Equal("kreyora-tools.v2", turn.RegistryVersion);
        Assert.NotNull(turn.PolicyVersion);
        Assert.Equal(300, turn.InputTokens);

        var row = await RowJsonAsync(db, turn.Id);
        foreach (var secret in new[] { "red kurta kati", "Red Cotton Kurta ko", "2,500", "2500", shop.KurtaId })
        {
            Assert.DoesNotContain(secret, row, StringComparison.OrdinalIgnoreCase);
        }

        // Logs carry IDs, outcomes and counts only.
        Assert.Contains(logs.Lines, l => l.Contains(result.TurnId!, StringComparison.Ordinal) && l.Contains("Replied", StringComparison.Ordinal));
        foreach (var secret in new[] { "red kurta kati", "Red Cotton Kurta ko", "KRY-" })
        {
            Assert.DoesNotContain(logs.Lines, l => l.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }

        // The model saw the versioned prompt with a canary, and the customer's text as a user message.
        var first = fake.Requests.First();
        Assert.Contains("KRY-", first.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("red kurta kati ho?", first.Messages[^1].Content);
        Assert.True(first.ContainsPersonalData == false); // this shop is on the synthetic allowlist
    }

    [Fact]
    public async Task ADuplicateTrigger_ReplaysTheFinishedTurn_AndSendsOnce()
    {
        var shop = await ReadyShopAsync("s06-duplicate");
        await using var factory = Host(shop);
        Fake(factory).Enqueue(Text("Namaste! Kasari help garu?"));
        var message = await CustomerSaysAsync(shop, "hello");

        var first = await RunAsync(factory, shop, message);
        var second = await RunAsync(factory, shop, message);

        Assert.Equal(first.TurnId, second.TurnId);
        Assert.True(second.Replayed);
        await using var db = Db();
        Assert.Equal(1, await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId));
    }

    // ---- loops, malformed calls, hallucinations ----

    [Fact]
    public async Task AModelThatKeepsCallingTools_StopsAtTheCap_AndHandsOffSafely()
    {
        var shop = await ReadyShopAsync("s06-loop");
        await using var factory = Host(shop);
        var fake = Fake(factory);
        for (var i = 0; i < 10; i++) fake.Enqueue(Calls(("SearchProducts", $$"""{"query":"kurta {{i}}"}""")));
        var message = await CustomerSaysAsync(shop, "kurta?");

        var result = await RunAsync(factory, shop, message);

        Assert.Equal(AssistantTurnOutcome.Fallback, result.Outcome);
        Assert.Equal(AssistantTurnReasons.LoopLimit, result.ReasonCode);
        Assert.Equal(5, fake.Requests.Count); // policy tool steps 4 + 1
        Assert.Equal(AiToolChoice.None, fake.Requests.Last().ToolChoice); // the last call had to answer
        await using var db = Db();
        var conversation = await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId);
        Assert.Equal(AutomationMode.HumanTakeover, conversation.AutomationMode);
        Assert.Equal("tool_unavailable", conversation.EscalationCategory);
        var notice = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.ConversationId == shop.ConversationId);
        Assert.Equal(OutboundMessageOrigin.Handoff, notice.Origin);
        Assert.Equal(OutboundMessageStatus.Queued, notice.Status); // not cancelled by the takeover it accompanies
        Assert.Equal(AssistantFixedTexts(), notice.TextContent);
    }

    [Fact]
    public async Task RepeatedOrMalformedToolCalls_GetErrorResults_AndTheTurnStillFinishes()
    {
        var shop = await ReadyShopAsync("s06-malformed");
        await using var factory = Host(shop);
        var fake = Fake(factory);
        fake.Enqueue(Calls(("SearchProducts", """{"query":"kurta"}"""), ("SearchProducts", """{"query":"kurta"}"""), ("MarkPaid", "{}"), ("GetPrice", "{not json"), ("GetShippingInfo", """{"place":"x"}""")));
        fake.Enqueue(Text("Hami sanga kurta cha, kun size chahiyo?"));
        var message = await CustomerSaysAsync(shop, "kurta cha?");

        var result = await RunAsync(factory, shop, message);

        Assert.Equal(AssistantTurnOutcome.Replied, result.Outcome);
        var toolResults = fake.Requests.Last().Messages.Where(m => m.Role == AiChatRole.Tool).Select(m => m.Content!).ToList();
        Assert.Equal(5, toolResults.Count);
        Assert.Contains("repeated_call", toolResults[1], StringComparison.Ordinal);
        Assert.Contains("tool_not_allowed", toolResults[2], StringComparison.Ordinal);
        Assert.Contains("too_many_calls", toolResults[3], StringComparison.Ordinal); // more than 3 in one response
        Assert.Contains("too_many_calls", toolResults[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInventedPrice_IsBlocked_CorrectedOnce_ThenHandedOff()
    {
        var shop = await ReadyShopAsync("s06-hallucination");
        await using var factory = Host(shop);
        var fake = Fake(factory);
        fake.Enqueue(Text("Kurta ko price NPR 1,999 matra ho!"));
        fake.Enqueue(Text("""Sorry: {"ok":true,"data":{"priceNpr":1999}} so it is NPR 1999."""));
        var message = await CustomerSaysAsync(shop, "kurta price?");

        var result = await RunAsync(factory, shop, message);

        Assert.Equal(AssistantTurnOutcome.Fallback, result.Outcome);
        Assert.Equal(AssistantTurnReasons.ValidationFailed, result.ReasonCode);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Contains("stated a number no tool", fake.Requests.Last().Messages[^1].Content, StringComparison.Ordinal);
        await using var db = Db();
        var turn = await db.AssistantTurns.IgnoreQueryFilters().SingleAsync(t => t.Id == result.TurnId);
        Assert.Contains("ungrounded_number", turn.ValidationCodes);
        Assert.Contains("tool_markup", turn.ValidationCodes);
        Assert.DoesNotContain(await db.OutboundMessages.IgnoreQueryFilters().Where(m => m.ConversationId == shop.ConversationId).Select(m => m.TextContent).ToListAsync(),
            t => t != null && t.Contains("1,999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACorrectedReply_IsSent_AndLinksOnlyFromToolsArePassed()
    {
        var shop = await ReadyShopAsync("s06-corrected");
        await using var factory = Host(shop);
        var fake = Fake(factory);
        fake.Enqueue(Text("Order here: https://evil.example/pay"));
        fake.Enqueue(Text("Hamro team ko sadasya le confirm garnuhunchha."));
        var message = await CustomerSaysAsync(shop, "kasari order garne?");

        var result = await RunAsync(factory, shop, message);

        Assert.Equal(AssistantTurnOutcome.Replied, result.Outcome);
        await using var db = Db();
        Assert.Contains("foreign_link", (await db.AssistantTurns.IgnoreQueryFilters().SingleAsync(t => t.Id == result.TurnId)).ValidationCodes);
        Assert.DoesNotContain("evil.example", (await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == result.OutboundMessageId)).TextContent!, StringComparison.Ordinal);
    }

    // ---- provider failure, circuit, timeouts ----

    [Fact]
    public async Task ProviderFailures_HandOff_AndFiveOpenTheCircuit()
    {
        var shop = await ReadyShopAsync("s06-provider");
        await using var factory = Host(shop);
        var fake = Fake(factory);
        for (var i = 0; i < 5; i++) fake.Enqueue(AiChatResult.Failed(AiFailureKind.ProviderUnavailable, "down"));

        var reasons = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            await ReleaseAsync(shop); // hand-offs take over; let the next turn run
            var message = await CustomerSaysAsync(shop, $"hello {i}");
            reasons.Add((await RunAsync(factory, shop, message)).ReasonCode);
        }

        Assert.All(reasons.Take(5), r => Assert.Equal("provider_failure:providerunavailable", r));
        Assert.Equal(AssistantTurnReasons.CircuitOpen, reasons[5]);
        Assert.Equal(5, fake.Requests.Count); // the open circuit made no model call
    }

    [Fact]
    public async Task ASlowModel_HitsTheTurnDeadline_AndHandsOff()
    {
        var shop = await ReadyShopAsync("s06-slow");
        await using var factory = Host(shop, o => { o.Orchestration.ModelCallTimeoutSeconds = 1; o.Orchestration.TurnDeadlineSeconds = 2; },
            services => services.AddSingleton<IAiChatClient>(new SlowChatClient(TimeSpan.FromSeconds(30))));
        var message = await CustomerSaysAsync(shop, "hello?");
        var started = DateTimeOffset.UtcNow;

        var result = await RunAsync(factory, shop, message);

        Assert.Equal(AssistantTurnOutcome.Fallback, result.Outcome);
        Assert.Equal(AssistantTurnReasons.TurnDeadline, result.ReasonCode);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    // ---- budgets ----

    [Fact]
    public async Task Budgets_PerTurnTokens_ShopDayAndPlatformDay_StopModelCalls()
    {
        var shop = await ReadyShopAsync("s06-tokens");
        await using (var factory = Host(shop, o => o.Orchestration.MaxTokensPerTurn = 1000))
        {
            var fake = Fake(factory);
            for (var i = 0; i < 4; i++) fake.Enqueue(Calls(("SearchProducts", $$"""{"query":"q{{i}}"}"""), 600, 200));
            var result = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "kurta"));
            Assert.Equal(AssistantTurnReasons.TokenBudget, result.ReasonCode);
            Assert.Equal(2, fake.Requests.Count); // 800, then 1,600 ≥ 1,000: no third call
        }

        var daily = await ReadyShopAsync("s06-daily");
        await using (var factory = Host(daily, o => o.Orchestration.MaxTurnsPerTenantPerDay = 1))
        {
            var fake = Fake(factory);
            fake.Enqueue(Text("Namaste!"));
            Assert.Equal(AssistantTurnOutcome.Replied, (await RunAsync(factory, daily, await CustomerSaysAsync(daily, "hi"))).Outcome);
            var second = await RunAsync(factory, daily, await CustomerSaysAsync(daily, "hi again"));
            Assert.Equal(AssistantTurnReasons.TenantDailyLimit, second.ReasonCode);
            Assert.Single(fake.Requests);
        }

        var platform = await ReadyShopAsync("s06-platform");
        var elsewhere = await ReadyShopAsync("s06-platform-other");
        await using (var db = TenantDb(elsewhere.TenantId, out _))
        {
            // Another shop already used today's whole platform allowance.
            var used = AssistantTurn.Start(elsewhere.TenantId, null, null, $"playground:{Guid.NewGuid():N}", true, DateTimeOffset.UtcNow);
            used.RecordModelCall(new TurnModelCall("Primary", "fake", "fake-model", 5, 10, 10, "stop"), 0);
            used.Finish(AssistantTurnOutcome.Replied, "playground", DateTimeOffset.UtcNow);
            db.AssistantTurns.Add(used);
            await db.SaveChangesAsync();
        }

        await using (var factory = Host(platform, o => o.Orchestration.MaxModelCallsPerDay = 1))
        {
            var result = await RunAsync(factory, platform, await CustomerSaysAsync(platform, "hi"));
            Assert.Equal(AssistantTurnReasons.PlatformDailyLimit, result.ReasonCode);
            Assert.Empty(Fake(factory).Requests);
        }
    }

    [Fact]
    public async Task TheReplyRateLimit_SkipsSilently()
    {
        var shop = await ReadyShopAsync("s06-rate");
        await SetPolicyAsync(shop, maxRepliesPerHour: 1);
        await using var factory = Host(shop);
        var fake = Fake(factory);
        fake.Enqueue(Text("Namaste!"));
        await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi"));

        var second = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi again"));

        Assert.Equal(AssistantTurnOutcome.Skipped, second.Outcome);
        Assert.Equal(AssistantTurnReasons.ReplyRateLimit, second.ReasonCode);
        Assert.Single(fake.Requests);
    }

    // ---- kill switches and ownership ----

    [Fact]
    public async Task KillSwitches_PlatformShopAndTakeover_StopBeforeAnyModelCall()
    {
        var shop = await ReadyShopAsync("s06-kill");
        await using (var off = Host(shop, aiEnabled: false))
        {
            var result = await RunAsync(off, shop, await CustomerSaysAsync(shop, "hi"));
            Assert.Equal(AssistantTurnReasons.PlatformDisabled, result.ReasonCode);
            Assert.Empty(Fake(off).Requests);
        }

        await using var factory = Host(shop);
        await SetPolicyAsync(shop, enabled: false);
        Assert.Equal(AssistantTurnReasons.AssistantInactive, (await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi 2"))).ReasonCode);
        await SetPolicyAsync(shop, enabled: true);

        await TakeOverAsync(shop);
        Assert.Equal(AssistantTurnReasons.AutomationPaused, (await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi 3"))).ReasonCode);
        Assert.Empty(Fake(factory).Requests);
        await using var db = Db();
        Assert.Equal(0, await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId));
    }

    [Fact]
    public async Task ATakeoverOrANewerMessageDuringTheTurn_PreventsSending()
    {
        var shop = await ReadyShopAsync("s06-ownership");
        await using (var factory = Host(shop, configureServices: services => services.AddSingleton<IAiChatClient>(new SideEffectChatClient(() => TakeOverAsync(shop)))))
        {
            var result = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi"));
            Assert.Equal(AssistantTurnReasons.TakenOverDuringTurn, result.ReasonCode);
        }

        await ReleaseAsync(shop);
        await using (var factory = Host(shop, configureServices: services => services.AddSingleton<IAiChatClient>(new SideEffectChatClient(async () => { await CustomerSaysAsync(shop, "wait, one more thing"); }))))
        {
            var result = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi again"));
            Assert.Equal(AssistantTurnOutcome.Superseded, result.Outcome);
        }

        await using var db = Db();
        Assert.Equal(0, await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId && m.Origin == OutboundMessageOrigin.Automation));
    }

    [Fact]
    public async Task AnOlderTrigger_IsSupersededImmediately()
    {
        var shop = await ReadyShopAsync("s06-older");
        await using var factory = Host(shop);
        var older = await CustomerSaysAsync(shop, "first");
        await CustomerSaysAsync(shop, "second");

        var result = await RunAsync(factory, shop, older);

        Assert.Equal(AssistantTurnOutcome.Superseded, result.Outcome);
        Assert.Empty(Fake(factory).Requests);
    }

    // ---- data rule, pre-checks, escalation ----

    [Fact]
    public async Task ARealShop_IsNeverSentToTheModelWithoutApproval_AndGetsTheHandOff()
    {
        var shop = await ReadyShopAsync("s06-real");
        await using var factory = Host(shop, synthetic: false);

        var result = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "Mero naam Sita, phone 9841234567"));

        Assert.Equal(AssistantTurnReasons.DataPolicy, result.ReasonCode);
        Assert.Empty(Fake(factory).Requests);
        await using var db = Db();
        Assert.Equal(OutboundMessageOrigin.Handoff, (await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.ConversationId == shop.ConversationId)).Origin);
        Assert.DoesNotContain("9841234567", await RowJsonAsync(db, result.TurnId!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHandOffNotice_StillRespectsTheMessagingWindow()
    {
        var shop = await ReadyShopAsync("s06-window");
        await using var factory = Host(shop, synthetic: false);
        var message = await CustomerSaysAsync(shop, "hello");
        await using (var db = TenantDb(shop.TenantId, out _))
        {
            await db.Conversations.Where(c => c.Id == shop.ConversationId).ExecuteUpdateAsync(set => set.SetProperty(c => c.LastCustomerMessageAt, DateTimeOffset.UtcNow.AddHours(-25)));
        }

        var result = await RunAsync(factory, shop, message);

        Assert.Equal(AssistantTurnOutcome.Fallback, result.Outcome);
        Assert.Null(result.OutboundMessageId);
        await using var check = Db();
        Assert.Contains((await check.AssistantTurns.IgnoreQueryFilters().SingleAsync(t => t.Id == result.TurnId)).ValidationCodes, c => c.StartsWith("enqueue_denied:window_closed", StringComparison.Ordinal));
        Assert.Equal(AutomationMode.HumanTakeover, (await check.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).AutomationMode); // the team still gets it
    }

    [Fact]
    public async Task KeywordsAPersonRequestAndPhotos_AreHandledWithoutTheModel()
    {
        var shop = await ReadyShopAsync("s06-prechecks");
        await SetPolicyAsync(shop, keywords: ["wholesale"]);
        await using var factory = Host(shop);

        var keyword = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "Wholesale rate kati ho?"));
        await ReleaseAsync(shop);
        var person = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "I want to talk to a person"));
        await ReleaseAsync(shop);
        var photo = await RunAsync(factory, shop, await CustomerSendsPhotoAsync(shop));

        Assert.Equal(AssistantTurnReasons.KeywordEscalation, keyword.ReasonCode);
        Assert.Equal(AssistantTurnReasons.PersonRequested, person.ReasonCode);
        Assert.Equal(AssistantTurnOutcome.Replied, photo.Outcome);
        Assert.Equal(AssistantTurnReasons.UnrecognizedMedia, photo.ReasonCode);
        Assert.Empty(Fake(factory).Requests);
        await using var db = Db();
        var photoReply = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == photo.OutboundMessageId);
        Assert.Contains("product name", photoReply.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheModelEscalating_HandsOff_WithOneNoticePerCooldown()
    {
        var shop = await ReadyShopAsync("s06-escalate");
        await using var factory = Host(shop);
        var fake = Fake(factory);
        fake.Enqueue(Calls(("EscalateToHuman", """{"category":"complaint"}""")));

        var first = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "my order arrived broken!"));
        await using (var check = Db())
        {
            Assert.Equal("complaint", (await check.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).EscalationCategory);
        }

        await ReleaseAsync(shop);
        fake.Enqueue(AiChatResult.Failed(AiFailureKind.ProviderUnavailable, "down")); // forces a second hand-off within the cooldown
        var second = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hello??"));

        Assert.Equal(AssistantTurnOutcome.Escalated, first.Outcome);
        Assert.Equal(AssistantTurnReasons.ModelEscalation, first.ReasonCode);
        Assert.NotNull(first.OutboundMessageId);
        Assert.Equal(AssistantTurnOutcome.Fallback, second.Outcome);
        Assert.Null(second.OutboundMessageId); // cooldown: no second notice
        await using var db = Db();
        Assert.Contains(AssistantTurnReasons.FallbackCooldown, (await db.AssistantTurns.IgnoreQueryFilters().SingleAsync(t => t.Id == second.TurnId)).ValidationCodes);
        Assert.Equal(1, await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == shop.ConversationId && m.Origin == OutboundMessageOrigin.Handoff));
    }

    // ---- concurrency and isolation ----

    [Fact]
    public async Task OneTurnPerConversation_AndTwoPerShop()
    {
        var shop = await ReadyShopAsync("s06-busy");
        await using var factory = Host(shop);
        await using (var db = TenantDb(shop.TenantId, out _))
        {
            db.AssistantTurns.Add(AssistantTurn.Start(shop.TenantId, shop.ConversationId, null, "manual-running-1", false, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var busy = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi"));
        Assert.Equal(AssistantTurnReasons.ConversationBusy, busy.ReasonCode);
        Assert.Null(busy.TurnId); // not persisted, so a later retry can run

        await using (var db = TenantDb(shop.TenantId, out _))
        {
            await db.AssistantTurns.Where(t => t.TurnKey == "manual-running-1").ExecuteDeleteAsync();
            db.AssistantTurns.Add(AssistantTurn.Start(shop.TenantId, null, null, "playground:a", true, DateTimeOffset.UtcNow));
            db.AssistantTurns.Add(AssistantTurn.Start(shop.TenantId, null, null, "playground:b", true, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.Equal(AssistantTurnReasons.TenantBusy, (await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi 2"))).ReasonCode);
    }

    [Fact]
    public async Task AnotherTenantsConversation_CannotBeRun_AndTurnLogsAreTenantScoped()
    {
        var shop = await ReadyShopAsync("s06-iso-a");
        var other = await ReadyShopAsync("s06-iso-b");
        await using var factory = Host(shop);
        Fake(factory).Enqueue(Text("Namaste!"));
        var mine = await RunAsync(factory, shop, await CustomerSaysAsync(shop, "hi"));
        var otherMessage = await CustomerSaysAsync(other, "hi");

        var foreign = await factory.AsSystemAsync(shop.TenantId, sp => sp.GetRequiredService<IAssistantTurnService>().RunAsync(other.ConversationId, otherMessage));

        Assert.Equal(AssistantTurnReasons.ConversationNotFound, foreign.ReasonCode);
        using var client = factory.CreateClient();
        var log = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/turns", shop.TenantId, TenantRole.Viewer);
        Assert.Contains(log["items"]!.AsArray(), i => i!["id"]!.GetValue<string>() == mine.TurnId);
        var otherLog = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/turns", other.TenantId, TenantRole.Viewer);
        Assert.Empty(otherLog["items"]!.AsArray());
    }

    // ---- playground ----

    [Fact]
    public async Task ThePlayground_RunsTheRealTurn_DryRunsWrites_AndSendsNothing()
    {
        var shop = await ReadyShopAsync("s06-playground");
        await using var factory = Host(shop, synthetic: false); // made-up text: allowed even for a real shop
        var fake = Fake(factory);
        fake.Enqueue(Calls(("CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}""")));
        fake.Enqueue(Text("Yo link bata order garnus."));
        using var client = factory.CreateClient();

        var result = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/playground", shop.TenantId, TenantRole.Admin,
            new { messages = new[] { new { from = "customer", text = "1 ota small kurta order garna milcha?" } } });

        Assert.Equal("replied", result["outcome"]!.GetValue<string>().ToLowerInvariant());
        Assert.Equal("Yo link bata order garnus.", result["reply"]!.GetValue<string>());
        Assert.True(result["tools"]![0]!["dryRun"]!.GetValue<bool>());
        await using var db = Db();
        Assert.Equal(0, await db.OutboundMessages.IgnoreQueryFilters().CountAsync(m => m.TenantId == shop.TenantId));
        Assert.Equal(0, await db.AssistantCheckoutLinks.IgnoreQueryFilters().CountAsync(l => l.TenantId == shop.TenantId));
        Assert.True((await db.AssistantTurns.IgnoreQueryFilters().SingleAsync(t => t.Id == result["turnId"]!.GetValue<string>())).IsPlayground);

        foreach (var role in new[] { TenantRole.Viewer, TenantRole.Operator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/v1/assistant/playground", shop.TenantId, role, new { messages = new[] { new { from = "customer", text = "hi" } } })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, "/v1/assistant/playground", shop.TenantId, TenantRole.Owner, new { messages = new[] { new { from = "shop", text = "hi" } } })).StatusCode);
    }

    // ---- helpers ----

    private static AiChatResult Text(string text, int input = 100, int output = 20) =>
        AiChatResult.Success(text, [], AiFinishReason.Stop, new AiUsage(input, output), "fake", "fake-model", TimeSpan.FromMilliseconds(5));

    private static AiChatResult Calls(params (string Name, string Arguments)[] calls) => Calls(calls, 100, 20);

    private static AiChatResult Calls((string Name, string Arguments) call, int input, int output) => Calls([call], input, output);

    private static AiChatResult Calls((string Name, string Arguments)[] calls, int input, int output) =>
        AiChatResult.Success(null, [.. calls.Select((c, i) => new AiToolCall($"call-{Guid.NewGuid():N}-{i}", c.Name, c.Arguments))], AiFinishReason.ToolCalls,
            new AiUsage(input, output), "fake", "fake-model", TimeSpan.FromMilliseconds(5));

    private static string AssistantFixedTexts() => Kreyora.Infrastructure.Assistant.Orchestration.AssistantFixedTexts.Handoff("en");

    private static FakeAiChatClient Fake(AssistantTestHost factory) => factory.Services.GetRequiredService<FakeAiChatClient>();

    private static Task<AssistantTurnResult> RunAsync(AssistantTestHost factory, Shop shop, string messageId) =>
        factory.AsSystemAsync(shop.TenantId, sp => sp.GetRequiredService<IAssistantTurnService>().RunAsync(shop.ConversationId, messageId));

    private AssistantTestHost Host(Shop shop, Action<AiOptions>? configure = null, Action<IServiceCollection>? configureServices = null, bool aiEnabled = true, bool synthetic = true) =>
        new(fixture.ConnectionString, new InMemoryStorage(), aiEnabled, services =>
        {
            services.PostConfigure<AiOptions>(o =>
            {
                if (synthetic) o.DataPolicy.SyntheticTenantIds.Add(shop.TenantId);
                configure?.Invoke(o);
            });
            configureServices?.Invoke(services);
        });

    /// <summary>A seeded shop that passes every activation check (store ready, channel connected, policy reviewed).</summary>
    private async Task<Shop> ReadyShopAsync(string prefix)
    {
        var shop = await SeedShopAsync(fixture, prefix);
        await SetPolicyAsync(shop);
        return shop;
    }

    private async Task SetPolicyAsync(Shop shop, bool? enabled = null, int? maxRepliesPerHour = null, string[]? keywords = null)
    {
        await using var db = TenantDb(shop.TenantId, out _);
        if (!await db.AssistantPolicies.AnyAsync())
        {
            db.AssistantPolicies.Add(AssistantPolicy.CreateDefault(shop.TenantId));
            await db.SaveChangesAsync();
        }

        await db.AssistantPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.ReviewedAt, DateTimeOffset.UtcNow));
        if (enabled is { } on) await db.AssistantPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.Enabled, on));
        if (maxRepliesPerHour is { } max) await db.AssistantPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.MaxRepliesPerConversationPerHour, max));
        if (keywords is not null) await db.AssistantPolicies.ExecuteUpdateAsync(set => set.SetProperty(p => p.EscalationKeywords, keywords.ToList()));
    }

    private async Task<string> CustomerSaysAsync(Shop shop, string text)
    {
        await Task.Delay(15); // strictly later than anything before
        await using var db = TenantDb(shop.TenantId, out _);
        var now = DateTimeOffset.UtcNow;
        var message = Message.CreateInboundText(shop.TenantId, shop.ConversationId, shop.ConnectionId, null, $"mid_{Guid.NewGuid():N}", text, now, now);
        db.Messages.Add(message);
        (await db.Conversations.SingleAsync(c => c.Id == shop.ConversationId)).RecordInboundMessage(now);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task<string> CustomerSendsPhotoAsync(Shop shop)
    {
        await Task.Delay(15);
        await using var db = TenantDb(shop.TenantId, out _);
        var now = DateTimeOffset.UtcNow;
        var message = Message.CreateInboundMedia(shop.TenantId, shop.ConversationId, shop.ConnectionId, null, $"mid_{Guid.NewGuid():N}", "https://cdn.example/x.jpg", "image", null, now, now);
        db.Messages.Add(message);
        (await db.Conversations.SingleAsync(c => c.Id == shop.ConversationId)).RecordInboundMessage(now);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task TakeOverAsync(Shop shop)
    {
        await using var db = TenantDb(shop.TenantId, out _);
        (await db.Conversations.SingleAsync(c => c.Id == shop.ConversationId)).TakeOver();
        await db.SaveChangesAsync();
    }

    private async Task ReleaseAsync(Shop shop)
    {
        await using var db = TenantDb(shop.TenantId, out _);
        (await db.Conversations.SingleAsync(c => c.Id == shop.ConversationId)).Release();
        await db.SaveChangesAsync();
    }

    private static async Task<string> RowJsonAsync(AppDbContext db, string turnId) =>
        await db.Database.SqlQuery<string>($"SELECT row_to_json(t)::text AS \"Value\" FROM assistant_turns t WHERE id = {turnId}").SingleAsync();

    private AppDbContext Db() => fixture.CreateDbContext(new TenantContextAccessor());

    private AppDbContext TenantDb(string tenantId, out IDisposable scope)
    {
        var accessor = new TenantContextAccessor();
        scope = accessor.BeginScope(new TenantContext(tenantId, "u", "m", TenantRole.Owner));
        return fixture.CreateDbContext(accessor);
    }

    /// <summary>Captures every rendered log event (message + properties) from the host's Serilog pipeline.</summary>
    private sealed class CapturingSink : Serilog.Core.ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            Lines.Enqueue(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " +
                string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}")) + (logEvent.Exception is null ? string.Empty : " " + logEvent.Exception));
        }
    }

    /// <summary>A model that never answers in time (honours cancellation).</summary>
    private sealed class SlowChatClient(TimeSpan delay) : IAiChatClient
    {
        public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return Text("too late");
        }
    }

    /// <summary>A model that triggers a side effect (takeover, new message) while "thinking", then answers.</summary>
    private sealed class SideEffectChatClient(Func<Task> sideEffect) : IAiChatClient
    {
        public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
        {
            await sideEffect();
            return Text("Namaste! Kasari help garu?");
        }
    }
}
