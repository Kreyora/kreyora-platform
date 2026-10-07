using Kreyora.Application.Models;
using Kreyora.Domain.Storefront;

namespace Kreyora.Application.Storefront;

public interface IStorefrontAdministrationService
{
    Task<Result<StorefrontStore>> GetStoreAsync(CancellationToken cancellationToken = default);
    Task<Result<StorefrontStore>> CreateStoreAsync(CreateStoreRequest request, CancellationToken cancellationToken = default);
    Task<Result<StorefrontStore>> UpdateStoreAsync(UpdateStoreRequest request, CancellationToken cancellationToken = default);
    Task<Result<StoreReadiness>> GetReadinessAsync(CancellationToken cancellationToken = default);
    Task<Result<StorefrontStore>> ActivateStoreAsync(ActivateStoreRequest request, CancellationToken cancellationToken = default);
    Task<Result<StorePublicationPage>> ListPublicationsAsync(StorePublicationQuery query, CancellationToken cancellationToken = default);
    Task<Result<StoreProductPublicationItem>> SetProductVisibilityAsync(SetStoreProductVisibilityRequest request, CancellationToken cancellationToken = default);
}

public interface IStorefrontCatalogReadService
{
    Task<bool> IsPublishedPurchasableAsync(string productId, CancellationToken cancellationToken = default);
    Task<StorefrontCatalogVariant?> GetPublishedVariantAsync(string variantId, CancellationToken cancellationToken = default);
}

public interface IStorefrontInventoryReadService
{
    Task<int?> GetAvailableQuantityAsync(string variantId, CancellationToken cancellationToken = default);
}

public interface IDeliveryRuleReadService
{
    Task<bool> HasActiveRulesAsync(string storeId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveCodRuleAsync(string storeId, CancellationToken cancellationToken = default);
}

public interface IDeliveryRuleService
{
    Task<Result<DeliveryRuleItem>> GetAsync(string ruleId, CancellationToken cancellationToken = default);
    Task<Result<DeliveryRulePage>> ListAsync(DeliveryRuleQuery query, CancellationToken cancellationToken = default);
    Task<Result<DeliveryRuleItem>> CreateAsync(CreateDeliveryRuleRequest request, CancellationToken cancellationToken = default);
    Task<Result<DeliveryRuleItem>> UpdateAsync(UpdateDeliveryRuleRequest request, CancellationToken cancellationToken = default);
}

public interface IStorefrontQuoteService
{
    Task<Result<StorefrontDeliveryQuote>> CreateQuoteAsync(StorefrontQuoteRequest request, CancellationToken cancellationToken = default);
    Task<Result<StorefrontDeliveryQuote>> ReadQuoteAsync(string quoteToken, CancellationToken cancellationToken = default);
    Task<Result<StorefrontCheckoutQuote>> RevalidateForCheckoutAsync(string quoteToken, CancellationToken cancellationToken = default);
}

public interface IStorefrontCheckoutSessionService
{
    Task<Result<CheckoutSessionItemResult>> CreateAsync(CreateCheckoutSessionRequest request, CancellationToken cancellationToken = default);
    Task<int> ExpireDueSessionsAsync(CancellationToken cancellationToken = default);
}

public interface IPublicStorefrontService
{
    Task<Result<PublicStorefront>> GetStoreAsync(CancellationToken cancellationToken = default);
    Task<Result<PublicCatalogPage>> ListProductsAsync(PublicCatalogQuery query, CancellationToken cancellationToken = default);
    Task<Result<PublicCatalogProduct>> GetProductAsync(string productSlug, CancellationToken cancellationToken = default);
    Task<Result<PublicMediaReadContent>> OpenMediaAsync(string mediaAssetId, CancellationToken cancellationToken = default);
}

public sealed record PublicStorefrontContext(string TenantId, string StoreId, string PlatformSlug);

public interface IPublicStorefrontContextAccessor
{
    PublicStorefrontContext? Current { get; }
    PublicStorefrontContext RequireCurrent();
    IDisposable BeginScope(PublicStorefrontContext context);
}

public interface IPublicStorefrontResolver
{
    Task<PublicStorefrontContext?> ResolveAsync(string platformSlug, CancellationToken cancellationToken = default);
}

public sealed record CreateStoreRequest(StoreSettingsInput Settings, string IdempotencyKey);
public sealed record UpdateStoreRequest(StoreSettingsInput Settings, uint ExpectedVersion);
public sealed record ActivateStoreRequest(uint ExpectedVersion, string IdempotencyKey);
public sealed record SetStoreProductVisibilityRequest(string ProductId, StoreProductVisibility Visibility, uint ExpectedVersion, string IdempotencyKey);
public sealed record StorePublicationQuery(int Page, int PageSize);
public sealed record DeliveryRuleQuery(int Page, int PageSize);
public sealed record CreateDeliveryRuleRequest(DeliveryRuleInput Rule, string IdempotencyKey);
public sealed record UpdateDeliveryRuleRequest(string RuleId, DeliveryRuleInput Rule, uint ExpectedVersion);
/// <param name="AssistantLinkToken">Public: a checkout link from the assistant; that chat's held units count as available (M09-S05).</param>
/// <param name="HeldByConversationId">Internal only: the assistant quoting inside a chat; that chat's held units count as available.</param>
public sealed record StorefrontQuoteRequest(IReadOnlyList<StorefrontQuoteLineRequest> Lines, StorefrontDestinationInput Destination, string? AssistantLinkToken = null, string? HeldByConversationId = null);

/// <summary>
/// Units an assistant chat holds per variant (M09-S05), so the customer they are held for can still quote and check out
/// those units while everyone else sees them as reserved. Read-only.
/// </summary>
public interface IConversationHoldAllowance
{
    Task<IReadOnlyDictionary<string, int>> HeldAsync(string storeId, string? conversationId, string? assistantLinkToken, CancellationToken cancellationToken = default);
}
public sealed record StorefrontQuoteLineRequest(string VariantId, int Quantity);
public sealed record StorefrontDestinationInput(string CountryCode, string District, string? Municipality, string? Locality);
public sealed record CheckoutCustomerInput(string DisplayName, string Phone, string? Email, bool SaveContact, bool PrivacyAcknowledged);
public sealed record CheckoutAddressInput(string AddressLine1, string? AddressLine2, string District, string? Municipality, string? Locality, string? Landmark);
public sealed record CreateCheckoutSessionRequest(string QuoteToken, CheckoutCustomerInput Customer, CheckoutAddressInput Address, string IdempotencyKey, string? AssistantLinkToken = null);

public sealed record StoreSettingsInput(
    string DisplayName,
    string PlatformSlug,
    string? Tagline,
    StoreThemePreset ThemePreset,
    string? BrandAccentHex,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactWhatsApp,
    string? FacebookUrl,
    string? InstagramUrl,
    string? TikTokUrl,
    string? TermsPolicy,
    string? PrivacyPolicy,
    string? ReturnsPolicy,
    string? PaymentPolicy);

public sealed record StorefrontStore(
    string Id,
    string TenantId,
    string DisplayName,
    string PlatformSlug,
    string? Tagline,
    StoreStatus Status,
    StoreThemePreset ThemePreset,
    string? BrandAccentHex,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactWhatsApp,
    string? FacebookUrl,
    string? InstagramUrl,
    string? TikTokUrl,
    string? TermsPolicy,
    string? PrivacyPolicy,
    string? ReturnsPolicy,
    string? PaymentPolicy,
    DateTimeOffset? ActivatedAt,
    uint Version);

public sealed record StoreReadiness(bool CanActivate, bool CanAcceptOrders, IReadOnlyList<StoreReadinessSection> Sections, IReadOnlyList<StoreReadinessBlocker> Blockers);
public sealed record StoreReadinessSection(string Name, bool IsReady);
public sealed record StoreReadinessBlocker(string Code, string Section);
public sealed record StoreProductPublicationItem(string Id, string ProductId, StoreProductVisibility Visibility, uint Version);
public sealed record StorePublicationPage(IReadOnlyList<StoreProductPublicationItem> Items, int Page, int PageSize, int TotalCount);
public sealed record StorefrontCatalogVariant(string ProductId, string ProductTitle, string VariantId, string VariantName, decimal UnitPriceNpr);
public sealed record DeliveryRuleInput(
    string Name,
    int Priority,
    DeliveryFeeType FeeType,
    decimal BaseFeeNpr,
    decimal? FreeAboveNpr,
    string? EstimatedEtaText,
    bool CodAvailable,
    bool IsActive,
    IReadOnlyList<DeliveryZoneInput> Zones);
public sealed record DeliveryRuleZoneItem(string District, string? Municipality, string? Locality);
public sealed record DeliveryRuleItem(
    string Id,
    string Name,
    int Priority,
    DeliveryFeeType FeeType,
    decimal BaseFeeNpr,
    decimal? FreeAboveNpr,
    string? EstimatedEtaText,
    bool CodAvailable,
    bool IsActive,
    IReadOnlyList<DeliveryRuleZoneItem> Zones,
    uint Version);
public sealed record DeliveryRulePage(IReadOnlyList<DeliveryRuleItem> Items, int Page, int PageSize, int TotalCount);
public sealed record StorefrontQuoteLine(string ProductId, string ProductTitle, string VariantId, string VariantName, int Quantity, decimal UnitPriceNpr, decimal LineSubtotalNpr);
public sealed record StorefrontQuoteDelivery(string RuleId, string RuleName, decimal FeeNpr, string? EstimatedEtaText, bool CodAvailable);
public sealed record StorefrontQuoteTotals(decimal MerchandiseSubtotalNpr, decimal DiscountNpr, decimal DeliveryFeeNpr, decimal TaxNpr, decimal ProviderFeeNpr, decimal PlatformFeeNpr, decimal TotalNpr, string Currency);
public sealed record StorefrontDeliveryQuote(string QuoteToken, DateTimeOffset ExpiresAt, IReadOnlyList<StorefrontQuoteLine> Lines, StorefrontQuoteDelivery Delivery, StorefrontQuoteTotals Totals);
public sealed record StorefrontCheckoutQuote(string StoreId, DateTimeOffset QuoteExpiresAt, StorefrontDestinationInput Destination, IReadOnlyList<StorefrontQuoteLine> Lines, StorefrontQuoteDelivery Delivery, StorefrontQuoteTotals Totals);
public sealed record CheckoutSessionLineItem(string VariantId, int Quantity, string InventoryReservationId, decimal UnitPriceNpr, decimal LineSubtotalNpr);
public sealed record CheckoutSessionItemResult(string Id, string StoreId, string? CustomerId, DateTimeOffset ExpiresAt, IReadOnlyList<CheckoutSessionLineItem> Items, StorefrontQuoteDelivery Delivery, StorefrontQuoteTotals Totals, bool WasReplayed);

public sealed record PublicStorefront(
    string DisplayName,
    string PlatformSlug,
    string? Tagline,
    StoreThemePreset ThemePreset,
    string? BrandAccentHex,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactWhatsApp,
    string? FacebookUrl,
    string? InstagramUrl,
    string? TikTokUrl,
    string? TermsPolicy,
    string? PrivacyPolicy,
    string? ReturnsPolicy,
    string? PaymentPolicy);

public sealed record PublicCatalogQuery(string? Search, string? Cursor, int PageSize);
public sealed record PublicCatalogPage(IReadOnlyList<PublicCatalogProduct> Items, string? NextCursor);
public sealed record PublicCatalogProduct(string Id, string Title, string? Description, string Slug, IReadOnlyList<PublicCatalogVariant> Variants, IReadOnlyList<PublicMediaAsset> Media);
public sealed record PublicCatalogVariant(string Id, string Name, IReadOnlyDictionary<string, string> Options, decimal PriceNpr, decimal? CompareAtPriceNpr);
public sealed record PublicMediaAsset(string Id, string ContentType, string? AltText, int SortOrder);
public sealed record PublicMediaReadContent(Stream Content, string ContentType, long ByteSize);

// ---- Customer-facing read queries for the assistant (M09-S04) ----------------------------------------------------

/// <summary>Stock as customers may see it: bands only, never exact counts (M09-S04 Q3).</summary>
public enum CustomerAvailability
{
    InStock,
    LowStock,
    OutOfStock
}

public sealed record CustomerProductSummary(string ProductId, string Title, IReadOnlyDictionary<string, IReadOnlyList<string>> Options, decimal FromPriceNpr, bool Available);

public sealed record CustomerVariant(
    string VariantId,
    string Name,
    IReadOnlyDictionary<string, string> Options,
    decimal PriceNpr,
    decimal? CompareAtPriceNpr,
    CustomerAvailability Availability,
    bool? CanFulfil);

public sealed record CustomerProduct(string ProductId, string Title, string Slug, IReadOnlyList<CustomerVariant> Variants);

/// <summary>
/// The published, visible catalog of one store as its public storefront shows it, with stock reduced to bands.
/// Runs in the current tenant; unknown, unpublished, hidden or other-tenant IDs answer as missing.
/// </summary>
public interface ICustomerCatalogQuery
{
    Task<IReadOnlyList<CustomerProductSummary>> SearchAsync(string storeId, string query, int limit, int lowStockThreshold, CancellationToken cancellationToken = default);

    Task<CustomerProduct?> GetProductAsync(string storeId, string productId, int? quantity, int lowStockThreshold, CancellationToken cancellationToken = default);

    Task<CustomerProduct?> GetProductBySlugAsync(string storeId, string productSlug, int lowStockThreshold, CancellationToken cancellationToken = default);
}

public enum DeliveryInfoStatus
{
    Matched,
    PlaceUnknown,
    PlaceNotServed,
    NeedsMoreDetail,
    ItemsUnavailable
}

public sealed record DeliveryInfoItem(string VariantId, int Quantity);

public sealed record DeliveryInfoResult(
    DeliveryInfoStatus Status,
    string? MatchedPlace,
    decimal? FeeNpr,
    decimal? BaseFeeNpr,
    decimal? FreeAboveNpr,
    decimal? MerchandiseSubtotalNpr,
    string? EtaText,
    bool CodAvailable,
    bool QrAvailable,
    IReadOnlyList<string> Suggestions);

/// <summary>
/// Delivery fee, ETA and payment options for a place the customer names (M09-S04 Q5): the shop's zone names at every
/// level plus the built-in Nepal gazetteer. Item prices, when given, come from the published catalog, never the caller.
/// </summary>
public interface IDeliveryInfoQuery
{
    Task<DeliveryInfoResult> GetAsync(string storeId, string place, IReadOnlyList<DeliveryInfoItem>? items, CancellationToken cancellationToken = default);

    /// <summary>The place as the matched zone's own destination (so the quote service picks the same rule), or why it can't be matched.</summary>
    Task<DeliveryDestinationResolution> ResolveDestinationAsync(string storeId, string place, CancellationToken cancellationToken = default);
}

public sealed record DeliveryDestinationResolution(DeliveryInfoStatus Status, StorefrontDestinationInput? Destination, string? Label, IReadOnlyList<string> Suggestions);
