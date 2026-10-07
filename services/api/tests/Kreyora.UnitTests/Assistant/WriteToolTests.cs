using System.Text.Json;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant.Tools;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.UnitTests.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kreyora.UnitTests.Assistant;

/// <summary>M09-S05 (ADR-020): write-tool schemas, registry gates, idempotency keys, link/action/escalation domain rules.</summary>
public sealed class WriteToolTests
{
    private static readonly IAssistantTool[] WriteTools = [new QuoteCartTool(), new ReserveInventoryTool(), new ReleaseReservationTool(), new CreateCheckoutLinkTool(), new EscalateToHumanTool()];

    // ---- matrix and schemas ----

    [Fact]
    public void TheMatrix_IsExactlyFiveWriteTools_WithStrictSchemas_AndNoOrderDrafts()
    {
        Assert.Equal(["QuoteCart", "ReserveInventory", "ReleaseReservation", "CreateCheckoutLink"], AssistantPolicy.WriteTools);
        Assert.Equal(AssistantPolicy.WriteTools.Append(AssistantPolicy.AlwaysAllowedTool), WriteTools.Select(t => t.Name));
        foreach (var tool in WriteTools)
        {
            using var schema = JsonDocument.Parse(tool.ParametersSchema);
            Assert.Empty(ToolSchemaValidator.UnsupportedKeywords(schema.RootElement));
            Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
            using var smuggled = JsonDocument.Parse("""{"paid":true,"priceNpr":1,"status":"fulfilled","tenantId":"x"}""");
            var errors = ToolSchemaValidator.Validate(schema.RootElement, smuggled.RootElement);
            Assert.Contains(errors, e => e.StartsWith("paid", StringComparison.Ordinal));
            Assert.Contains(errors, e => e.StartsWith("tenantId", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("QuoteCart", """{"items":[{"variantId":"V1","quantity":6}],"place":"x"}""", "items[0].quantity: above maximum")]
    [InlineData("QuoteCart", """{"items":[],"place":"x"}""", "items: too few items")]
    [InlineData("ReserveInventory", """{"items":[{"variantId":"V1","quantity":1},{"variantId":"V2","quantity":1},{"variantId":"V3","quantity":1},{"variantId":"V4","quantity":1},{"variantId":"V5","quantity":1},{"variantId":"V6","quantity":1}]}""", "items: too many items")]
    [InlineData("ReserveInventory", """{"items":[{"variantId":"V1","quantity":1}],"confirmationId":"bad id!"}""", "confirmationId: invalid format")]
    [InlineData("EscalateToHuman", """{"category":"bored"}""", "category: not an allowed value")]
    [InlineData("CreateCheckoutLink", """{"items":[{"variantId":"V1"}]}""", "items[0].quantity: required")]
    public void MalformedWriteArguments_AreRejectedByField(string toolName, string arguments, string expected)
    {
        var tool = WriteTools.Single(t => t.Name == toolName);
        using var schema = JsonDocument.Parse(tool.ParametersSchema);
        using var value = JsonDocument.Parse(arguments);
        Assert.Contains(expected, ToolSchemaValidator.Validate(schema.RootElement, value.RootElement));
    }

    // ---- registry gates ----

    [Fact]
    public async Task WriteTools_NeedAConversation_StopAfterTakeover_ButEscalationStillAnswers()
    {
        var calls = new List<string>();
        var registry = Registry(calls, "QuoteCart", "EscalateToHuman", "SearchProducts");

        var noConversation = await registry.ExecuteAsync(Context(conversationId: null), Call("QuoteCart", """{"items":[{"variantId":"V1","quantity":1}],"place":"x"}"""));
        var paused = await registry.ExecuteAsync(Context(automation: false), Call("QuoteCart", """{"items":[{"variantId":"V1","quantity":1}],"place":"x"}"""));
        var escalation = await registry.ExecuteAsync(Context(automation: false), Call("EscalateToHuman", """{"category":"other"}"""));
        var read = await registry.ExecuteAsync(Context(automation: false), Call("SearchProducts", """{"query":"kurta"}"""));

        Assert.Equal("conversation_required", noConversation.Trace.Outcome);
        Assert.Equal("automation_paused", paused.Trace.Outcome);
        Assert.Equal("ok", escalation.Trace.Outcome);
        Assert.Equal("ok", read.Trace.Outcome);
        Assert.Equal(["EscalateToHuman", "SearchProducts"], calls);
        Assert.Equal(["SearchProducts"], registry.GetDefinitions(Context(automation: false)).Select(d => d.Name)); // nothing to act with after takeover
        Assert.Equal(["SearchProducts", "QuoteCart", "EscalateToHuman"], registry.GetDefinitions(Context()).Select(d => d.Name));
    }

    [Fact]
    public async Task SellerPreview_MarksWriteToolsAsDryRun_ExceptQuoteCart()
    {
        var registry = Registry([], "QuoteCart", "CreateCheckoutLink");
        var preview = Context(conversationId: null, preview: true);

        var quote = await registry.ExecuteAsync(preview, Call("QuoteCart", """{"items":[{"variantId":"V1","quantity":1}],"place":"x"}"""));
        var link = await registry.ExecuteAsync(preview, Call("CreateCheckoutLink", """{"items":[{"variantId":"V1","quantity":1}]}"""));

        Assert.False(quote.Trace.DryRun);
        Assert.True(link.Trace.DryRun);
    }

    [Fact]
    public void IdempotencyKeys_IgnorePropertyOrder_ButNotTurnsToolsOrValues()
    {
        var context = Context(turnId: "turn-1");
        using var a = JsonDocument.Parse("""{"items":[{"variantId":"V1","quantity":1}],"place":"x"}""");
        using var b = JsonDocument.Parse("""{"place":"x","items":[{"quantity":1,"variantId":"V1"}]}""");
        using var c = JsonDocument.Parse("""{"place":"x","items":[{"quantity":2,"variantId":"V1"}]}""");
        var call = new AiToolCall("call-a", "QuoteCart", "{}");

        var key = AssistantToolRegistry.IdempotencyKey(context, call, "QuoteCart", a.RootElement);
        Assert.Equal(key, AssistantToolRegistry.IdempotencyKey(context, call with { Id = "call-b" }, "QuoteCart", b.RootElement)); // same turn
        Assert.NotEqual(key, AssistantToolRegistry.IdempotencyKey(context, call, "QuoteCart", c.RootElement));
        Assert.NotEqual(key, AssistantToolRegistry.IdempotencyKey(context with { TurnId = "turn-2" }, call, "QuoteCart", a.RootElement));
        Assert.NotEqual(key, AssistantToolRegistry.IdempotencyKey(context, call, "CreateCheckoutLink", a.RootElement));
        Assert.NotEqual(key, AssistantToolRegistry.IdempotencyKey(context with { ConversationId = "conv-2" }, call, "QuoteCart", a.RootElement));
    }

    // ---- domain rules ----

    [Fact]
    public void CheckoutLinks_StoreOnlyATokenHash_AreBounded_AndUsedOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var (link, token) = AssistantCheckoutLink.Create("t", "s", "c", "i", [new("V2", 1), new("V1", 2)], now.AddHours(24), now);

        Assert.True(token.Length >= 20);
        Assert.Equal(AssistantCheckoutLink.HashToken(token), link.TokenHash);
        Assert.DoesNotContain(token, link.TokenHash, StringComparison.Ordinal);
        Assert.Equal("V1x2;V2x1", link.LinesFingerprint);
        Assert.True(link.IsLive(now));
        Assert.False(link.IsLive(now.AddHours(25)));
        Assert.True(link.MarkUsed("o1", now));
        Assert.False(link.MarkUsed("o2", now));
        Assert.False(link.IsLive(now));
        Assert.NotEqual(token, AssistantCheckoutLink.Create("t", "s", "c", "i", [new("V1", 1)], now.AddHours(1), now).Token);

        Assert.Throws<ArgumentOutOfRangeException>(() => AssistantCheckoutLink.Create("t", "s", "c", "i", [new("V1", 6)], now.AddHours(1), now));
        Assert.Throws<ArgumentException>(() => AssistantCheckoutLink.Create("t", "s", "c", "i", [new("V1", 1), new("V1", 1)], now.AddHours(1), now));
        Assert.Throws<ArgumentException>(() => AssistantCheckoutLink.Create("t", "s", "c", "i", [], now.AddHours(1), now));
    }

    [Fact]
    public void Proposals_ConfirmOnce_ForTheSameChatToolAndItems_BeforeExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var proposal = AssistantAction.Propose("t", "c1", "ReserveInventory", "k", "V1x1", now.AddMinutes(10));

        Assert.True(proposal.CanConfirm("c1", "ReserveInventory", "V1x1", now));
        Assert.False(proposal.CanConfirm("c2", "ReserveInventory", "V1x1", now));
        Assert.False(proposal.CanConfirm("c1", "ReserveInventory", "V1x2", now));
        Assert.False(proposal.CanConfirm("c1", "ReserveInventory", "V1x1", now.AddMinutes(11)));
        proposal.Confirm(now);
        Assert.False(proposal.CanConfirm("c1", "ReserveInventory", "V1x1", now));
    }

    [Fact]
    public void Escalation_TakesOverOnce_KeepsTheFirstReason_AndReleaseClearsIt()
    {
        var conversation = Conversation.Start("t", "conn", "store", "identity", ChannelType.Instagram);
        var now = DateTimeOffset.UtcNow;

        Assert.True(conversation.Escalate("refund_or_exchange", now));
        Assert.False(conversation.IsAutomationActive);
        Assert.Equal(ConversationStatus.HumanAssigned, conversation.Status);
        Assert.False(conversation.Escalate("complaint", now));
        Assert.Equal("refund_or_exchange", conversation.EscalationCategory);
        Assert.True(conversation.Release());
        Assert.Null(conversation.EscalationCategory);
        Assert.Null(conversation.EscalatedAt);
    }

    [Fact]
    public void ChatIdentities_LinkToACustomerOnce_NeverWhenErased_AndErasureUnlinks()
    {
        var now = DateTimeOffset.UtcNow;
        var identity = CustomerChannelIdentity.Create("t", "conn", ChannelType.Instagram, "igsid", now);

        Assert.True(identity.LinkCustomer("customer-1"));
        Assert.False(identity.LinkCustomer("customer-2"));
        Assert.Equal("customer-1", identity.CustomerId);
        Assert.True(identity.Erase(now));
        Assert.Null(identity.CustomerId);
        Assert.False(identity.LinkCustomer("customer-3"));
    }

    [Fact]
    public void Validator_RejectsBadWriteToolSettings()
    {
        var options = new AiOptions { Tools = new AiToolOptions { CheckoutLinkHours = 0, MaxLiveLinksPerConversation = 99, MaxHoldsPerConversationPerDay = 0, ConfirmationMinutes = 120 } };

        var errors = AiOptionsValidator.Errors(options).ToList();

        Assert.Contains(errors, e => e.Contains("CheckoutLinkHours", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxLiveLinksPerConversation", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxHoldsPerConversationPerDay", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("ConfirmationMinutes", StringComparison.Ordinal));
        Assert.Empty(AiOptionsValidator.Errors(new AiOptions()));
    }

    // ---- helpers ----

    private static AiToolCall Call(string name, string arguments) => new($"call-{Guid.NewGuid():N}", name, arguments);

    private static AssistantToolContext Context(string? conversationId = "conv-1", bool automation = true, bool preview = false, string? turnId = null) =>
        new("tenant-1", "store-1", conversationId, conversationId is null ? null : "identity-1", null,
            [.. AssistantPolicy.ReadTools, .. AssistantPolicy.WriteTools, AssistantPolicy.AlwaysAllowedTool], preview, automation, turnId);

    private static AssistantToolRegistry Registry(List<string> calls, params string[] names)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        var provider = services.BuildServiceProvider();
        IAssistantTool[] tools = [.. names.Select(name => (IAssistantTool)new RecordingTool(name, WriteTools.Concat<IAssistantTool>([new SearchProductsTool()]).Single(t => t.Name == name).ParametersSchema, calls))];
        return new AssistantToolRegistry(tools, provider.GetRequiredService<IServiceScopeFactory>(), new StaticOptionsMonitor<AiOptions>(new AiOptions()),
            new SystemTime(), NullLogger<AssistantToolRegistry>.Instance);
    }

    private sealed class SystemTime : ITimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class RecordingTool(string name, string schema, List<string> calls) : IAssistantTool
    {
        public string Name => name;

        public int Version => 1;

        public string Description => "test";

        public string ParametersSchema => schema;

        public Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
        {
            lock (calls) calls.Add(name);
            return Task.FromResult(AssistantToolResult.Success(new { }, 0));
        }
    }
}
