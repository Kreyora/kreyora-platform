using System.Globalization;
using System.Text.Json.Nodes;

namespace Kreyora.AiEvaluation;

/// <summary>
/// Seeds the synthetic evaluation shop (<c>fake-catalog.v1.json</c>) through the real seller API: store, payments,
/// products with sizes/colours and stock, store visibility, delivery zones, a payment FAQ, a reviewed policy. Re-runnable.
/// </summary>
public static class PipelineSeeder
{
    /// <summary>City → (district, municipality) for the catalog's delivery zones (Nepal gazetteer names).</summary>
    public static readonly IReadOnlyDictionary<string, (string District, string? Municipality)> Places = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Kathmandu"] = ("Kathmandu", null),
        ["Lalitpur"] = ("Lalitpur", null),
        ["Patan"] = ("Lalitpur", "Patan"),
        ["Bhaktapur"] = ("Bhaktapur", null),
        ["Pokhara"] = ("Kaski", "Pokhara"),
        ["Chitwan"] = ("Chitwan", null),
        ["Bharatpur"] = ("Chitwan", "Bharatpur"),
        ["Butwal"] = ("Rupandehi", "Butwal"),
        ["Biratnagar"] = ("Morang", "Biratnagar"),
        ["Dharan"] = ("Sunsari", "Dharan"),
        ["Nepalgunj"] = ("Banke", "Nepalgunj"),
    };

    public static string FaqText(FakeCatalog catalog) => $"Payment: {catalog.PaymentNotes}";

    public static async Task SeedAsync(ApiSession api, FakeCatalog catalog, Action<string> log, CancellationToken cancellationToken)
    {
        var store = await api.SendAsync(HttpMethod.Get, "v1/store", null, cancellationToken, allowNotFound: true);
        if (store?["id"] is null)
        {
            store = await api.SendAsync(HttpMethod.Post, "v1/store", new
            {
                displayName = "Demo Boutique", platformSlug = "demo-boutique-eval", tagline = "Synthetic evaluation shop", themePreset = "default", brandAccentHex = (string?)null,
                contactName = "Demo Owner", contactEmail = "owner@kreyora.test", contactPhone = "9800000000", contactWhatsApp = (string?)null,
                facebookUrl = (string?)null, instagramUrl = (string?)null, tikTokUrl = (string?)null,
                termsPolicy = "Orders are confirmed by the shop.", privacyPolicy = "Details are used only to deliver your order.",
                returnsPolicy = "Contact the shop about returns and exchanges.", paymentPolicy = catalog.PaymentNotes,
            }, cancellationToken);
        }

        var storeId = store!["id"]!.GetValue<string>();
        await api.SendAsync(HttpMethod.Put, $"v1/store/{storeId}/payment-configuration",
            new { codEnabled = true, merchantQrEnabled = true, merchantQrInstructions = "Scan the shop QR at checkout.", merchantQrMediaAssetId = (string?)null }, cancellationToken);
        log("store and payments");

        var existing = (await api.GetAsync("v1/catalog/products?pageSize=100", cancellationToken))?["items"]?.AsArray() ?? [];
        foreach (var product in catalog.Products)
        {
            var slug = product.Id.ToLowerInvariant();
            var found = existing.FirstOrDefault(p => p?["slug"]?.GetValue<string>() == slug);
            var id = found?["id"]?.GetValue<string>() ?? (await api.SendAsync(HttpMethod.Post, "v1/catalog/products", new
            {
                title = product.Name,
                description = $"{product.Name} ({product.NameNe}), {product.Category}.",
                slug,
                idempotencyKey = $"m09s08-{slug}",
                variants = product.Variants.Select(v => new
                {
                    sku = v.Sku,
                    name = string.Join(" / ", new[] { v.Size, v.Color }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } n ? n : "Standard",
                    options = Options(v),
                    priceNpr = v.PriceNpr,
                    compareAtPriceNpr = (decimal?)null,
                    isPublished = true
                })
            }, cancellationToken))!["id"]!.GetValue<string>();

            var detail = await api.GetAsync($"v1/catalog/products/{id}", cancellationToken);
            if (!string.Equals(detail?["publishState"]?.GetValue<string>(), "published", StringComparison.OrdinalIgnoreCase))
            {
                detail = await api.SendAsync(HttpMethod.Post, $"v1/catalog/products/{id}/publication", new { state = "published", expectedVersion = detail!["version"]!.GetValue<long>() }, cancellationToken);
            }

            foreach (var variant in product.Variants.Where(v => v.Stock > 0))
            {
                var variantId = detail!["variants"]!.AsArray().First(v => v?["sku"]?.GetValue<string>() == variant.Sku)!["id"]!.GetValue<string>();
                await api.SendAsync(HttpMethod.Post, "v1/inventory/adjustments",
                    new { variantId, type = "openingBalance", quantity = variant.Stock, reason = "Synthetic evaluation stock", idempotencyKey = $"m09s08-stock-{variant.Sku}" }, cancellationToken);
            }
        }

        log($"{catalog.Products.Count} products with stock");

        var publications = await api.GetAsync("v1/store/publications", cancellationToken);
        var pubs = publications is JsonArray a ? a : publications?["items"]?.AsArray() ?? [];
        foreach (var product in (await api.GetAsync("v1/catalog/products?pageSize=100", cancellationToken))?["items"]?.AsArray() ?? [])
        {
            var productId = product!["id"]!.GetValue<string>();
            var pub = pubs.FirstOrDefault(p => p?["productId"]?.GetValue<string>() == productId);
            if (!string.Equals(pub?["visibility"]?.GetValue<string>(), "visible", StringComparison.OrdinalIgnoreCase))
            {
                await api.SendAsync(HttpMethod.Put, $"v1/store/publications/{productId}", new { visibility = "visible", expectedVersion = pub?["version"]?.GetValue<long>() ?? 0 }, cancellationToken);
            }
        }

        var rules = await api.GetAsync("v1/store/delivery-rules", cancellationToken);
        if (((rules as JsonArray) ?? rules?["items"]?.AsArray() ?? []).Count == 0)
        {
            var priority = 0;
            foreach (var zone in catalog.Shipping)
            {
                await api.SendAsync(HttpMethod.Post, "v1/store/delivery-rules", new
                {
                    name = zone.Zone,
                    priority = priority++,
                    feeType = "flat",
                    baseFeeNpr = zone.FeeNpr,
                    freeAboveNpr = (decimal?)null,
                    estimatedEtaText = $"{zone.Days} days",
                    codAvailable = zone.Cod,
                    isActive = true,
                    zones = zone.Cities.Select(c => Places[c]).Distinct().Select(p => new { district = p.District, municipality = p.Municipality, locality = (string?)null })
                }, cancellationToken);
            }
        }

        log("delivery zones");
        var current = await api.GetAsync("v1/store", cancellationToken);
        if (!string.Equals(current?["status"]?.GetValue<string>(), "active", StringComparison.OrdinalIgnoreCase))
        {
            await api.SendAsync(HttpMethod.Post, "v1/store/activate", new { expectedVersion = current!["version"]!.GetValue<long>() }, cancellationToken);
        }

        var documents = await api.GetAsync("v1/assistant/knowledge", cancellationToken);
        if (((documents as JsonArray) ?? documents?["items"]?.AsArray() ?? []).Count == 0)
        {
            var created = await api.SendAsync(HttpMethod.Post, "v1/assistant/knowledge", new { title = "Payment options", category = "payment", text = FaqText(catalog) }, cancellationToken);
            await api.SendAsync(HttpMethod.Post, $"v1/assistant/knowledge/{created!["id"]}/versions/{created["pendingVersions"]![0]!["id"]}/approve", null, cancellationToken);
        }

        var policy = await api.GetAsync("v1/assistant/policy", cancellationToken);
        await api.SendAsync(HttpMethod.Put, "v1/assistant/policy", policy, cancellationToken);
        var readiness = await api.GetAsync("v1/assistant/readiness", cancellationToken);
        var missing = readiness?["checks"]?.AsArray().Where(c => c?["required"]?.GetValue<bool>() == true && c["passed"]?.GetValue<bool>() != true)
            .Select(c => c!["code"]!.GetValue<string>()).ToList() ?? [];
        // The channel is not needed for the playground; everything else must be ready.
        missing.Remove("channel_connected");
        missing.Remove("ai_entitled");
        log(missing.Count == 0 ? "assistant ready (playground)" : $"assistant NOT ready: {string.Join(", ", missing)}");
        if (missing.Count > 0) throw new InvalidOperationException($"Seeded shop not ready: {string.Join(", ", missing)}");
    }

    private static Dictionary<string, string>? Options(FakeVariant variant)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(variant.Size)) options["size"] = variant.Size;
        if (!string.IsNullOrWhiteSpace(variant.Color)) options["color"] = variant.Color;
        return options.Count == 0 ? null : options;
    }

    internal static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
