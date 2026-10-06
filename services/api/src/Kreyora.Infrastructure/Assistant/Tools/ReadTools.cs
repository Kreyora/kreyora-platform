using System.Text.Json;
using Kreyora.Application.Assistant;
using Kreyora.Application.Orders;
using Kreyora.Application.Storefront;
using Kreyora.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant.Tools;

// The five M09-S04 read tools. Each is a thin adapter: arguments (already validated) → one application query →
// minimized result. Context (tenant, store, customer) comes from AssistantToolContext, never from the arguments.

/// <summary>Shared helpers for the read tools.</summary>
internal static class ToolArgs
{
    public const string IdPattern = "^[0-9A-Za-z_-]{1,40}$";

    public static string? String(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : null;

    public static int? Int(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    public static int LowStockThreshold(IServiceProvider services) => services.GetRequiredService<IOptionsMonitor<AiOptions>>().CurrentValue.Tools.LowStockThreshold;

    public static object VariantOptions(IReadOnlyDictionary<string, string> options) => options.Count == 0 ? new Dictionary<string, string>() : options;
}

public sealed class SearchProductsTool : IAssistantTool
{
    public string Name => "SearchProducts";

    public int Version => 1;

    public string Description => "Search this shop's published products by name words (any language or spelling). Returns product ids for the other tools, the options (sizes, colors), the lowest price and whether anything is in stock.";

    public string ParametersSchema => """
        {"type":"object","properties":{
          "query":{"type":"string","minLength":1,"maxLength":64,"description":"Product words from the customer, e.g. 'red kurta'"},
          "limit":{"type":"integer","minimum":1,"maximum":5,"description":"Maximum products to return (default 5)"}
        },"required":["query"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var products = await services.GetRequiredService<ICustomerCatalogQuery>().SearchAsync(context.StoreId, ToolArgs.String(arguments, "query")!,
            ToolArgs.Int(arguments, "limit") ?? 5, ToolArgs.LowStockThreshold(services), cancellationToken);
        return AssistantToolResult.Success(new
        {
            products = products.Select(p => new { productId = p.ProductId, title = p.Title, options = p.Options, fromPriceNpr = p.FromPriceNpr, available = p.Available }),
            hint = products.Count == 0 ? "No matching product. Ask for another name or offer to connect the customer with the team. Never invent products." : null
        }, products.Count);
    }
}

public sealed class CheckInventoryTool : IAssistantTool
{
    public string Name => "CheckInventory";

    public int Version => 1;

    public string Description => "Whether a product (or one variant) is in stock: in_stock, low_stock or out_of_stock, and whether a quantity can be supplied. Exact stock counts are never available.";

    public string ParametersSchema => $$"""
        {"type":"object","properties":{
          "productId":{"type":"string","pattern":"{{ToolArgs.IdPattern}}","description":"From SearchProducts"},
          "variantId":{"type":"string","pattern":"{{ToolArgs.IdPattern}}","description":"Optional: one variant"},
          "quantity":{"type":"integer","minimum":1,"maximum":100,"description":"Optional: how many the customer wants"}
        },"required":["productId"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var product = await services.GetRequiredService<ICustomerCatalogQuery>().GetProductAsync(context.StoreId, ToolArgs.String(arguments, "productId")!,
            ToolArgs.Int(arguments, "quantity"), ToolArgs.LowStockThreshold(services), cancellationToken);
        var variants = Select(product, ToolArgs.String(arguments, "variantId"));
        if (product is null || variants.Count == 0)
        {
            return AssistantToolResult.Failed(AssistantToolErrorCodes.NotFound, "That product is not available in this shop. Search again or ask the customer.");
        }

        return AssistantToolResult.Success(new
        {
            productId = product.ProductId,
            title = product.Title,
            variants = variants.Select(v => new { variantId = v.VariantId, name = v.Name, options = ToolArgs.VariantOptions(v.Options), availability = v.Availability, canFulfil = v.CanFulfil })
        }, variants.Count);
    }

    internal static IReadOnlyList<CustomerVariant> Select(CustomerProduct? product, string? variantId) =>
        product is null ? [] : variantId is null ? product.Variants : [.. product.Variants.Where(v => v.VariantId == variantId)];
}

public sealed class GetPriceTool : IAssistantTool
{
    public string Name => "GetPrice";

    public int Version => 1;

    public string Description => "Current price in NPR for a product, per variant, with the original price when it is on sale. Quote these numbers exactly; the final total is confirmed at checkout.";

    public string ParametersSchema => $$"""
        {"type":"object","properties":{
          "productId":{"type":"string","pattern":"{{ToolArgs.IdPattern}}","description":"From SearchProducts"},
          "variantId":{"type":"string","pattern":"{{ToolArgs.IdPattern}}","description":"Optional: one variant"}
        },"required":["productId"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var product = await services.GetRequiredService<ICustomerCatalogQuery>().GetProductAsync(context.StoreId, ToolArgs.String(arguments, "productId")!,
            null, ToolArgs.LowStockThreshold(services), cancellationToken);
        var variants = CheckInventoryTool.Select(product, ToolArgs.String(arguments, "variantId"));
        if (product is null || variants.Count == 0)
        {
            return AssistantToolResult.Failed(AssistantToolErrorCodes.NotFound, "That product is not available in this shop. Search again or ask the customer.");
        }

        return AssistantToolResult.Success(new
        {
            productId = product.ProductId,
            title = product.Title,
            currency = "NPR",
            variants = variants.Select(v => new { variantId = v.VariantId, name = v.Name, options = ToolArgs.VariantOptions(v.Options), priceNpr = v.PriceNpr, compareAtPriceNpr = v.CompareAtPriceNpr }),
            note = "Prices are current. Delivery is extra; the final total is confirmed at checkout."
        }, variants.Count);
    }
}

public sealed class GetShippingInfoTool : IAssistantTool
{
    public string Name => "GetShippingInfo";

    public int Version => 1;

    public string Description => "Delivery fee, delivery time and payment options (cash on delivery, QR) for a place in Nepal (district, city or area, any spelling). Add items to get the exact fee for an order.";

    public string ParametersSchema => $$$"""
        {"type":"object","properties":{
          "place":{"type":"string","minLength":1,"maxLength":80,"description":"Where to deliver, e.g. 'Pokhara', 'Lalitpur', 'Kavre'"},
          "items":{"type":"array","maxItems":10,"description":"Optional: variants and quantities for an exact fee","items":{"type":"object","properties":{
            "variantId":{"type":"string","pattern":"{{{ToolArgs.IdPattern}}}"},
            "quantity":{"type":"integer","minimum":1,"maximum":100}
          },"required":["variantId","quantity"],"additionalProperties":false}}
        },"required":["place"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        List<DeliveryInfoItem>? items = null;
        if (arguments.TryGetProperty("items", out var list))
        {
            items = [.. list.EnumerateArray().Select(i => new DeliveryInfoItem(i.GetProperty("variantId").GetString()!, i.GetProperty("quantity").GetInt32()))];
        }

        var info = await services.GetRequiredService<IDeliveryInfoQuery>().GetAsync(context.StoreId, ToolArgs.String(arguments, "place")!, items, cancellationToken);
        return info.Status switch
        {
            DeliveryInfoStatus.Matched => AssistantToolResult.Success(new
            {
                place = info.MatchedPlace,
                feeNpr = info.FeeNpr,
                baseFeeNpr = info.FeeNpr is null ? info.BaseFeeNpr : null,
                freeAboveNpr = info.FreeAboveNpr,
                merchandiseSubtotalNpr = info.MerchandiseSubtotalNpr,
                etaText = info.EtaText,
                cashOnDelivery = info.CodAvailable,
                qrPayment = info.QrAvailable
            }, 1),
            DeliveryInfoStatus.NeedsMoreDetail => AssistantToolResult.Failed(AssistantToolErrorCodes.NeedsMoreDetail,
                "Ask the customer which of these places they mean.", new { options = info.Suggestions }),
            DeliveryInfoStatus.PlaceNotServed => AssistantToolResult.Failed(AssistantToolErrorCodes.PlaceNotServed,
                "The shop does not deliver there. Say so politely; you may mention the places served or hand over to a team member.", new { servedPlaces = info.Suggestions }),
            DeliveryInfoStatus.ItemsUnavailable => AssistantToolResult.Failed(AssistantToolErrorCodes.NotFound,
                "One of the items is unavailable or out of stock. Check the items again."),
            _ => AssistantToolResult.Failed(AssistantToolErrorCodes.PlaceUnknown,
                "The place was not recognised. Ask for the district or city.", new { servedPlaces = info.Suggestions })
        };
    }
}

public sealed class GetOrderStatusTool : IAssistantTool
{
    public string Name => "GetOrderStatus";

    public int Version => 1;

    public string Description => "Status of the customer's own order. Orders linked to this chat are shown directly; otherwise ask the customer for the order number and the last 4 digits of the phone number used for the order.";

    public string ParametersSchema => """
        {"type":"object","properties":{
          "orderNumber":{"type":"string","minLength":4,"maxLength":40,"pattern":"^[A-Za-z0-9-]+$","description":"As the customer gives it, e.g. ORD-01J..."},
          "phoneLast4":{"type":"string","pattern":"^[0-9]{4}$","description":"Last 4 digits of the order's phone number"}
        },"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<IOrderStatusLookupService>().LookupAsync(new OrderStatusLookupRequest(
            context.CustomerId, context.ConversationId, ToolArgs.String(arguments, "orderNumber"), ToolArgs.String(arguments, "phoneLast4")), cancellationToken);
        return result.Outcome switch
        {
            OrderStatusLookupOutcome.Found => AssistantToolResult.Success(new
            {
                orders = result.Orders.Select(o => new
                {
                    orderNumber = o.OrderNumber,
                    status = o.Status,
                    paymentStatus = o.PaymentStatus,
                    fulfilmentStatus = o.FulfilmentStatus,
                    placedAt = o.PlacedAt,
                    itemCount = o.ItemCount,
                    etaText = o.EtaText
                })
            }, result.Orders.Count),
            OrderStatusLookupOutcome.VerificationRequired => AssistantToolResult.Failed(AssistantToolErrorCodes.VerificationRequired,
                "Ask the customer for the order number and the last 4 digits of the phone number used for the order."),
            OrderStatusLookupOutcome.Locked => AssistantToolResult.Failed(AssistantToolErrorCodes.Locked,
                "Too many attempts for this order. Do not try again; hand over to a team member."),
            _ => AssistantToolResult.Failed(AssistantToolErrorCodes.NotVerified,
                "The order number and phone digits do not match. Reveal nothing about any order; ask the customer to check, or hand over to a team member.")
        };
    }
}
