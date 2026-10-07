using System.Text.Json;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant.Tools;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.UnitTests.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kreyora.UnitTests.Assistant;

/// <summary>M09-S04: schema validation, registry rules (allowlist, timeout, failures, envelope, trace), gazetteer, lockout, links, shared posts.</summary>
public sealed class ReadToolTests
{
    private static readonly IAssistantTool[] RealTools = [new SearchProductsTool(), new CheckInventoryTool(), new GetPriceTool(), new GetShippingInfoTool(), new GetOrderStatusTool()];

    // ---- schema validator ----

    private const string Schema = """
        {"type":"object","properties":{
          "query":{"type":"string","minLength":1,"maxLength":5},
          "limit":{"type":"integer","minimum":1,"maximum":3},
          "code":{"type":"string","pattern":"^[0-9]{4}$"},
          "mode":{"type":"string","enum":["a","b"]},
          "items":{"type":"array","maxItems":2,"items":{"type":"object","properties":{"id":{"type":"string"}},"required":["id"],"additionalProperties":false}}
        },"required":["query"],"additionalProperties":false}
        """;

    [Theory]
    [InlineData("""{"query":"abc"}""")]
    [InlineData("""{"query":"abc","limit":3,"code":"1234","mode":"b","items":[{"id":"x"}]}""")]
    public void Validator_AcceptsValidArguments(string json) => Assert.Empty(Validate(json));

    [Theory]
    [InlineData("{}", "query: required")]
    [InlineData("""{"query":null}""", "query: required")]
    [InlineData("""{"query":""}""", "query: too short")]
    [InlineData("""{"query":"  "}""", "query: too short")]
    [InlineData("""{"query":"abcdef"}""", "query: too long")]
    [InlineData("""{"query":1}""", "query: must be a string")]
    [InlineData("""{"query":"a","limit":0}""", "limit: below minimum")]
    [InlineData("""{"query":"a","limit":4}""", "limit: above maximum")]
    [InlineData("""{"query":"a","limit":1.5}""", "limit: must be an integer")]
    [InlineData("""{"query":"a","limit":"2"}""", "limit: must be an integer")]
    [InlineData("""{"query":"a","code":"12a4"}""", "code: invalid format")]
    [InlineData("""{"query":"a","mode":"c"}""", "mode: not an allowed value")]
    [InlineData("""{"query":"a","items":[{"id":"1"},{"id":"2"},{"id":"3"}]}""", "items: too many items")]
    [InlineData("""{"query":"a","items":[{}]}""", "items[0].id: required")]
    [InlineData("""{"query":"a","items":[{"id":"1","price":5}]}""", "items[0].price: not allowed")]
    [InlineData("""{"query":"a","tenantId":"T1"}""", "tenantId: not allowed")]
    [InlineData("""[1]""", "$: must be an object")]
    public void Validator_RejectsInvalidArguments_ByFieldName_WithoutEchoingValues(string json, string expected)
    {
        var errors = Validate(json);

        Assert.Contains(expected, errors);
        Assert.DoesNotContain(errors, e => e.Contains("T1", StringComparison.Ordinal) || e.Contains("abcdef", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryToolSchema_UsesOnlyEnforcedKeywords_AndRejectsUnknownFields()
    {
        foreach (var tool in RealTools)
        {
            using var schema = JsonDocument.Parse(tool.ParametersSchema);
            Assert.Empty(ToolSchemaValidator.UnsupportedKeywords(schema.RootElement));
            Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
            Assert.Contains(tool.Name, AssistantPolicy.ReadTools);
            Assert.Equal(1, tool.Version);
            using var smuggled = JsonDocument.Parse("""{"tenantId":"x","priceNpr":1,"stock":5}""");
            Assert.Contains(ToolSchemaValidator.Validate(schema.RootElement, smuggled.RootElement), e => e.StartsWith("tenantId", StringComparison.Ordinal));
        }

        Assert.Equal(AssistantPolicy.ReadTools, RealTools.Select(t => t.Name));
    }

    // ---- registry ----

    [Fact]
    public async Task Registry_RunsAllowedTools_InTheContextsTenant_AndWrapsTheResult()
    {
        var tool = new ScriptedTool("SearchProducts", (sp, _) =>
            Task.FromResult(AssistantToolResult.Success(new { tenant = sp.GetRequiredService<ITenantContextAccessor>().RequireCurrent().TenantId }, 1)));
        var registry = Registry(tool);

        var outcome = await registry.ExecuteAsync(Context(["SearchProducts"]), new AiToolCall("c1", "SearchProducts", """{"query":"kurta"}"""));

        var json = JsonNode.Parse(outcome.ResultJson)!;
        Assert.True(json["ok"]!.GetValue<bool>());
        Assert.Equal("tenant-1", json["data"]!["tenant"]!.GetValue<string>());
        Assert.Equal("SearchProducts", json["tool"]!.GetValue<string>());
        Assert.Equal(1, json["version"]!.GetValue<int>());
        Assert.NotNull(json["asOf"]);
        Assert.Equal("ok", outcome.Trace.Outcome);
        Assert.Equal(["query"], outcome.Trace.ArgumentFields);
        Assert.Equal(16, outcome.Trace.ArgumentsHash.Length);
    }

    [Fact]
    public async Task Registry_RefusesToolsOutsideTheAllowlist_AndNeverCallsThem()
    {
        var calls = 0;
        var tool = new ScriptedTool("GetPrice", (_, _) => { calls++; return Task.FromResult(AssistantToolResult.Success(new { }, 0)); });
        var write = new ScriptedTool("QuoteCart", (_, _) => { calls++; return Task.FromResult(AssistantToolResult.Success(new { }, 0)); });
        var retired = new ScriptedTool("CreateOrderDraft", (_, _) => { calls++; return Task.FromResult(AssistantToolResult.Success(new { }, 0)); });
        var registry = Registry(tool, write, retired);

        foreach (var (name, allowed) in new[] { ("GetPrice", new[] { "SearchProducts" }), ("QuoteCart", new[] { "GetPrice" }), ("CreateOrderDraft", new[] { "CreateOrderDraft" }), ("Nope", new[] { "Nope" }) })
        {
            var outcome = await registry.ExecuteAsync(Context(allowed), new AiToolCall("c", name, "{}"));
            Assert.Equal("tool_not_allowed", outcome.Trace.Outcome);
        }

        Assert.Equal(0, calls);
        Assert.Empty(registry.GetDefinitions(Context(["CreateOrderDraft"])));
        Assert.Equal(["GetPrice", "QuoteCart"], registry.GetDefinitions(Context(["GetPrice", "QuoteCart"])).Select(d => d.Name));
    }

    [Fact]
    public async Task Registry_TimesOutSlowTools_AndTheNextCallStillWorks()
    {
        var slow = new ScriptedTool("SearchProducts", async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None); // ignores cancellation on purpose
            return AssistantToolResult.Success(new { }, 0);
        });
        var fast = new ScriptedTool("GetPrice", (_, _) => Task.FromResult(AssistantToolResult.Success(new { ok = 1 }, 1)));
        var registry = Registry(slow, fast);
        var started = DateTimeOffset.UtcNow;

        var timedOut = await registry.ExecuteAsync(Context(["SearchProducts", "GetPrice"]), new AiToolCall("c", "SearchProducts", """{"query":"x"}"""));
        var next = await registry.ExecuteAsync(Context(["SearchProducts", "GetPrice"]), new AiToolCall("c", "GetPrice", """{"productId":"P1"}"""));

        Assert.Equal("timeout", timedOut.Trace.Outcome);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal("ok", next.Trace.Outcome);
    }

    [Fact]
    public async Task Registry_TurnsExceptionsAndBadJsonIntoStableCodes()
    {
        var throwing = new ScriptedTool("SearchProducts", (_, _) => throw new InvalidOperationException("database password=hunter2"));
        var registry = Registry(throwing);

        var failed = await registry.ExecuteAsync(Context(["SearchProducts"]), new AiToolCall("c", "SearchProducts", """{"query":"x"}"""));
        var badJson = await registry.ExecuteAsync(Context(["SearchProducts"]), new AiToolCall("c", "SearchProducts", "{oops"));
        var empty = await registry.ExecuteAsync(Context(["SearchProducts"]), new AiToolCall("c", "SearchProducts", ""));

        Assert.Equal("unavailable", failed.Trace.Outcome);
        Assert.DoesNotContain("hunter2", failed.ResultJson, StringComparison.Ordinal);
        Assert.Equal("invalid_arguments", badJson.Trace.Outcome);
        Assert.Equal("invalid_arguments", empty.Trace.Outcome); // "{}" lacks the required query
        Assert.Contains("query: required", empty.ResultJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_DescribesSchemas_InPolicyOrder_WithEnabledFlags()
    {
        var registry = Registry(RealTools);

        var tools = registry.Describe(["GetPrice"]);

        Assert.Equal(AssistantPolicy.ReadTools, tools.Select(t => t.Name));
        Assert.True(tools.Single(t => t.Name == "GetPrice").EnabledInPolicy);
        Assert.False(tools.Single(t => t.Name == "SearchProducts").EnabledInPolicy);
        Assert.Equal("kreyora-tools.v2", registry.Version);
    }

    // ---- domain rules ----

    [Fact]
    public void Gazetteer_Has77Districts_AndResolvesCitiesAliasesAndScripts()
    {
        Assert.Equal(77, NepalGazetteer.AllDistricts.Count);
        Assert.Equal(77, NepalGazetteer.AllDistricts.Distinct().Count());
        Assert.Equal(new NepalGazetteer.Place("Kaski", "Pokhara"), Assert.Single(NepalGazetteer.Lookup("Pokhara Metropolitan City")));
        Assert.Equal(new NepalGazetteer.Place("Kaski", "Pokhara"), Assert.Single(NepalGazetteer.Lookup("पोखरा")));
        Assert.Equal("Kathmandu", Assert.Single(NepalGazetteer.Lookup("KTM")).District);
        Assert.Equal("Kathmandu", Assert.Single(NepalGazetteer.Lookup("काठमाडौं")).District);
        Assert.Equal("Kavrepalanchok", NepalGazetteer.CanonicalDistrict("Kavre"));
        Assert.Equal("Kaski", NepalGazetteer.CanonicalDistrict("Kaski district"));
        Assert.Equal("Lalitpur", Assert.Single(NepalGazetteer.Lookup("Patan")).District);
        Assert.Equal(3, NepalGazetteer.Lookup("Kathmandu valley").Count);
        Assert.Empty(NepalGazetteer.Lookup("Atlantis"));
        Assert.Empty(NepalGazetteer.Lookup("  "));
        Assert.Equal("pokhara", NepalGazetteer.Key("Pokhara ma"));
    }

    [Fact]
    public void OrderLookupGuard_LocksAtTheFifthFailure_ForTheRestOfTheWindow()
    {
        var start = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
        var guard = OrderLookupGuard.Create("t", "o", start);

        for (var i = 1; i < OrderLookupGuard.MaxFailures; i++) Assert.False(guard.RegisterFailure(start.AddMinutes(i)));
        Assert.False(guard.IsLocked(start.AddMinutes(4)));
        Assert.True(guard.RegisterFailure(start.AddMinutes(5)));
        Assert.True(guard.IsLocked(start.AddHours(23)));
        Assert.False(guard.IsLocked(start.AddHours(24)));

        Assert.False(guard.RegisterFailure(start.AddHours(25))); // a new window starts from zero
        Assert.Equal(1, guard.FailureCount);
        guard.Reset(start.AddHours(26));
        Assert.Equal(0, guard.FailureCount);
    }

    [Theory]
    [InlineData("ORD-01J9Z3K4M5N6P7Q8R9S0T1V2W3", "ORD-01J9Z3K4M5N6P7Q8R9S0T1V2W3")]
    [InlineData(" ord-01j9z3k4m5n6p7q8r9s0t1v2w3 ", "ORD-01J9Z3K4M5N6P7Q8R9S0T1V2W3")]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3", "ORD-01J9Z3K4M5N6P7Q8R9S0T1V2W3")]
    [InlineData("KR-1042", null)]
    [InlineData("ORD-' OR 1=1", null)]
    [InlineData(null, null)]
    public void OrderNumbers_AreNormalized_OrRejected(string? input, string? expected) =>
        Assert.Equal(expected, OrderStatusLookupService.NormalizeOrderNumber(input));

    [Theory]
    [InlineData(0, CustomerAvailability.OutOfStock)]
    [InlineData(-2, CustomerAvailability.OutOfStock)]
    [InlineData(1, CustomerAvailability.LowStock)]
    [InlineData(3, CustomerAvailability.LowStock)]
    [InlineData(4, CustomerAvailability.InStock)]
    public void StockBands_HideExactCounts(int available, CustomerAvailability expected) =>
        Assert.Equal(expected, CustomerCatalogQuery.Band(available, 3));

    // ---- links and shared posts ----

    private static readonly StorefrontLinkOptions Links = new() { PlatformBaseDomain = "kreyora.app", EnableDevelopmentSlugRoutes = false };

    [Theory]
    [InlineData("https://demo.kreyora.app/product/red-kurta", "red-kurta")]
    [InlineData("demo.kreyora.app/product/Red-Kurta", "red-kurta")]
    [InlineData("https://kreyora.app/store/demo/product/red-kurta", "red-kurta")]
    [InlineData("https://www.kreyora.app/store/DEMO/product/red-kurta).", "red-kurta")]
    [InlineData("https://other.kreyora.app/product/red-kurta", null)]
    [InlineData("https://kreyora.app/store/other/product/red-kurta", null)]
    [InlineData("https://evil.example/store/demo/product/red-kurta", null)]
    [InlineData("https://demo.kreyora.app.evil.example/product/red-kurta", null)]
    [InlineData("https://demo.kreyora.app/cart", null)]
    [InlineData("javascript:alert(1)", null)]
    public void StorefrontLinks_MatchOnlyThisShopsProductPages(string link, string? expected) =>
        Assert.Equal(expected, ProductReferenceResolver.ProductSlugFor(link, "demo", Links));

    [Fact]
    public void DevelopmentSlugRoutes_AcceptTheStorePathOnAnyHost()
    {
        var dev = new StorefrontLinkOptions { PlatformBaseDomain = "kreyora.test", EnableDevelopmentSlugRoutes = true };
        Assert.Equal("tee", ProductReferenceResolver.ProductSlugFor("http://localhost:3000/store/demo/product/tee", "demo", dev));
        Assert.Null(ProductReferenceResolver.ProductSlugFor("http://localhost:3000/store/demo/product/tee", "demo", Links));
    }

    [Theory]
    [InlineData("""{"url":"https://cdn/x","reel_video_id":"123456"}""", "reel_video_id:123456")]
    [InlineData("""{"url":"https://cdn/x","ig_post_media_id":17890001}""", "ig_post_media_id:17890001")]
    [InlineData("""{"url":"https://cdn/x","title":"Caption"}""", null)]
    [InlineData("""{"url":"https://cdn/x","something_id":{"nested":1}}""", null)]
    public void SharedPostIdentifiers_AreKeptAsSent_WithoutInterpretation(string payload, string? expected)
    {
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(expected, InstagramChannelProvider.SharedPostIdentifier(json.RootElement));
    }

    [Fact]
    public void Messages_StoreTheSharedPostId_Truncated_AndRedactionClearsIt()
    {
        var now = DateTimeOffset.UtcNow;
        var message = Message.CreateInboundMedia("t", "c", "conn", null, "mid", "https://cdn/x", "ig_reel", null, now, now, "reel_video_id:" + new string('9', 200));

        Assert.Equal(Message.SharedPostIdMaxLength, message.SharedPostId!.Length);
        Assert.True(message.Redact(now));
        Assert.Null(message.SharedPostId);
    }

    [Fact]
    public void Validator_RejectsBadToolSettings()
    {
        var options = new AiOptions { Tools = new AiToolOptions { TimeoutSeconds = 0, LowStockThreshold = 101 } };

        var errors = AiOptionsValidator.Errors(options).ToList();

        Assert.Contains(errors, e => e.Contains("Ai:Tools:TimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Ai:Tools:LowStockThreshold", StringComparison.Ordinal));
    }

    // ---- helpers ----

    private static List<string> Validate(string json)
    {
        using var schema = JsonDocument.Parse(Schema);
        using var value = JsonDocument.Parse(json);
        return [.. ToolSchemaValidator.Validate(schema.RootElement, value.RootElement)];
    }

    private static AssistantToolContext Context(IReadOnlyList<string> allowed) =>
        new("tenant-1", "store-1", "conv-1", "identity-1", null, allowed, IsSellerPreview: false);

    private static AssistantToolRegistry Registry(params IAssistantTool[] tools)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        var provider = services.BuildServiceProvider();
        var options = new AiOptions { Tools = new AiToolOptions { TimeoutSeconds = 1 } };
        return new AssistantToolRegistry(tools, provider.GetRequiredService<IServiceScopeFactory>(), new StaticOptionsMonitor<AiOptions>(options),
            new SystemTime(), NullLogger<AssistantToolRegistry>.Instance);
    }

    private sealed class SystemTime : ITimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    /// <summary>A tool with a real-looking schema whose behavior the test scripts.</summary>
    private sealed class ScriptedTool(string name, Func<IServiceProvider, CancellationToken, Task<AssistantToolResult>> run) : IAssistantTool
    {
        public string Name => name;

        public int Version => 1;

        public string Description => "test";

        public string ParametersSchema => name == "SearchProducts"
            ? new SearchProductsTool().ParametersSchema
            : """{"type":"object","properties":{"productId":{"type":"string"}},"additionalProperties":false}""";

        public Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken) =>
            run(services, cancellationToken);
    }
}
