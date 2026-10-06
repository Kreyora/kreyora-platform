using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;

namespace Kreyora.AiEvaluation;

/// <summary>
/// The planned M09-S04 read tools plus EscalateToHuman, answered deterministically from the synthetic catalog.
/// Order lookups are scoped to the conversation's customer, exactly as the real tools must be.
/// </summary>
public sealed class FakeTools(FakeCatalog catalog)
{
    public static readonly IReadOnlyList<AiToolDefinition> Definitions =
    [
        new("SearchProducts", "Search the shop's catalog by words in the product name or category. Returns matching products with their ids.",
            """{"type":"object","properties":{"query":{"type":"string","description":"Search words, any language"}},"required":["query"]}"""),
        new("CheckInventory", "Current stock for a product, per size/color variant.",
            """{"type":"object","properties":{"productId":{"type":"string"},"variant":{"type":"string","description":"Optional size or color"}},"required":["productId"]}"""),
        new("GetPrice", "Current price in NPR for a product, per variant.",
            """{"type":"object","properties":{"productId":{"type":"string"},"variant":{"type":"string","description":"Optional size or color"}},"required":["productId"]}"""),
        new("GetShippingInfo", "Delivery fee, delivery time and whether cash on delivery (COD) or QR payment is available for a city.",
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}"""),
        new("GetOrderStatus", "Status of the current customer's own orders. Only this customer's orders are visible.",
            """{"type":"object","properties":{"orderNumber":{"type":"string","description":"Optional, e.g. KR-1042"}}}"""),
        new("EscalateToHuman", "Hand the conversation to a human team member (complaints, refunds, exchanges, custom requests, safety questions, or when the customer asks for a person).",
            """{"type":"object","properties":{"reason":{"type":"string"}},"required":["reason"]}"""),
    ];

    public static readonly IReadOnlyDictionary<string, string[]> RequiredArguments = Definitions.ToDictionary(
        d => d.Name,
        d => JsonNode.Parse(d.ParametersJsonSchema)?["required"] is JsonArray required
            ? required.Select(r => r!.GetValue<string>()).ToArray()
            : []);

    public string Execute(string name, string argumentsJson)
    {
        JsonObject args;
        try
        {
            args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return Error("Arguments were not valid JSON.");
        }

        return name switch
        {
            "SearchProducts" => Search(Text(args, "query")),
            "CheckInventory" => Inventory(Text(args, "productId"), Text(args, "variant")),
            "GetPrice" => Price(Text(args, "productId"), Text(args, "variant")),
            "GetShippingInfo" => Shipping(Text(args, "city")),
            "GetOrderStatus" => Orders(Text(args, "orderNumber")),
            "EscalateToHuman" => JsonSerializer.Serialize(new { escalated = true, message = "A team member has been notified and will reply." }),
            _ => Error($"Unknown tool {name}.")
        };
    }

    private string Search(string? query)
    {
        var words = (query ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = catalog.Products
            .Where(p => words.Length == 0 || words.Any(w =>
                p.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                || p.NameNe.Contains(w, StringComparison.Ordinal)
                || p.Category.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new { productId = p.Id, name = p.Name, nameNe = p.NameNe, category = p.Category })
            .ToList();
        return JsonSerializer.Serialize(new { results = matches.Count > 0 ? matches : catalog.Products.Select(p => new { productId = p.Id, name = p.Name, nameNe = p.NameNe, category = p.Category }).ToList(), exactMatch = matches.Count > 0 });
    }

    private string Inventory(string? productId, string? variant) =>
        WithProduct(productId, product => JsonSerializer.Serialize(new
        {
            productId = product.Id,
            name = product.Name,
            variants = Variants(product, variant).Select(v => new { sku = v.Sku, size = v.Size, color = v.Color, inStock = v.Stock > 0, quantity = v.Stock }),
            restockDate = (string?)null
        }));

    private string Price(string? productId, string? variant) =>
        WithProduct(productId, product => JsonSerializer.Serialize(new
        {
            productId = product.Id,
            name = product.Name,
            currency = "NPR",
            variants = Variants(product, variant).Select(v => new { sku = v.Sku, size = v.Size, color = v.Color, priceNpr = v.PriceNpr })
        }));

    private string Shipping(string? city)
    {
        var zone = catalog.Shipping.FirstOrDefault(z => z.Cities.Any(c => string.Equals(c, city?.Trim(), StringComparison.OrdinalIgnoreCase)));
        return zone is null
            ? JsonSerializer.Serialize(new { city, served = false, message = "We do not deliver to this city yet.", paymentNotes = catalog.PaymentNotes })
            : JsonSerializer.Serialize(new { city, served = true, zone = zone.Zone, feeNpr = zone.FeeNpr, deliveryDays = zone.Days, cashOnDelivery = zone.Cod, qrPayment = zone.Qr, paymentNotes = catalog.PaymentNotes });
    }

    private string Orders(string? orderNumber)
    {
        var mine = catalog.ConversationCustomer.Orders;
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return JsonSerializer.Serialize(new { orders = mine });
        }

        var match = mine.FirstOrDefault(o => string.Equals(o.GetProperty("orderNumber").GetString(), orderNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        return match.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.Serialize(new { found = false, message = "No order with that number belongs to this customer." })
            : JsonSerializer.Serialize(new { found = true, order = match });
    }

    private string WithProduct(string? productId, Func<FakeProduct, string> render)
    {
        var product = catalog.Products.FirstOrDefault(p => string.Equals(p.Id, productId?.Trim(), StringComparison.OrdinalIgnoreCase));
        return product is null ? Error("Unknown productId. Use SearchProducts to find the right id.") : render(product);
    }

    private static IEnumerable<FakeVariant> Variants(FakeProduct product, string? variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
        {
            return product.Variants;
        }

        var matching = product.Variants.Where(v =>
            string.Equals(v.Size, variant.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(v.Color, variant.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(v.Sku, variant.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return matching.Count > 0 ? matching : product.Variants;
    }

    private static string? Text(JsonObject args, string name) =>
        args[name] switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonValue value => Convert.ToString(value.ToJsonString(), CultureInfo.InvariantCulture),
            _ => null
        };

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });
}
