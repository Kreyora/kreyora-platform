using System.Net;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Orders;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kreyora.IntegrationTests.Assistant.AssistantHttp;

namespace Kreyora.IntegrationTests.Assistant;

/// <summary>
/// M09-S04 over real PostgreSQL: the five read tools through the registry (trusted context, allowlist, strict schema,
/// minimized results, traces), order-status verification and lockout, cross-tenant isolation, stale data, idempotent
/// reads, the storefront-link resolver, and the owner tool console API.
/// </summary>
public sealed class ReadToolEndpointTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public ReadToolEndpointTests(PostgresFixture fixture) => this.fixture = fixture;

    // ---- catalog tools ----

    [Fact]
    public async Task SearchProducts_ReturnsOnlyPublishedVisibleProducts_WithBandsAndPrices_NoInternalFields()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-search");
        await using var factory = await FactoryAsync();

        var result = await RunAsync(factory, shop, "SearchProducts", """{"query":"red kurta"}""");

        Assert.True(result["ok"]!.GetValue<bool>());
        var product = Assert.Single(result["data"]!["products"]!.AsArray())!;
        Assert.Equal(shop.KurtaId, product["productId"]!.GetValue<string>());
        Assert.Equal(2500m, product["fromPriceNpr"]!.GetValue<decimal>());
        Assert.True(product["available"]!.GetValue<bool>());
        Assert.Equal(["L", "M", "S"], product["options"]!["size"]!.AsArray().Select(v => v!.GetValue<string>()).Order());
        var json = result.ToJsonString();
        foreach (var forbidden in new[] { shop.TenantId, "Secret Prototype", "Draft Item", "KURTA-S", "onHand", "reserved" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }

        var none = await RunAsync(factory, shop, "SearchProducts", """{"query":"laptop charger"}""");
        Assert.Empty(none["data"]!["products"]!.AsArray());
        Assert.NotNull(none["data"]!["hint"]);
        var hidden = await RunAsync(factory, shop, "SearchProducts", """{"query":"Secret Prototype"}""");
        Assert.Empty(hidden["data"]!["products"]!.AsArray());
    }

    [Fact]
    public async Task CheckInventory_ShowsBandsAndCanFulfil_NeverCounts_AndGetPriceShowsSalePrices()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-stock");
        await using var factory = await FactoryAsync();

        var stock = await RunAsync(factory, shop, "CheckInventory", $$"""{"productId":"{{shop.KurtaId}}","quantity":3}""");
        var variants = stock["data"]!["variants"]!.AsArray().ToDictionary(v => v!["name"]!.GetValue<string>(), v => v!);
        Assert.Equal("in_stock", variants["Small"]["availability"]!.GetValue<string>());
        Assert.True(variants["Small"]["canFulfil"]!.GetValue<bool>());
        Assert.Equal("low_stock", variants["Medium"]["availability"]!.GetValue<string>());
        Assert.False(variants["Medium"]["canFulfil"]!.GetValue<bool>());
        Assert.Equal("out_of_stock", variants["Large"]["availability"]!.GetValue<string>());
        Assert.DoesNotContain("\"10\"", stock.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("quantity", stock["data"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        var one = await RunAsync(factory, shop, "GetPrice", $$"""{"productId":"{{shop.KurtaId}}","variantId":"{{shop.SmallId}}"}""");
        var price = Assert.Single(one["data"]!["variants"]!.AsArray())!;
        Assert.Equal(2500m, price["priceNpr"]!.GetValue<decimal>());
        Assert.Equal(3000m, price["compareAtPriceNpr"]!.GetValue<decimal>());
        Assert.Equal("NPR", one["data"]!["currency"]!.GetValue<string>());

        var missingVariant = await RunAsync(factory, shop, "GetPrice", $$"""{"productId":"{{shop.KurtaId}}","variantId":"01J0000000000000000000NOPE"}""");
        Assert.Equal("not_found", missingVariant["error"]!["code"]!.GetValue<string>());
        var hidden = await RunAsync(factory, shop, "GetPrice", $$"""{"productId":"{{shop.HiddenId}}"}""");
        Assert.Equal("not_found", hidden["error"]!["code"]!.GetValue<string>());
    }

    // ---- delivery ----

    [Fact]
    public async Task GetShippingInfo_MatchesZonesGazetteerAndAliases_PricesItemsServerSide()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-ship");
        await using var factory = await FactoryAsync();

        var lalitpur = await RunAsync(factory, shop, "GetShippingInfo", """{"place":"Patan"}""");
        Assert.True(lalitpur["ok"]!.GetValue<bool>());
        Assert.Equal(100m, lalitpur["data"]!["feeNpr"]!.GetValue<decimal>());
        Assert.True(lalitpur["data"]!["cashOnDelivery"]!.GetValue<bool>());
        Assert.True(lalitpur["data"]!["qrPayment"]!.GetValue<bool>());

        var pokhara = await RunAsync(factory, shop, "GetShippingInfo", """{"place":"पोखरा"}""");
        Assert.Equal("Pokhara", pokhara["data"]!["place"]!.GetValue<string>());
        Assert.Equal(200m, pokhara["data"]!["baseFeeNpr"]!.GetValue<decimal>());
        Assert.Equal(5000m, pokhara["data"]!["freeAboveNpr"]!.GetValue<decimal>());
        Assert.False(pokhara["data"]!["cashOnDelivery"]!.GetValue<bool>());

        var withItems = await RunAsync(factory, shop, "GetShippingInfo", $$"""{"place":"Pokhara","items":[{"variantId":"{{shop.SmallId}}","quantity":2}]}""");
        Assert.Equal(5000m, withItems["data"]!["merchandiseSubtotalNpr"]!.GetValue<decimal>());
        Assert.Equal(0m, withItems["data"]!["feeNpr"]!.GetValue<decimal>()); // free above 5,000

        var kaski = await RunAsync(factory, shop, "GetShippingInfo", """{"place":"Kaski district"}""");
        Assert.Equal("needs_more_detail", kaski["error"]!["code"]!.GetValue<string>());
        Assert.Contains("Pokhara", kaski["data"]!["options"]!.ToJsonString());

        var valley = await RunAsync(factory, shop, "GetShippingInfo", """{"place":"Kathmandu valley"}""");
        Assert.Equal("needs_more_detail", valley["error"]!["code"]!.GetValue<string>());

        var jumla = await RunAsync(factory, shop, "GetShippingInfo", """{"place":"Jumla"}""");
        Assert.Equal("place_not_served", jumla["error"]!["code"]!.GetValue<string>());
        var jhapa = await RunAsync(factory, shop, "GetShippingInfo", """{"place":"Jhapa"}"""); // only an inactive rule
        Assert.Equal("place_not_served", jhapa["error"]!["code"]!.GetValue<string>());
        var unknown = await RunAsync(factory, shop, "GetShippingInfo", """{"place":"Atlantis"}""");
        Assert.Equal("place_unknown", unknown["error"]!["code"]!.GetValue<string>());
        var outOfStock = await RunAsync(factory, shop, "GetShippingInfo", $$"""{"place":"Lalitpur","items":[{"variantId":"{{shop.LargeId}}","quantity":1}]}""");
        Assert.Equal("not_found", outOfStock["error"]!["code"]!.GetValue<string>());
    }

    // ---- order status ----

    [Fact]
    public async Task GetOrderStatus_NeedsLinkOrNumberPlusPhoneDigits_RevealsNoPii_AndLocksAfterFiveFailures()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-orders");
        await using var factory = await FactoryAsync();
        var order = await AssistantShopSeed.CreateOrderAsync(factory, shop);

        var ask = await RunAsync(factory, shop, "GetOrderStatus", "{}");
        Assert.Equal("verification_required", ask["error"]!["code"]!.GetValue<string>());

        var verified = await RunAsync(factory, shop, "GetOrderStatus", $$"""{"orderNumber":"{{order.OrderNumber.ToLowerInvariant()}}","phoneLast4":"4567"}""");
        Assert.True(verified["ok"]!.GetValue<bool>(), verified.ToJsonString());
        var status = Assert.Single(verified["data"]!["orders"]!.AsArray())!;
        Assert.Equal(order.OrderNumber, status["orderNumber"]!.GetValue<string>());
        Assert.Equal("pending_confirmation", status["status"]!.GetValue<string>());
        foreach (var pii in new[] { "Sita", AssistantShopSeed.Phone, "Lakeside", "totalNpr", "Kathmandu", shop.TenantId })
        {
            Assert.DoesNotContain(pii, verified.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        }

        var bareUlid = await RunAsync(factory, shop, "GetOrderStatus", $$"""{"orderNumber":"{{order.OrderNumber[4..]}}","phoneLast4":"4567"}""");
        Assert.True(bareUlid["ok"]!.GetValue<bool>());

        // Wrong digits and unknown orders answer the same way; five wrong tries lock the order, even for the right digits.
        var unknown = await RunAsync(factory, shop, "GetOrderStatus", """{"orderNumber":"ORD-01J00000000000000000000000","phoneLast4":"4567"}""");
        Assert.Equal("not_verified", unknown["error"]!["code"]!.GetValue<string>());
        for (var i = 0; i < 5; i++)
        {
            var wrong = await RunAsync(factory, shop, "GetOrderStatus", $$"""{"orderNumber":"{{order.OrderNumber}}","phoneLast4":"000{{i}}"}""");
            Assert.Equal("not_verified", wrong["error"]!["code"]!.GetValue<string>());
            Assert.Equal(unknown["error"]!["message"]!.GetValue<string>(), wrong["error"]!["message"]!.GetValue<string>());
        }

        var locked = await RunAsync(factory, shop, "GetOrderStatus", $$"""{"orderNumber":"{{order.OrderNumber}}","phoneLast4":"4567"}""");
        Assert.Equal("locked", locked["error"]!["code"]!.GetValue<string>());

        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        var actions = await db.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == shop.TenantId && a.TargetId == order.OrderId).Select(a => new { a.Action, a.Metadata }).ToListAsync();
        Assert.Equal(5, actions.Count(a => a.Action == "assistant.order_lookup.failed"));
        Assert.Single(actions, a => a.Action == "assistant.order_lookup.locked");
        Assert.All(actions, a =>
        {
            Assert.DoesNotContain("0004", a.Metadata ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("4567", a.Metadata ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GetOrderStatus_ForALinkedCustomer_ShowsTheirOrdersWithoutQuestions_ButNotSomeoneElses()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-linked");
        await using var factory = await FactoryAsync();
        var order = await AssistantShopSeed.CreateOrderAsync(factory, shop);
        await using (var db = fixture.CreateDbContext(new TenantContextAccessor()))
        {
            // Simulates the S05 link between the chat identity and the customer (no domain method exists yet).
            await db.CustomerChannelIdentities.IgnoreQueryFilters().Where(i => i.Id == shop.IdentityId)
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.CustomerId, order.CustomerId));
        }

        var linked = await RunAsync(factory, shop, "GetOrderStatus", "{}");
        Assert.Equal(order.OrderNumber, Assert.Single(linked["data"]!["orders"]!.AsArray())!["orderNumber"]!.GetValue<string>());

        var other = await AssistantShopSeed.SeedShopAsync(fixture, "s04-linked-other");
        var otherOrder = await AssistantShopSeed.CreateOrderAsync(factory, other);
        var foreign = await RunAsync(factory, shop, "GetOrderStatus", $$"""{"orderNumber":"{{otherOrder.OrderNumber}}","phoneLast4":"4567"}""");
        Assert.Equal("not_verified", foreign["error"]!["code"]!.GetValue<string>()); // another tenant's order never verifies
    }

    // ---- registry: allowlist, schema, isolation, staleness, idempotency, trace ----

    [Fact]
    public async Task Registry_RefusesDisabledUnknownAndWriteTools_AndRejectsInvalidOrSmuggledArguments()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-registry");
        await using var factory = await FactoryAsync();
        await SetAllowedToolsAsync(factory, shop.TenantId, ["SearchProducts"]);

        var definitions = await factory.AsTenantAsync(shop.TenantId, async sp =>
            sp.GetRequiredService<IAssistantToolRegistry>().GetDefinitions((await sp.GetRequiredService<IAssistantToolContextFactory>().ForConversationAsync(shop.ConversationId)).Value!));
        Assert.Equal(["SearchProducts"], definitions.Select(d => d.Name));

        foreach (var tool in new[] { "GetPrice", "QuoteCart", "CreateCheckoutLink", "DropTables" })
        {
            var refused = await RunAsync(factory, shop, tool, $$"""{"productId":"{{shop.KurtaId}}"}""");
            Assert.Equal("tool_not_allowed", refused["error"]!["code"]!.GetValue<string>());
        }

        foreach (var arguments in new[]
        {
            "{}", """{"query":""}""", """{"query":"   "}""", $$"""{"query":"{{new string('x', 65)}}"}""", """{"query":42}""", """{"query":"kurta","limit":9}""",
            """{"query":"kurta","limit":2.5}""", "[1,2]", "{not json",
            $$"""{"query":"kurta","tenantId":"{{shop.TenantId}}"}""", """{"query":"kurta","priceNpr":1}""", """{"query":"kurta","stock":99}"""
        })
        {
            var invalid = await RunAsync(factory, shop, "SearchProducts", arguments);
            Assert.Equal("invalid_arguments", invalid["error"]!["code"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task OtherTenantsIds_AnswerLikeMissingOnes_AndIdenticalCatalogsStaySeparate()
    {
        var shopA = await AssistantShopSeed.SeedShopAsync(fixture, "s04-iso-a");
        var shopB = await AssistantShopSeed.SeedShopAsync(fixture, "s04-iso-b");
        await using var factory = await FactoryAsync();

        var search = await RunAsync(factory, shopA, "SearchProducts", """{"query":"kurta"}""");
        Assert.Equal(shopA.KurtaId, Assert.Single(search["data"]!["products"]!.AsArray())!["productId"]!.GetValue<string>());

        foreach (var (tool, arguments) in new[]
        {
            ("CheckInventory", $$"""{"productId":"{{shopB.KurtaId}}"}"""),
            ("GetPrice", $$"""{"productId":"{{shopB.KurtaId}}"}"""),
            ("GetPrice", $$"""{"productId":"{{shopA.KurtaId}}","variantId":"{{shopB.SmallId}}"}""")
        })
        {
            var foreign = await RunAsync(factory, shopA, tool, arguments);
            Assert.Equal("not_found", foreign["error"]!["code"]!.GetValue<string>());
            Assert.DoesNotContain(shopB.KurtaId, foreign.ToJsonString(), StringComparison.Ordinal);
        }

        var shipping = await RunAsync(factory, shopA, "GetShippingInfo", $$"""{"place":"Lalitpur","items":[{"variantId":"{{shopB.SmallId}}","quantity":1}]}""");
        Assert.Equal("not_found", shipping["error"]!["code"]!.GetValue<string>());

        // A conversation of another tenant cannot be used to build a context.
        var context = await factory.AsTenantAsync(shopA.TenantId, sp => sp.GetRequiredService<IAssistantToolContextFactory>().ForConversationAsync(shopB.ConversationId));
        Assert.True(context.IsFailure);
    }

    [Fact]
    public async Task Results_AreAlwaysCurrent_AndReadsChangeNothing()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-stale");
        await using var factory = await FactoryAsync();
        var priceArgs = $$"""{"productId":"{{shop.KurtaId}}","variantId":"{{shop.SmallId}}"}""";
        var stockArgs = $$"""{"productId":"{{shop.KurtaId}}","variantId":"{{shop.SmallId}}","quantity":5}""";

        var first = await RunAsync(factory, shop, "GetPrice", priceArgs); // also creates the default policy
        var before = await CountRowsAsync();
        var second = await RunAsync(factory, shop, "GetPrice", priceArgs);
        foreach (var (tool, arguments) in new[] { ("SearchProducts", """{"query":"kurta"}"""), ("CheckInventory", stockArgs), ("GetShippingInfo", """{"place":"Lalitpur"}"""), ("GetOrderStatus", "{}") })
        {
            await RunAsync(factory, shop, tool, arguments);
        }

        Assert.Equal(before, await CountRowsAsync()); // idempotent reads: nothing written
        Assert.Equal(first["data"]!.ToJsonString(), second["data"]!.ToJsonString());

        await using (var db = fixture.CreateDbContext(new TenantContextAccessor()))
        {
            await db.ProductVariants.IgnoreQueryFilters().Where(v => v.Id == shop.SmallId).ExecuteUpdateAsync(set => set.SetProperty(v => v.PriceNpr, 2750m));
            await db.InventoryItems.IgnoreQueryFilters().Where(i => i.VariantId == shop.SmallId).ExecuteUpdateAsync(set => set.SetProperty(i => i.ReservedQuantity, 7));
            await db.DeliveryRules.IgnoreQueryFilters().Where(r => r.TenantId == shop.TenantId && r.Name == "Valley").ExecuteUpdateAsync(set => set.SetProperty(r => r.IsActive, false));
        }

        var repriced = await RunAsync(factory, shop, "GetPrice", priceArgs);
        Assert.Equal(2750m, repriced["data"]!["variants"]![0]!["priceNpr"]!.GetValue<decimal>());
        var reserved = await RunAsync(factory, shop, "CheckInventory", stockArgs);
        Assert.Equal("low_stock", reserved["data"]!["variants"]![0]!["availability"]!.GetValue<string>()); // 10 on hand − 7 reserved
        Assert.False(reserved["data"]!["variants"]![0]!["canFulfil"]!.GetValue<bool>());
        Assert.Equal("place_not_served", (await RunAsync(factory, shop, "GetShippingInfo", """{"place":"Lalitpur"}"""))["error"]!["code"]!.GetValue<string>());
        Assert.True(DateTimeOffset.Parse(repriced["asOf"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) >= DateTimeOffset.Parse(first["asOf"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));

        await using (var db = fixture.CreateDbContext(new TenantContextAccessor()))
        {
            await db.StoreProductPublications.IgnoreQueryFilters().Where(p => p.ProductId == shop.KurtaId).ExecuteUpdateAsync(set => set.SetProperty(p => p.Visibility, StoreProductVisibility.Hidden));
        }

        Assert.Empty((await RunAsync(factory, shop, "SearchProducts", """{"query":"kurta"}"""))["data"]!["products"]!.AsArray());
        Assert.Equal("not_found", (await RunAsync(factory, shop, "GetPrice", priceArgs))["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Traces_RecordFieldNamesAndOutcome_NeverValues()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-trace");
        await using var factory = await FactoryAsync();

        var outcome = await ExecuteAsync(factory, shop, "GetOrderStatus", """{"orderNumber":"ORD-01J00000000000000000000000","phoneLast4":"4321","secret":"x"}""");

        Assert.Equal("invalid_arguments", outcome.Trace.Outcome);
        Assert.Equal(["orderNumber", "phoneLast4", "(unknown)"], outcome.Trace.ArgumentFields);
        Assert.Equal(AssistantToolRegistryVersion, outcome.Trace.RegistryVersion);
        Assert.Equal(1, outcome.Trace.ToolVersion);
        Assert.Equal(shop.ConversationId, outcome.Trace.ConversationId);
        var traceJson = System.Text.Json.JsonSerializer.Serialize(outcome.Trace);
        Assert.DoesNotContain("4321", traceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", traceJson, StringComparison.Ordinal);

        var ok = await ExecuteAsync(factory, shop, "SearchProducts", """{"query":"kurta"}""");
        Assert.Equal("ok", ok.Trace.Outcome);
        Assert.Equal(1, ok.Trace.ResultCount);
        Assert.DoesNotContain("kurta", System.Text.Json.JsonSerializer.Serialize(ok.Trace), StringComparison.OrdinalIgnoreCase);
    }

    // ---- product references ----

    [Fact]
    public async Task ProductReferences_MatchOnlyThisShopsStorefrontLinks()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-links");
        var other = await AssistantShopSeed.SeedShopAsync(fixture, "s04-links-other");
        await using var factory = await FactoryAsync();

        var text = $"Yo kati ho? https://{shop.Slug}.kreyora.test/product/{shop.KurtaSlug} ani yo pani http://localhost:3000/store/{shop.Slug}/product/{shop.KurtaSlug}. " +
                   $"Arko: https://{other.Slug}.kreyora.test/product/{other.KurtaSlug} https://evil.example/store/{other.Slug}/product/{other.KurtaSlug} " +
                   $"https://{shop.Slug}.kreyora.test/product/{shop.HiddenSlug}";
        var references = await factory.AsTenantAsync(shop.TenantId, sp => sp.GetRequiredService<IProductReferenceResolver>().ResolveAsync(text));

        var reference = Assert.Single(references);
        Assert.Equal(shop.KurtaId, reference.ProductId);
        Assert.Equal("storefront_link", reference.Source);
        Assert.Empty(await factory.AsTenantAsync(shop.TenantId, sp => sp.GetRequiredService<IProductReferenceResolver>().ResolveAsync("no links here")));
    }

    // ---- owner console API ----

    [Fact]
    public async Task ToolConsole_ListsSchemas_PreviewsAsSeller_AndEnforcesRolesAndCsrf()
    {
        var shop = await AssistantShopSeed.SeedShopAsync(fixture, "s04-console");
        await using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        var catalog = await JsonAsync(client, HttpMethod.Get, "/v1/assistant/tools", shop.TenantId, TenantRole.Viewer);
        Assert.Equal("kreyora-tools.v2", catalog["registryVersion"]!.GetValue<string>());
        Assert.Equal(["SearchProducts", "CheckInventory", "GetPrice", "GetShippingInfo", "GetOrderStatus", "QuoteCart", "ReserveInventory", "ReleaseReservation", "CreateCheckoutLink", "EscalateToHuman"],
            catalog["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()));
        Assert.All(catalog["tools"]!.AsArray(), t => Assert.False(t!["parametersSchema"]!["additionalProperties"]!.GetValue<bool>()));

        var preview = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/tools/GetPrice/preview", shop.TenantId, TenantRole.Admin, new { arguments = new { productId = shop.KurtaId } });
        Assert.True(preview["result"]!["ok"]!.GetValue<bool>());
        Assert.True(preview["trace"]!["sellerPreview"]!.GetValue<bool>());
        var orderPreview = await JsonAsync(client, HttpMethod.Post, "/v1/assistant/tools/GetOrderStatus/preview", shop.TenantId, TenantRole.Owner, new { arguments = new { } });
        Assert.Equal("verification_required", orderPreview["result"]!["error"]!["code"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, "/v1/assistant/tools/CreateOrderDraft/preview", shop.TenantId, TenantRole.Owner, new { arguments = new { } })).StatusCode);
        foreach (var role in new[] { TenantRole.Viewer, TenantRole.Operator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/v1/assistant/tools/GetPrice/preview", shop.TenantId, role, new { arguments = new { productId = shop.KurtaId } })).StatusCode);
        }

        var noCsrf = Request(HttpMethod.Post, "/v1/assistant/tools/GetPrice/preview", shop.TenantId, TenantRole.Owner);
        noCsrf.Content = System.Net.Http.Json.JsonContent.Create(new { arguments = new { productId = shop.KurtaId } });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noCsrf)).StatusCode);
        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "/v1/assistant/tools");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(anonymous)).StatusCode);
    }

    // ---- helpers ----

    private const string AssistantToolRegistryVersion = "kreyora-tools.v2";

    private static async Task<JsonNode> RunAsync(AssistantTestHost factory, AssistantShopSeed.Shop shop, string tool, string arguments) =>
        JsonNode.Parse((await ExecuteAsync(factory, shop, tool, arguments)).ResultJson)!;

    private static Task<AssistantToolOutcome> ExecuteAsync(AssistantTestHost factory, AssistantShopSeed.Shop shop, string tool, string arguments) =>
        factory.AsTenantAsync(shop.TenantId, async sp =>
        {
            var context = await sp.GetRequiredService<IAssistantToolContextFactory>().ForConversationAsync(shop.ConversationId);
            Assert.True(context.IsSuccess);
            return await sp.GetRequiredService<IAssistantToolRegistry>().ExecuteAsync(context.Value!, new AiToolCall("call-1", tool, arguments));
        });

    private static async Task SetAllowedToolsAsync(AssistantTestHost factory, string tenantId, string[] tools)
    {
        await factory.AsTenantAsync(tenantId, sp => sp.GetRequiredService<IAssistantPolicyQuery>().GetEffectiveAsync()); // creates the policy
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AssistantPolicies.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ExecuteUpdateAsync(set => set.SetProperty(p => p.AllowedTools, tools.ToList()));
    }

    private async Task<long> CountRowsAsync()
    {
        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        return await db.AuditEvents.IgnoreQueryFilters().LongCountAsync() + await db.OrderLookupGuards.IgnoreQueryFilters().LongCountAsync()
            + await db.InventoryReservations.IgnoreQueryFilters().LongCountAsync() + await db.Orders.IgnoreQueryFilters().LongCountAsync()
            + await db.Customers.IgnoreQueryFilters().LongCountAsync() + await db.CheckoutSessions.IgnoreQueryFilters().LongCountAsync();
    }

    private async Task<AssistantTestHost> FactoryAsync()
    {
        await using (var db = fixture.CreateDbContext(new TenantContextAccessor()))
        {
            await db.Database.MigrateAsync();
        }

        return new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage());
    }


}
