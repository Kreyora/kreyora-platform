using Kreyora.Application.Storefront;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Storefront;

/// <summary>
/// Delivery fee, ETA and payment options for a place a customer names (M09-S04 Q5). The place is matched against the
/// shop's zone names at every level first, then the built-in <see cref="NepalGazetteer"/>; spellings of districts are
/// compared canonically so "Kavre" and "Kavrepalanchok" are the same zone. Rule choice follows the quote service:
/// most specific zone, then priority, then age. Item prices come from the published catalog, never the caller.
/// </summary>
public sealed class DeliveryInfoQuery(
    AppDbContext dbContext,
    IStorefrontCatalogReadService catalog,
    IStorefrontInventoryReadService inventory) : IDeliveryInfoQuery
{
    private const int MaxSuggestions = 15;

    public async Task<DeliveryInfoResult> GetAsync(string storeId, string place, IReadOnlyList<DeliveryInfoItem>? items, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(storeId, place, cancellationToken);
        if (resolved.Match is not { } match) return Outcome(resolved.Status, resolved.Suggestions);

        var rule = match.Zone.Rule;
        var payments = await dbContext.StorePaymentConfigurations.AsNoTracking().Where(c => c.StoreId == storeId)
            .Select(c => new { c.CodEnabled, c.MerchantQrEnabled }).SingleOrDefaultAsync(cancellationToken);
        var codEnabled = payments?.CodEnabled ?? true; // same default as the payment configuration service
        var qrEnabled = payments?.MerchantQrEnabled ?? true;

        decimal? subtotal = null;
        if (items is { Count: > 0 })
        {
            subtotal = await SubtotalAsync(storeId, items, cancellationToken);
            if (subtotal is null) return Outcome(DeliveryInfoStatus.ItemsUnavailable, []);
        }

        decimal? fee = subtotal is { } amount ? rule.CalculateFee(amount) : rule.FeeType == DeliveryFeeType.Flat ? rule.BaseFeeNpr : null;
        return new DeliveryInfoResult(DeliveryInfoStatus.Matched, match.Destination.Label, fee, rule.BaseFeeNpr,
            rule.FeeType == DeliveryFeeType.Threshold ? rule.FreeAboveNpr : null, subtotal, rule.EstimatedEtaText,
            rule.CodAvailable && codEnabled, qrEnabled, []);
    }

    public async Task<DeliveryDestinationResolution> ResolveDestinationAsync(string storeId, string place, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(storeId, place, cancellationToken);
        if (resolved.Match is not { } match) return new DeliveryDestinationResolution(resolved.Status, null, null, resolved.Suggestions);
        var zone = match.Zone.Zone;
        return new DeliveryDestinationResolution(DeliveryInfoStatus.Matched, new StorefrontDestinationInput("NP", zone.District, zone.Municipality, zone.Locality), match.Destination.Label, []);
    }

    private async Task<Resolution> ResolveAsync(string storeId, string place, CancellationToken cancellationToken)
    {
        var rules = await dbContext.DeliveryRules.AsNoTracking().Where(r => r.StoreId == storeId && r.IsActive).Include(r => r.Zones).ToListAsync(cancellationToken);
        var zones = rules.SelectMany(rule => rule.Zones.Select(zone => new ZoneEntry(rule, zone))).ToList();
        var served = zones.Select(z => z.Label).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxSuggestions).ToArray();

        var key = NepalGazetteer.Key(place);
        if (key.Length == 0) return new Resolution(DeliveryInfoStatus.PlaceUnknown, served, null);

        // 1. The shop's own zone names (any level); 2. the gazetteer.
        var destinations = zones.SelectMany(z => z.DirectMatches(key)).Distinct().ToList();
        if (destinations.Count == 0)
        {
            var places = NepalGazetteer.Lookup(place);
            if (places.Select(p => p.District).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                return new Resolution(DeliveryInfoStatus.NeedsMoreDetail, [.. places.Select(p => p.District).Distinct(StringComparer.Ordinal)], null);
            }

            destinations = [.. places.Select(p => new Destination(Canonical(p.District), NepalGazetteer.Key(p.Municipality), string.Empty, p.Municipality ?? p.District))];
        }

        if (destinations.Count == 0) return new Resolution(DeliveryInfoStatus.PlaceUnknown, served, null);

        var match = destinations
            .SelectMany(destination => zones.Where(z => z.Matches(destination)).Select(z => new Match(destination, z)))
            .OrderByDescending(m => m.Zone.Zone.Specificity)
            .ThenBy(m => m.Zone.Rule.Priority)
            .ThenBy(m => m.Zone.Rule.CreatedAt)
            .ThenBy(m => m.Zone.Rule.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (match is null)
        {
            // Served only at municipality/locality level inside that district: ask which one.
            var narrower = destinations.Where(d => d.Municipality.Length == 0)
                .SelectMany(d => zones.Where(z => z.District == d.District && z.Municipality.Length > 0).Select(z => z.Zone.Municipality!))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxSuggestions).ToArray();
            return narrower.Length > 0
                ? new Resolution(DeliveryInfoStatus.NeedsMoreDetail, narrower, null)
                : new Resolution(DeliveryInfoStatus.PlaceNotServed, served, null);
        }

        return new Resolution(DeliveryInfoStatus.Matched, [], match);
    }

    /// <summary>Server-priced subtotal of published, visible, in-stock variants; null if any line can't be supplied.</summary>
    private async Task<decimal?> SubtotalAsync(string storeId, IReadOnlyList<DeliveryInfoItem> items, CancellationToken cancellationToken)
    {
        decimal subtotal = 0;
        foreach (var item in items)
        {
            var variant = await catalog.GetPublishedVariantAsync(item.VariantId, cancellationToken);
            if (variant is null || item.Quantity < 1) return null;
            var visible = await dbContext.StoreProductPublications.AnyAsync(p => p.StoreId == storeId && p.ProductId == variant.ProductId && p.Visibility == StoreProductVisibility.Visible, cancellationToken);
            var available = await inventory.GetAvailableQuantityAsync(item.VariantId, cancellationToken);
            if (!visible || available is null || available < item.Quantity) return null;
            subtotal += variant.UnitPriceNpr * item.Quantity;
        }

        return subtotal;
    }

    private static DeliveryInfoResult Outcome(DeliveryInfoStatus status, IReadOnlyList<string> suggestions) =>
        new(status, null, null, null, null, null, null, false, false, suggestions);

    private static string Canonical(string district) => NepalGazetteer.CanonicalDistrict(district) ?? NepalGazetteer.Key(district);

    private sealed record Destination(string District, string Municipality, string Locality, string Label);

    private sealed record Match(Destination Destination, ZoneEntry Zone);

    private sealed record Resolution(DeliveryInfoStatus Status, IReadOnlyList<string> Suggestions, Match? Match);

    private sealed class ZoneEntry(DeliveryRule rule, DeliveryRuleZone zone)
    {
        public DeliveryRule Rule { get; } = rule;
        public DeliveryRuleZone Zone { get; } = zone;
        public string District { get; } = Canonical(zone.District);
        public string Municipality { get; } = NepalGazetteer.Key(zone.Municipality);
        public string Locality { get; } = NepalGazetteer.Key(zone.Locality);
        public string Label => Zone.Locality ?? Zone.Municipality ?? Zone.District;

        /// <summary>Destinations implied when the customer's words equal one of this zone's names.</summary>
        public IEnumerable<Destination> DirectMatches(string key)
        {
            if (Locality.Length > 0 && Locality == key) yield return new Destination(District, Municipality, Locality, Zone.Locality!);
            if (Municipality.Length > 0 && Municipality == key) yield return new Destination(District, Municipality, string.Empty, Zone.Municipality!);
            if (NepalGazetteer.Key(Zone.District) == key || District == NepalGazetteer.CanonicalDistrict(key)) yield return new Destination(District, string.Empty, string.Empty, Zone.District);
        }

        public bool Matches(Destination destination) =>
            District == destination.District &&
            (Municipality.Length == 0 || Municipality == destination.Municipality) &&
            (Locality.Length == 0 || Locality == destination.Locality);
    }
}
