using System.Security.Cryptography;
using System.Text.Json;
using Kreyora.Application.Models;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Storefront;

public sealed class StorefrontQuoteService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IStorefrontCatalogReadService catalog,
    IStorefrontInventoryReadService inventory,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<StorefrontQuoteOptions> options,
    ITimeProvider timeProvider) : IStorefrontQuoteService
{
    private readonly ITimeLimitedDataProtector protector = dataProtectionProvider.CreateProtector("Kreyora.Storefront.DeliveryQuote.v1").ToTimeLimitedDataProtector();

    public async Task<Result<StorefrontDeliveryQuote>> CreateQuoteAsync(StorefrontQuoteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        tenantContext.RequireCurrent();
        try
        {
            if (request.Lines is null || request.Lines.Count is 0 or > 50) return Result<StorefrontDeliveryQuote>.ValidationError("A quote requires 1-50 product lines.");
            if (request.Lines.GroupBy(line => line.VariantId, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Result<StorefrontDeliveryQuote>.ValidationError("A quote cannot contain the same variant more than once.");
            var destination = DeliveryRuleZone.NormalizeDestination(new DeliveryDestinationInput(request.Destination.CountryCode, request.Destination.District, request.Destination.Municipality, request.Destination.Locality));
            var store = await dbContext.Stores.SingleOrDefaultAsync(cancellationToken);
            if (store is null) return Result<StorefrontDeliveryQuote>.NotFound("A store has not been created for the selected workspace.");

            var lines = new List<StorefrontQuoteLine>();
            foreach (var line in request.Lines)
            {
                if (line.Quantity is < 1 or > 100) return Result<StorefrontDeliveryQuote>.ValidationError("Quote quantities must be between 1 and 100.");
                var variant = await catalog.GetPublishedVariantAsync(line.VariantId, cancellationToken);
                if (variant is null || !await IsVisibleAsync(store.Id, variant.ProductId, cancellationToken))
                {
                    return Result<StorefrontDeliveryQuote>.ValidationError("A selected product is unavailable.");
                }

                var available = await inventory.GetAvailableQuantityAsync(variant.VariantId, cancellationToken);
                if (available is null || available < line.Quantity) return Result<StorefrontDeliveryQuote>.ValidationError("A selected product does not have enough available stock.");
                var subtotal = variant.UnitPriceNpr * line.Quantity;
                lines.Add(new StorefrontQuoteLine(variant.ProductId, variant.ProductTitle, variant.VariantId, variant.VariantName, line.Quantity, variant.UnitPriceNpr, subtotal));
            }

            var selected = await FindRuleAsync(store.Id, destination, cancellationToken);
            if (selected is null) return Result<StorefrontDeliveryQuote>.ValidationError("Delivery is not available for the selected destination.");
            var merchandiseSubtotal = lines.Sum(line => line.LineSubtotalNpr);
            var deliveryFee = selected.Rule.CalculateFee(merchandiseSubtotal);
            var totals = new StorefrontQuoteTotals(merchandiseSubtotal, 0m, deliveryFee, 0m, 0m, 0m, merchandiseSubtotal + deliveryFee, "NPR");
            var delivery = new StorefrontQuoteDelivery(selected.Rule.Id, selected.Rule.Name, deliveryFee, selected.Rule.EstimatedEtaText, selected.Rule.CodAvailable);
            var expiresAt = timeProvider.UtcNow.AddMinutes(options.Value.LifetimeMinutes);
            var payload = new QuotePayload(store.Id, expiresAt, destination, lines, delivery, totals);
            var quoteToken = protector.Protect(JsonSerializer.Serialize(payload), expiresAt);
            return Result<StorefrontDeliveryQuote>.Success(new StorefrontDeliveryQuote(quoteToken, expiresAt, lines, delivery, totals));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<StorefrontDeliveryQuote>.ValidationError(exception.Message);
        }
    }

    public async Task<Result<StorefrontDeliveryQuote>> ReadQuoteAsync(string quoteToken, CancellationToken cancellationToken = default)
    {
        tenantContext.RequireCurrent();
        if (string.IsNullOrWhiteSpace(quoteToken)) return Result<StorefrontDeliveryQuote>.ValidationError("The quote is invalid or expired.");
        try
        {
            var payload = JsonSerializer.Deserialize<QuotePayload>(protector.Unprotect(quoteToken, out var expiresAt));
            if (payload is null || payload.ExpiresAt != expiresAt || !await dbContext.Stores.AnyAsync(store => store.Id == payload.StoreId, cancellationToken))
            {
                return Result<StorefrontDeliveryQuote>.ValidationError("The quote is invalid or expired.");
            }

            return Result<StorefrontDeliveryQuote>.Success(new StorefrontDeliveryQuote(quoteToken, expiresAt, payload.Lines, payload.Delivery, payload.Totals));
        }
        catch (CryptographicException)
        {
            return Result<StorefrontDeliveryQuote>.ValidationError("The quote is invalid or expired.");
        }
        catch (JsonException)
        {
            return Result<StorefrontDeliveryQuote>.ValidationError("The quote is invalid or expired.");
        }
    }

    private Task<bool> IsVisibleAsync(string storeId, string productId, CancellationToken cancellationToken) =>
        dbContext.StoreProductPublications.AnyAsync(publication => publication.StoreId == storeId && publication.ProductId == productId && publication.Visibility == StoreProductVisibility.Visible, cancellationToken);

    private async Task<RuleMatch?> FindRuleAsync(string storeId, DeliveryDestination destination, CancellationToken cancellationToken)
    {
        var rules = await dbContext.DeliveryRules.Where(rule => rule.StoreId == storeId && rule.IsActive).Include(rule => rule.Zones).ToListAsync(cancellationToken);
        return rules.SelectMany(rule => rule.Zones.Where(zone => zone.Matches(destination)).Select(zone => new RuleMatch(rule, zone)))
            .OrderByDescending(match => match.Zone.Specificity)
            .ThenBy(match => match.Rule.Priority)
            .ThenBy(match => match.Rule.CreatedAt)
            .ThenBy(match => match.Rule.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private sealed record RuleMatch(DeliveryRule Rule, DeliveryRuleZone Zone);
    private sealed record QuotePayload(
        string StoreId,
        DateTimeOffset ExpiresAt,
        DeliveryDestination Destination,
        IReadOnlyList<StorefrontQuoteLine> Lines,
        StorefrontQuoteDelivery Delivery,
        StorefrontQuoteTotals Totals);
}
