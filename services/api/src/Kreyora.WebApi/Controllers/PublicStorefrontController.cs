using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Kreyora.Application.Orders;
using Kreyora.Application.Storefront;
using Kreyora.Domain.Orders;
using Kreyora.WebApi.Configuration;
using Kreyora.WebApi.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[AllowAnonymous]
[RequirePublicStorefrontContext]
[ApiVersion("1.0")]
[Route("public/v{version:apiVersion}/store")]
[Route("public/v{version:apiVersion}/dev/stores/{slug}")]
public sealed class PublicStorefrontController(
    IPublicStorefrontService storefront,
    IStorefrontQuoteService quotes,
    IStorefrontCheckoutSessionService sessions,
    IOrderCreationService orders,
    IOptions<PublicStorefrontOptions> options) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("public-reads")]
    public async Task<ActionResult<PublicStorefront>> GetStore(CancellationToken cancellationToken)
    {
        var result = await storefront.GetStoreAsync(cancellationToken);
        return ToPublicReadResult(result);
    }

    [HttpGet("products")]
    [EnableRateLimiting("public-reads")]
    public async Task<ActionResult<PublicCatalogPage>> ListProducts([FromQuery] string? cursor, [FromQuery] string? q, [FromQuery] int pageSize = 24, CancellationToken cancellationToken = default)
    {
        var result = await storefront.ListProductsAsync(new PublicCatalogQuery(q, cursor, pageSize), cancellationToken);
        return ToPublicReadResult(result);
    }

    [HttpGet("products/{productSlug}")]
    [EnableRateLimiting("public-reads")]
    public async Task<ActionResult<PublicCatalogProduct>> GetProduct(string productSlug, CancellationToken cancellationToken)
    {
        var result = await storefront.GetProductAsync(productSlug, cancellationToken);
        return ToPublicReadResult(result);
    }

    [HttpGet("media/{mediaAssetId}")]
    [EnableRateLimiting("public-reads")]
    public async Task<IActionResult> GetMedia(string mediaAssetId, CancellationToken cancellationToken)
    {
        var result = await storefront.OpenMediaAsync(mediaAssetId, cancellationToken);
        if (result.IsFailure) return PublicError(result.Error!.Status, result.Error!.Detail);
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers.Vary = "Host";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType, enableRangeProcessing: false);
    }

    [HttpPost("checkout/quotes")]
    [EnableRateLimiting("public-quotes")]
    public async Task<ActionResult<PublicDeliveryQuote>> Quote(PublicQuoteRequest request, CancellationToken cancellationToken)
    {
        var result = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest(request.Lines.Select(item => new StorefrontQuoteLineRequest(item.VariantId, item.Quantity)).ToArray(),
            new StorefrontDestinationInput(request.Destination.CountryCode, request.Destination.District, request.Destination.Municipality, request.Destination.Locality)), cancellationToken);
        return result.Match<ActionResult<PublicDeliveryQuote>>(
            value => Ok(MapQuote(value)),
            error => PublicError(error.Status, error.Detail));
    }

    [HttpPost("checkout/sessions")]
    [EnableRateLimiting("public-sessions")]
    public async Task<ActionResult<PublicCheckoutSession>> CreateSession(PublicCheckoutSessionRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!TryIdempotencyKey(idempotencyKey, out var key)) return PublicError(StatusCodes.Status400BadRequest, "A valid Idempotency-Key header is required.");
        var result = await sessions.CreateAsync(new CreateCheckoutSessionRequest(request.QuoteToken,
            new CheckoutCustomerInput(request.Customer.DisplayName, request.Customer.Phone, request.Customer.Email, request.Customer.SaveContact, request.Customer.PrivacyAcknowledged),
            new CheckoutAddressInput(request.Address.AddressLine1, request.Address.AddressLine2, request.Address.District, request.Address.Municipality, request.Address.Locality, request.Address.Landmark), key), cancellationToken);
        return result.Match<ActionResult<PublicCheckoutSession>>(
            value => StatusCode(value.WasReplayed ? StatusCodes.Status200OK : StatusCodes.Status201Created,
                new PublicCheckoutSession(value.Id, value.ExpiresAt, value.Items.Select(MapSessionLine).ToArray(), MapDelivery(value.Delivery), value.Totals, value.WasReplayed)),
            error => PublicError(error.Status, error.Detail));
    }

    [HttpPost("checkout/orders")]
    [EnableRateLimiting("public-orders")]
    public async Task<ActionResult<PublicOrderConfirmation>> CreateOrder(PublicCreateOrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!TryIdempotencyKey(idempotencyKey, out var key)) return PublicError(StatusCodes.Status400BadRequest, "A valid Idempotency-Key header is required.");
        var result = await orders.CreateFromCheckoutAsync(new CreateOrderFromCheckoutRequest(request.CheckoutSessionId, OrderPaymentMethod.CashOnDelivery, key), cancellationToken);
        return result.Match<ActionResult<PublicOrderConfirmation>>(
            value => StatusCode(value.WasReplayed ? StatusCodes.Status200OK : StatusCodes.Status201Created,
                new PublicOrderConfirmation(value.OrderNumber, value.Status, value.PaymentStatus, value.FulfilmentStatus, value.PaymentMethod, value.TotalNpr, value.Currency, value.WasReplayed)),
            error => PublicError(error.Status, error.Detail));
    }

    private ActionResult<T> ToPublicReadResult<T>(Kreyora.Application.Models.Result<T> result) => result.Match<ActionResult<T>>(
        value =>
        {
            var payload = JsonSerializer.Serialize(value);
            var etag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            Response.Headers.CacheControl = $"public, max-age={options.Value.ReadCacheSeconds}";
            Response.Headers.Vary = "Host";
            Response.Headers.ETag = $"\"{etag}\"";
            var notModified = Request.Headers.TryGetValue("If-None-Match", out var existingEtags) &&
                existingEtags.Any(header => string.Equals(header, $"\"{etag}\"", StringComparison.Ordinal));
            return notModified ? StatusCode(StatusCodes.Status304NotModified) : Ok(value);
        },
        error => PublicError(error.Status, error.Detail));

    private ActionResult<T> ToPublicWriteResult<T>(Kreyora.Application.Models.Result<T> result) => result.Match<ActionResult<T>>(
        value =>
        {
            Response.Headers.CacheControl = "no-store";
            return Ok(value);
        },
        error => PublicError(error.Status, error.Detail));

    private ObjectResult PublicError(int status, string? detail)
    {
        Response.Headers.CacheControl = "no-store";
        return StatusCode(status, new ProblemDetails { Type = "https://kreyora.io/problems/public-storefront", Title = status == 404 ? "Not Found" : status == 409 ? "Conflict" : "Validation Error", Status = status, Detail = status == 404 ? "The storefront is unavailable." : detail });
    }

    private static bool TryIdempotencyKey(string? value, out string key)
    {
        key = value?.Trim() ?? string.Empty;
        return key.Length is > 0 and <= 256 && key.All(character => !char.IsControl(character));
    }

    private static PublicDeliveryQuote MapQuote(StorefrontDeliveryQuote quote) => new(
        quote.QuoteToken,
        quote.ExpiresAt,
        quote.Lines,
        MapDelivery(quote.Delivery),
        quote.Totals);

    private static PublicDeliveryOption MapDelivery(StorefrontQuoteDelivery delivery) => new(
        delivery.RuleName,
        delivery.FeeNpr,
        delivery.EstimatedEtaText,
        delivery.CodAvailable);

    private static PublicCheckoutSessionLine MapSessionLine(CheckoutSessionLineItem line) => new(
        line.VariantId,
        line.Quantity,
        line.UnitPriceNpr,
        line.LineSubtotalNpr);
}

public sealed record PublicQuoteRequest(IReadOnlyList<PublicQuoteLine> Lines, PublicDestination Destination);
public sealed record PublicQuoteLine(string VariantId, int Quantity);
public sealed record PublicDestination(string CountryCode, string District, string? Municipality, string? Locality);
public sealed record PublicCheckoutSessionRequest(string QuoteToken, PublicCheckoutCustomer Customer, PublicCheckoutAddress Address);
public sealed record PublicCheckoutCustomer(string DisplayName, string Phone, string? Email, bool SaveContact, bool PrivacyAcknowledged);
public sealed record PublicCheckoutAddress(string AddressLine1, string? AddressLine2, string District, string? Municipality, string? Locality, string? Landmark);
public sealed record PublicDeliveryQuote(string QuoteToken, DateTimeOffset ExpiresAt, IReadOnlyList<StorefrontQuoteLine> Lines, PublicDeliveryOption Delivery, StorefrontQuoteTotals Totals);
public sealed record PublicDeliveryOption(string Name, decimal FeeNpr, string? EstimatedEtaText, bool CodAvailable);
public sealed record PublicCheckoutSessionLine(string VariantId, int Quantity, decimal UnitPriceNpr, decimal LineSubtotalNpr);
public sealed record PublicCheckoutSession(string Id, DateTimeOffset ExpiresAt, IReadOnlyList<PublicCheckoutSessionLine> Items, PublicDeliveryOption Delivery, StorefrontQuoteTotals Totals, bool WasReplayed);
public sealed record PublicCreateOrderRequest(string CheckoutSessionId);
public sealed record PublicOrderConfirmation(string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, FulfilmentStatus FulfilmentStatus, OrderPaymentMethod PaymentMethod, decimal TotalNpr, string Currency, bool WasReplayed);
