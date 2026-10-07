using System.Net;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Orders;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kreyora.IntegrationTests.Assistant.AssistantShopSeed;

namespace Kreyora.IntegrationTests.Assistant;

/// <summary>
/// M09-S05 over real PostgreSQL (ADR-020): QuoteCart, two-phase holds, release, checkout links (public read, atomic
/// hold handover, order → chat link), EscalateToHuman; duplicate, malformed, unauthorized, stale, cancellation and
/// cross-tenant cases, and the no-bypass rules.
/// </summary>
public sealed class WriteToolEndpointTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public WriteToolEndpointTests(PostgresFixture fixture) => this.fixture = fixture;

    // ---- QuoteCart ----

    [Fact]
    public async Task QuoteCart_PricesOnTheServer_ResolvesThePlace_AndReturnsAQuoteId()
    {
        var shop = await SeedShopAsync(fixture, "s05-quote");
        await using var factory = await FactoryAsync(shop);

        var quote = await RunAsync(factory, shop, "QuoteCart", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":2}],"place":"पोखरा"}""");

        Assert.True(quote["ok"]!.GetValue<bool>(), quote.ToJsonString());
        var data = quote["data"]!;
        Assert.Equal(5000m, data["merchandiseSubtotalNpr"]!.GetValue<decimal>());
        Assert.Equal(0m, data["deliveryFeeNpr"]!.GetValue<decimal>()); // free above 5,000 in Pokhara
        Assert.Equal(5000m, data["totalNpr"]!.GetValue<decimal>());
        Assert.False(data["cashOnDelivery"]!.GetValue<bool>());
        Assert.NotNull(data["quoteId"]?.GetValue<string>());
        Assert.DoesNotContain("CfDJ", quote.ToJsonString(), StringComparison.Ordinal); // the signed quote never reaches the model

        foreach (var (arguments, code) in new[]
        {
            ($$"""{"items":[{"variantId":"{{shop.HiddenVariantId}}","quantity":1}],"place":"Lalitpur"}""", "not_found"),
            ($$"""{"items":[{"variantId":"{{shop.LargeId}}","quantity":1}],"place":"Lalitpur"}""", "not_found"),
            ($$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}],"place":"Atlantis"}""", "place_unknown"),
            ($$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1,"priceNpr":1}],"place":"Lalitpur"}""", "invalid_arguments"),
            ($$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":6}],"place":"Lalitpur"}""", "invalid_arguments")
        })
        {
            Assert.Equal(code, (await RunAsync(factory, shop, "QuoteCart", arguments))["error"]!["code"]!.GetValue<string>());
        }
    }

    // ---- holds: two-phase confirmation, duplicates, release ----

    [Fact]
    public async Task ReserveInventory_NeedsTheCustomersReplyBeforeHolding_ThenHoldsOnceAndReleases()
    {
        var shop = await SeedShopAsync(fixture, "s05-hold");
        await using var factory = await FactoryAsync(shop, holds: true);
        var items = $$"""{"items":[{"variantId":"{{shop.MediumId}}","quantity":2}]""";

        var proposal = await RunAsync(factory, shop, "ReserveInventory", items + "}");
        Assert.Equal("confirmation_required", proposal["error"]!["code"]!.GetValue<string>());
        var confirmationId = proposal["data"]!["confirmationId"]!.GetValue<string>();
        Assert.Equal(0, await HoldCountAsync(shop));

        // The model claims the customer agreed, but no customer message arrived after the proposal: still not held.
        var tooEarly = await RunAsync(factory, shop, "ReserveInventory", items + $$""","confirmationId":"{{confirmationId}}"}""");
        Assert.Equal("confirmation_required", tooEarly["error"]!["code"]!.GetValue<string>());
        Assert.Equal(0, await HoldCountAsync(shop));

        await CustomerSaysAsync(shop, "huncha, hold garnus");
        var changed = await RunAsync(factory, shop, "ReserveInventory", $$"""{"items":[{"variantId":"{{shop.MediumId}}","quantity":1}],"confirmationId":"{{confirmationId}}"}""");
        Assert.Equal("stale", changed["error"]!["code"]!.GetValue<string>()); // items differ from what was confirmed

        var confirm = items + $$""","confirmationId":"{{confirmationId}}"}""";
        var held = await RunAsync(factory, shop, "ReserveInventory", confirm, turnId: "turn-7");
        var replay = await RunAsync(factory, shop, "ReserveInventory", confirm, turnId: "turn-7");
        Assert.True(held["ok"]!.GetValue<bool>(), held.ToJsonString());
        Assert.Equal(held.ToJsonString(), replay.ToJsonString()); // duplicate call in the same turn replays
        Assert.Equal(1, await HoldCountAsync(shop));
        Assert.Equal("stale", (await RunAsync(factory, shop, "ReserveInventory", confirm, turnId: "turn-8"))["error"]!["code"]!.GetValue<string>()); // used once

        await using (var db = Db())
        {
            var hold = await db.InventoryReservations.IgnoreQueryFilters().SingleAsync(r => r.ReferenceId == shop.ConversationId);
            Assert.Equal(InventoryReservationSource.Conversation, hold.Source);
            Assert.True(hold.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(16));
            Assert.Equal(2, (await db.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.VariantId == shop.MediumId)).ReservedQuantity);
            Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.TenantId == shop.TenantId && a.Action == "inventory.reservation.created" && a.ActorUserId == null));
        }

        // Holding the same item again in a new proposal doesn't stack.
        var again = await RunAsync(factory, shop, "ReserveInventory", items + "}");
        await CustomerSaysAsync(shop, "ho");
        await RunAsync(factory, shop, "ReserveInventory", items + $$""","confirmationId":"{{again["data"]!["confirmationId"]!.GetValue<string>()}}"}""");
        Assert.Equal(1, await HoldCountAsync(shop));

        var released = await RunAsync(factory, shop, "ReleaseReservation", "{}");
        Assert.Single(released["data"]!["released"]!.AsArray());
        Assert.Equal(0, await HoldCountAsync(shop));
        await using var after = Db();
        Assert.Equal(0, (await after.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.VariantId == shop.MediumId)).ReservedQuantity);
    }

    [Fact]
    public async Task Holds_RespectTheDailyCap_AndOtherChatsReservationsCannotBeReleased()
    {
        var shop = await SeedShopAsync(fixture, "s05-cap");
        await using var factory = await FactoryAsync(shop, holds: true, configure: o => o.Tools.MaxHoldsPerConversationPerDay = 1);
        await HoldAsync(factory, shop, shop.MediumId, 1);

        var second = await RunAsync(factory, shop, "ReserveInventory", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}""");
        Assert.Equal("limit_reached", second["error"]!["code"]!.GetValue<string>());

        // A checkout reservation (not this chat's) can't be released through the tool.
        string checkoutReservation;
        var accessor = new TenantContextAccessor();
        using (accessor.BeginScope(new TenantContext(shop.TenantId, "u", "m", TenantRole.Owner)))
        await using (var db = fixture.CreateDbContext(accessor))
        {
            var item = await db.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.VariantId == shop.SmallId);
            var reservation = InventoryReservation.Create(shop.TenantId, item.Id, shop.SmallId, 1, InventoryReservationSource.Checkout, "01J0000000000000000000SESS", null, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);
            item.Reserve(1);
            db.InventoryReservations.Add(reservation);
            await SaveAsTenantAsync(db, shop);
            checkoutReservation = reservation.Id;
        }

        var foreign = await RunAsync(factory, shop, "ReleaseReservation", $$"""{"reservationIds":["{{checkoutReservation}}"]}""");
        Assert.Equal("not_found", foreign["error"]!["code"]!.GetValue<string>());
        await using var check = Db();
        Assert.Equal(InventoryReservationState.Active, (await check.InventoryReservations.IgnoreQueryFilters().SingleAsync(r => r.Id == checkoutReservation)).State);
    }

    // ---- checkout links: public read, handover, order linking, stale ----

    [Fact]
    public async Task CheckoutLink_EndsInTheNormalCheckout_HandsHeldUnitsOverAtomically_AndLinksTheOrderToTheChat()
    {
        var shop = await SeedShopAsync(fixture, "s05-link");
        await using var factory = await FactoryAsync(shop, holds: true);
        using var client = factory.CreateClient();
        await HoldAsync(factory, shop, shop.MediumId, 2); // the last 2 units

        var created = await RunAsync(factory, shop, "CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.MediumId}}","quantity":2}]}""");
        Assert.True(created["ok"]!.GetValue<bool>(), created.ToJsonString());
        var url = created["data"]!["url"]!.GetValue<string>();
        Assert.Contains($"/store/{shop.Slug}/link/", url, StringComparison.Ordinal);
        var token = url[(url.LastIndexOf('/') + 1)..];
        await using (var db = Db())
        {
            var link = await db.AssistantCheckoutLinks.IgnoreQueryFilters().SingleAsync(l => l.ConversationId == shop.ConversationId);
            Assert.NotEqual(token, link.TokenHash);
            Assert.Equal(AssistantCheckoutLink.HashToken(token), link.TokenHash);
        }

        // The storefront reads the link: current prices, no customer data, never cached.
        var publicRead = await client.GetAsync($"/public/v1/dev/stores/{shop.Slug}/assistant-links/{token}");
        Assert.Equal(HttpStatusCode.OK, publicRead.StatusCode);
        Assert.Equal("no-store", publicRead.Headers.CacheControl?.ToString());
        var linkJson = JsonNode.Parse(await publicRead.Content.ReadAsStringAsync())!;
        var line = Assert.Single(linkJson["items"]!.AsArray())!;
        Assert.Equal(2500m, line["unitPriceNpr"]!.GetValue<decimal>());
        Assert.True(line["available"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/public/v1/dev/stores/{shop.Slug}/assistant-links/wrong-token")).StatusCode);

        // Anyone else sees the units as reserved; this customer (with the link) can quote and buy them.
        var stranger = await factory.AsTenantAsync(shop.TenantId, sp => sp.GetRequiredService<IStorefrontQuoteService>()
            .CreateQuoteAsync(new StorefrontQuoteRequest([new StorefrontQuoteLineRequest(shop.MediumId, 1)], Kathmandu())));
        Assert.True(stranger.IsFailure);

        var order = await CheckoutAsync(factory, shop, shop.MediumId, 2, token);
        await using (var db = Db())
        {
            var holds = await db.InventoryReservations.IgnoreQueryFilters().Where(r => r.TenantId == shop.TenantId && r.VariantId == shop.MediumId).ToListAsync();
            Assert.Equal(InventoryReservationState.Released, holds.Single(r => r.Source == InventoryReservationSource.Conversation).State);
            Assert.Equal(InventoryReservationState.Committed, holds.Single(r => r.Source == InventoryReservationSource.Checkout).State);
            var item = await db.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.VariantId == shop.MediumId);
            Assert.Equal(0, item.OnHandQuantity);
            Assert.Equal(0, item.ReservedQuantity);
            var link = await db.AssistantCheckoutLinks.IgnoreQueryFilters().SingleAsync(l => l.ConversationId == shop.ConversationId);
            Assert.Equal(AssistantCheckoutLinkState.Used, link.State);
            Assert.Equal(order.OrderId, link.OrderId);
        }

        // The order now belongs to this chat: order status needs no verification.
        var status = await RunAsync(factory, shop, "GetOrderStatus", "{}");
        Assert.Equal(order.OrderNumber, Assert.Single(status["data"]!["orders"]!.AsArray())!["orderNumber"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/public/v1/dev/stores/{shop.Slug}/assistant-links/{token}")).StatusCode); // used
    }

    [Fact]
    public async Task CheckoutLink_FromAQuote_IsRefusedWhenThePriceOrItemsChanged()
    {
        var shop = await SeedShopAsync(fixture, "s05-stale");
        await using var factory = await FactoryAsync(shop);
        var items = $$"""[{"variantId":"{{shop.SmallId}}","quantity":1}]""";
        var quoteId = (await RunAsync(factory, shop, "QuoteCart", $$"""{"items":{{items}},"place":"Lalitpur"}"""))["data"]!["quoteId"]!.GetValue<string>();

        var matching = await RunAsync(factory, shop, "CreateCheckoutLink", $$"""{"items":{{items}},"quoteId":"{{quoteId}}"}""");
        Assert.True(matching["ok"]!.GetValue<bool>(), matching.ToJsonString());
        var otherItems = await RunAsync(factory, shop, "CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":2}],"quoteId":"{{quoteId}}"}""");
        Assert.Equal("stale", otherItems["error"]!["code"]!.GetValue<string>());

        await using (var db = Db())
        {
            await db.ProductVariants.IgnoreQueryFilters().Where(v => v.Id == shop.SmallId).ExecuteUpdateAsync(set => set.SetProperty(v => v.PriceNpr, 2600m));
        }

        var repriced = await RunAsync(factory, shop, "CreateCheckoutLink", $$"""{"items":{{items}},"quoteId":"{{quoteId}}"}""", turnId: "after-price-change");
        Assert.Equal("stale", repriced["error"]!["code"]!.GetValue<string>());
        var unknown = await RunAsync(factory, shop, "CreateCheckoutLink", $$"""{"items":{{items}},"quoteId":"01J0000000000000000000NONE"}""");
        Assert.Equal("not_found", unknown["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task CheckoutLinks_AreCapped_AndExpireOnTheStorefront()
    {
        var shop = await SeedShopAsync(fixture, "s05-link-cap");
        await using var factory = await FactoryAsync(shop, configure: o => o.Tools.MaxLiveLinksPerConversation = 1);
        using var client = factory.CreateClient();
        var url = (await RunAsync(factory, shop, "CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}"""))["data"]!["url"]!.GetValue<string>();

        var second = await RunAsync(factory, shop, "CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":2}]}""");
        Assert.Equal("limit_reached", second["error"]!["code"]!.GetValue<string>());

        await using (var db = Db())
        {
            await db.AssistantCheckoutLinks.IgnoreQueryFilters().Where(l => l.ConversationId == shop.ConversationId)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/public/v1/dev/stores/{shop.Slug}/assistant-links/{url[(url.LastIndexOf('/') + 1)..]}")).StatusCode);
    }

    // ---- escalation and takeover ----

    [Fact]
    public async Task EscalateToHuman_TakesOverWithACategory_AndThenEveryWriteToolIsPaused()
    {
        var shop = await SeedShopAsync(fixture, "s05-escalate");
        await using var factory = await FactoryAsync(shop, holds: true);

        var escalated = await RunAsync(factory, shop, "EscalateToHuman", """{"category":"refund_or_exchange"}""");
        Assert.True(escalated["ok"]!.GetValue<bool>(), escalated.ToJsonString());
        Assert.False(escalated["data"]!["alreadyWithTeam"]!.GetValue<bool>());
        var again = await RunAsync(factory, shop, "EscalateToHuman", """{"category":"complaint"}""");
        Assert.True(again["data"]!["alreadyWithTeam"]!.GetValue<bool>());

        await using (var db = Db())
        {
            var conversation = await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId);
            Assert.Equal(AutomationMode.HumanTakeover, conversation.AutomationMode);
            Assert.Equal(ConversationStatus.HumanAssigned, conversation.Status);
            Assert.Equal("refund_or_exchange", conversation.EscalationCategory);
            var audit = await db.AuditEvents.IgnoreQueryFilters().SingleAsync(a => a.TenantId == shop.TenantId && a.Action == "assistant.escalated");
            Assert.Contains("refund_or_exchange", audit.Metadata);
        }

        foreach (var (tool, arguments) in new[]
        {
            ("QuoteCart", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}],"place":"Lalitpur"}"""),
            ("ReserveInventory", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}"""),
            ("ReleaseReservation", "{}"),
            ("CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}""")
        })
        {
            Assert.Equal("automation_paused", (await RunAsync(factory, shop, tool, arguments))["error"]!["code"]!.GetValue<string>());
        }

        Assert.Equal("invalid_arguments", (await RunAsync(factory, shop, "EscalateToHuman", """{"category":"bored"}"""))["error"]!["code"]!.GetValue<string>());
    }

    // ---- no bypass, preview, cancellation, cross-tenant ----

    [Fact]
    public async Task NoToolCanMarkPaidFulfilOrChangePrices_AndSmuggledFieldsAreRejected()
    {
        var shop = await SeedShopAsync(fixture, "s05-bypass");
        await using var factory = await FactoryAsync(shop, holds: true);

        var known = await factory.AsTenantAsync(shop.TenantId, async sp => sp.GetRequiredService<IAssistantToolRegistry>()
            .GetDefinitions((await sp.GetRequiredService<IAssistantToolContextFactory>().ForConversationAsync(shop.ConversationId)).Value!).Select(d => d.Name).ToList());
        Assert.DoesNotContain(known, n => n.Contains("Pay", StringComparison.OrdinalIgnoreCase) || n.Contains("Fulfil", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Price", StringComparison.Ordinal) && n != "GetPrice" || n.Contains("Order", StringComparison.Ordinal) && n != "GetOrderStatus");
        foreach (var tool in new[] { "MarkPaid", "FulfilOrder", "UpdatePrice", "CreateOrderDraft", "ConfirmOrder", "PublishProduct" })
        {
            Assert.Equal("tool_not_allowed", (await RunAsync(factory, shop, tool, "{}"))["error"]!["code"]!.GetValue<string>());
        }

        foreach (var (tool, extra) in new[] { ("CreateCheckoutLink", "\"paid\":true"), ("CreateCheckoutLink", "\"status\":\"confirmed\""), ("QuoteCart", "\"deliveryFeeNpr\":0"),
            ("ReserveInventory", "\"expiresAt\":\"2099-01-01\""), ("EscalateToHuman", "\"tenantId\":\"x\"") })
        {
            var arguments = tool == "EscalateToHuman" ? $$"""{"category":"other",{{extra}}}""" : $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}],"place":"Lalitpur",{{extra}}}""";
            Assert.Equal("invalid_arguments", (await RunAsync(factory, shop, tool, arguments))["error"]!["code"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task SellerPreview_DryRunsWriteTools_AndWritesNothing()
    {
        var shop = await SeedShopAsync(fixture, "s05-preview");
        await using var factory = await FactoryAsync(shop, holds: true);
        var before = await CountRowsAsync();

        foreach (var (tool, arguments) in new[]
        {
            ("ReserveInventory", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}"""),
            ("CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}"""),
            ("ReleaseReservation", "{}"),
            ("EscalateToHuman", """{"category":"other"}"""),
            ("QuoteCart", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}],"place":"Lalitpur"}""")
        })
        {
            var outcome = await factory.AsTenantAsync(shop.TenantId, async sp => await sp.GetRequiredService<IAssistantToolRegistry>().ExecuteAsync(
                (await sp.GetRequiredService<IAssistantToolContextFactory>().ForSellerPreviewAsync()).Value!, new AiToolCall("p", tool, arguments)));
            Assert.Equal("ok", outcome.Trace.Outcome);
            Assert.Equal(tool != "QuoteCart", outcome.Trace.DryRun);
        }

        Assert.Equal(before, await CountRowsAsync());
        await using var db = Db();
        Assert.Equal(AutomationMode.Automated, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).AutomationMode);
    }

    [Fact]
    public async Task ACancelledCall_LeavesNoHoldLinkOrTakeover()
    {
        var shop = await SeedShopAsync(fixture, "s05-cancel");
        await using var factory = await FactoryAsync(shop, holds: true);
        var before = await CountRowsAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        foreach (var (tool, arguments) in new[]
        {
            ("CreateCheckoutLink", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}]}"""),
            ("EscalateToHuman", """{"category":"other"}""")
        })
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.AsTenantAsync(shop.TenantId, async sp =>
                await sp.GetRequiredService<IAssistantToolRegistry>().ExecuteAsync((await sp.GetRequiredService<IAssistantToolContextFactory>().ForConversationAsync(shop.ConversationId)).Value!,
                    new AiToolCall("c", tool, arguments), cancelled.Token)));
        }

        await Task.Delay(300); // let any abandoned tool task finish
        Assert.Equal(before, await CountRowsAsync());
        await using var db = Db();
        Assert.Equal(AutomationMode.Automated, (await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == shop.ConversationId)).AutomationMode);
    }

    [Fact]
    public async Task OtherTenantsVariantsConfirmationsAndLinks_AreNeverUsable()
    {
        var shop = await SeedShopAsync(fixture, "s05-iso-a");
        var other = await SeedShopAsync(fixture, "s05-iso-b");
        await using var factory = await FactoryAsync(shop, holds: true);
        await EnableHoldsAsync(factory, other);
        using var client = factory.CreateClient();

        foreach (var tool in new[] { "QuoteCart", "ReserveInventory", "CreateCheckoutLink" })
        {
            var arguments = $$"""{"items":[{"variantId":"{{other.SmallId}}","quantity":1}]{{(tool == "QuoteCart" ? ",\"place\":\"Lalitpur\"" : "")}}}""";
            Assert.Equal("not_found", (await RunAsync(factory, shop, tool, arguments))["error"]!["code"]!.GetValue<string>());
        }

        var foreignProposal = await RunAsync(factory, other, "ReserveInventory", $$"""{"items":[{"variantId":"{{other.SmallId}}","quantity":1}]}""");
        var foreignId = foreignProposal["data"]!["confirmationId"]!.GetValue<string>();
        await CustomerSaysAsync(shop, "yes");
        var stolen = await RunAsync(factory, shop, "ReserveInventory", $$"""{"items":[{"variantId":"{{shop.SmallId}}","quantity":1}],"confirmationId":"{{foreignId}}"}""");
        Assert.Equal("not_found", stolen["error"]!["code"]!.GetValue<string>());

        var otherUrl = (await RunAsync(factory, other, "CreateCheckoutLink", $$"""{"items":[{"variantId":"{{other.SmallId}}","quantity":1}]}"""))["data"]!["url"]!.GetValue<string>();
        var otherToken = otherUrl[(otherUrl.LastIndexOf('/') + 1)..];
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/public/v1/dev/stores/{shop.Slug}/assistant-links/{otherToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/public/v1/dev/stores/{other.Slug}/assistant-links/{otherToken}")).StatusCode);
    }

    // ---- helpers ----

    private static StorefrontDestinationInput Kathmandu() => new("NP", "Kathmandu", null, null);

    private static async Task<JsonNode> RunAsync(AssistantTestHost factory, Shop shop, string tool, string arguments, string? turnId = null) =>
        JsonNode.Parse((await factory.AsTenantAsync(shop.TenantId, async sp =>
        {
            var context = await sp.GetRequiredService<IAssistantToolContextFactory>().ForConversationAsync(shop.ConversationId, turnId);
            Assert.True(context.IsSuccess);
            return await sp.GetRequiredService<IAssistantToolRegistry>().ExecuteAsync(context.Value!, new AiToolCall($"call-{Guid.NewGuid():N}", tool, arguments));
        })).ResultJson)!;

    /// <summary>Proposal → customer reply → confirmation.</summary>
    private async Task HoldAsync(AssistantTestHost factory, Shop shop, string variantId, int quantity)
    {
        var items = $$"""{"items":[{"variantId":"{{variantId}}","quantity":{{quantity}}}]""";
        var proposal = await RunAsync(factory, shop, "ReserveInventory", items + "}");
        await CustomerSaysAsync(shop, "yes please");
        var held = await RunAsync(factory, shop, "ReserveInventory", items + $$""","confirmationId":"{{proposal["data"]!["confirmationId"]!.GetValue<string>()}}"}""");
        Assert.True(held["ok"]!.GetValue<bool>(), held.ToJsonString());
    }

    private static async Task<SeededOrder> CheckoutAsync(AssistantTestHost factory, Shop shop, string variantId, int quantity, string linkToken) =>
        await factory.AsTenantAsync(shop.TenantId, async sp =>
        {
            var quote = await sp.GetRequiredService<IStorefrontQuoteService>().CreateQuoteAsync(new StorefrontQuoteRequest(
                [new StorefrontQuoteLineRequest(variantId, quantity)], Kathmandu(), AssistantLinkToken: linkToken));
            Assert.True(quote.IsSuccess, quote.Error?.Detail);
            var session = await sp.GetRequiredService<IStorefrontCheckoutSessionService>().CreateAsync(new CreateCheckoutSessionRequest(quote.Value!.QuoteToken,
                new CheckoutCustomerInput("Sita Sharma", Phone, null, SaveContact: false, PrivacyAcknowledged: true),
                new CheckoutAddressInput("Lakeside Road 7", null, "Kathmandu", null, null, null), $"s05-session-{Guid.NewGuid():N}", linkToken));
            Assert.True(session.IsSuccess, session.Error?.Detail);
            var order = await sp.GetRequiredService<IOrderCreationService>().CreateFromCheckoutAsync(
                new CreateOrderFromCheckoutRequest(session.Value!.Id, OrderPaymentMethod.CashOnDelivery, $"s05-order-{Guid.NewGuid():N}"));
            Assert.True(order.IsSuccess, order.Error?.Detail);
            return new SeededOrder(order.Value!.Id, order.Value.OrderNumber, session.Value.CustomerId);
        });

    private async Task CustomerSaysAsync(Shop shop, string text)
    {
        await Task.Delay(20); // strictly after the proposal
        var accessor = new TenantContextAccessor();
        using var scope = accessor.BeginScope(new TenantContext(shop.TenantId, "u", "m", TenantRole.Owner));
        await using var db = fixture.CreateDbContext(accessor);
        var now = DateTimeOffset.UtcNow;
        db.Messages.Add(Message.CreateInboundText(shop.TenantId, shop.ConversationId, shop.ConnectionId, null, $"mid_{Guid.NewGuid():N}", text, now, now));
        await db.SaveChangesAsync();
    }

    private async Task<int> HoldCountAsync(Shop shop)
    {
        await using var db = Db();
        return await db.InventoryReservations.IgnoreQueryFilters().CountAsync(r => r.TenantId == shop.TenantId && r.Source == InventoryReservationSource.Conversation && r.State == InventoryReservationState.Active);
    }

    private async Task<long> CountRowsAsync()
    {
        await using var db = Db();
        return await db.InventoryReservations.IgnoreQueryFilters().LongCountAsync() + await db.AssistantCheckoutLinks.IgnoreQueryFilters().LongCountAsync()
            + await db.AssistantActions.IgnoreQueryFilters().LongCountAsync() + await db.AuditEvents.IgnoreQueryFilters().LongCountAsync()
            + await db.Orders.IgnoreQueryFilters().LongCountAsync();
    }

    private static async Task SaveAsTenantAsync(AppDbContext db, Shop shop)
    {
        _ = shop;
        await db.SaveChangesAsync();
    }

    private AppDbContext Db() => fixture.CreateDbContext(new TenantContextAccessor());

    private async Task<AssistantTestHost> FactoryAsync(Shop shop, bool holds = false, Action<AiOptions>? configure = null)
    {
        await using (var db = Db())
        {
            await db.Database.MigrateAsync();
        }

        var factory = new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage(), aiEnabled: false,
            configure is null ? null : services => services.PostConfigure(configure));
        if (holds) await EnableHoldsAsync(factory, shop);
        return factory;
    }

    /// <summary>Holds are opt-in (Q9): the test shop's owner switches them on.</summary>
    private static async Task EnableHoldsAsync(AssistantTestHost factory, Shop shop)
    {
        await factory.AsTenantAsync(shop.TenantId, sp => sp.GetRequiredService<IAssistantPolicyQuery>().GetEffectiveAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AssistantPolicies.IgnoreQueryFilters().Where(p => p.TenantId == shop.TenantId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.AllowedTools, new List<string>([.. AssistantPolicy.ReadTools, .. AssistantPolicy.WriteTools, AssistantPolicy.AlwaysAllowedTool])));
    }
}
