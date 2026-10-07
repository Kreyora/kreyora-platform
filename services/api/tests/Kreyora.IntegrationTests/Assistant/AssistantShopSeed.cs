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

namespace Kreyora.IntegrationTests.Assistant;

/// <summary>Shared synthetic shop for assistant tool tests (M09-S04/S05): catalog, stock, zones, an active store, a conversation.</summary>
internal static class AssistantShopSeed
{
    public const string Phone = "9841234567";

    public static async Task<SeededOrder> CreateOrderAsync(AssistantTestHost factory, Shop shop) =>
        await factory.AsTenantAsync(shop.TenantId, async sp =>
        {
            var quote = await sp.GetRequiredService<IStorefrontQuoteService>().CreateQuoteAsync(new StorefrontQuoteRequest(
                [new StorefrontQuoteLineRequest(shop.SmallId, 1)], new StorefrontDestinationInput("NP", "Kathmandu", null, null)));
            Assert.True(quote.IsSuccess, quote.Error?.Detail);
            var session = await sp.GetRequiredService<IStorefrontCheckoutSessionService>().CreateAsync(new CreateCheckoutSessionRequest(quote.Value!.QuoteToken,
                new CheckoutCustomerInput("Sita Sharma", Phone, null, SaveContact: true, PrivacyAcknowledged: true),
                new CheckoutAddressInput("Lakeside Road 7", null, "Kathmandu", null, null, null), $"s04-session-{Guid.NewGuid():N}"));
            Assert.True(session.IsSuccess, session.Error?.Detail);
            var order = await sp.GetRequiredService<IOrderCreationService>().CreateFromCheckoutAsync(
                new CreateOrderFromCheckoutRequest(session.Value!.Id, OrderPaymentMethod.CashOnDelivery, $"s04-order-{Guid.NewGuid():N}"));
            Assert.True(order.IsSuccess, order.Error?.Detail);
            return new SeededOrder(order.Value!.Id, order.Value.OrderNumber, session.Value.CustomerId);
        });

    /// <summary>A shop with a kurta (S in stock, M low, L out; on sale), a saree, a hidden and a draft product, three delivery rules and a conversation.</summary>
    public static async Task<Shop> SeedShopAsync(PostgresFixture fixture, string prefix)
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"shop-{suffix}";
        var store = Store.Create(tenant.Id, new StoreSettings("Demo Boutique", slug, null, StoreThemePreset.Default, null, "Owner", "owner@example.com", "9800000000",
            null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var kurta = Product.Create(tenant.Id, "Red Cotton Kurta", "Soft cotton kurta.", $"red-kurta-{suffix}");
        var small = kurta.AddVariant($"KURTA-S-{suffix}", "Small", new Dictionary<string, string> { ["size"] = "S" }, 2500m, 3000m, true);
        var medium = kurta.AddVariant($"KURTA-M-{suffix}", "Medium", new Dictionary<string, string> { ["size"] = "M" }, 2500m, null, true);
        var large = kurta.AddVariant($"KURTA-L-{suffix}", "Large", new Dictionary<string, string> { ["size"] = "L" }, 2600m, null, true);
        kurta.Publish();
        var saree = Product.Create(tenant.Id, "Silk Saree", null, $"silk-saree-{suffix}");
        var sareeVariant = saree.AddVariant($"SAREE-{suffix}", "Standard", null, 8000m, null, true);
        saree.Publish();
        var hidden = Product.Create(tenant.Id, "Secret Prototype", null, $"secret-{suffix}");
        var hiddenVariant = hidden.AddVariant($"SECRET-{suffix}", "Standard", null, 999m, null, true);
        hidden.Publish();
        var draft = Product.Create(tenant.Id, "Draft Item Kurta", null, $"draft-{suffix}");
        draft.AddVariant($"DRAFT-{suffix}", "Standard", null, 500m, null, true);

        store.Activate(DateTimeOffset.UtcNow); // reachable through the public storefront (checkout links, M09-S05)
        db.AddRange(store, kurta, saree, hidden, draft,
            Stock(tenant.Id, small.Id, 10), Stock(tenant.Id, medium.Id, 2), Stock(tenant.Id, large.Id, 0), Stock(tenant.Id, sareeVariant.Id, 5), Stock(tenant.Id, hiddenVariant.Id, 5),
            StoreProductPublication.Create(tenant.Id, store.Id, kurta.Id, StoreProductVisibility.Visible),
            StoreProductPublication.Create(tenant.Id, store.Id, saree.Id, StoreProductVisibility.Visible),
            StoreProductPublication.Create(tenant.Id, store.Id, hidden.Id, StoreProductVisibility.Hidden),
            DeliveryRule.Create(tenant.Id, store.Id, new DeliveryRuleSettings("Valley", 0, DeliveryFeeType.Flat, 100m, null, "1-2 days", true, true,
                [new DeliveryZoneInput("Kathmandu", null, null), new DeliveryZoneInput("Lalitpur", null, null), new DeliveryZoneInput("Bhaktapur", null, null)])),
            DeliveryRule.Create(tenant.Id, store.Id, new DeliveryRuleSettings("Pokhara", 1, DeliveryFeeType.Threshold, 200m, 5000m, "3 days", false, true,
                [new DeliveryZoneInput("Kaski", "Pokhara", null)])),
            DeliveryRule.Create(tenant.Id, store.Id, new DeliveryRuleSettings("Old Jhapa", 2, DeliveryFeeType.Flat, 300m, null, null, true, false,
                [new DeliveryZoneInput("Jhapa", null, null)])));

        var connection = ChannelConnection.Create(tenant.Id, ChannelType.Instagram, "igid_" + suffix, "S04 IG", storeId: store.Id);
        var identity = Kreyora.Domain.Customers.CustomerChannelIdentity.Create(tenant.Id, connection.Id, ChannelType.Instagram, "igsid_" + suffix, DateTimeOffset.UtcNow);
        var conversation = Conversation.Start(tenant.Id, connection.Id, store.Id, identity.Id, ChannelType.Instagram);
        db.AddRange(connection, identity, conversation);
        await db.SaveChangesAsync();
        return new Shop(tenant.Id, slug, conversation.Id, identity.Id, kurta.Id, kurta.Slug, small.Id, large.Id, hidden.Id, hidden.Slug, medium.Id, connection.Id, store.Id, hiddenVariant.Id);
    }

    private static InventoryItem Stock(string tenantId, string variantId, int onHand)
    {
        var item = InventoryItem.Create(tenantId, variantId);
        if (onHand > 0) item.ApplyMovement(onHand);
        return item;
    }

    public sealed record Shop(string TenantId, string Slug, string ConversationId, string IdentityId, string KurtaId, string KurtaSlug, string SmallId, string LargeId, string HiddenId, string HiddenSlug, string MediumId, string ConnectionId, string StoreId, string HiddenVariantId);

    public sealed record SeededOrder(string OrderId, string OrderNumber, string? CustomerId);
}
