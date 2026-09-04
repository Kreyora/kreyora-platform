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

public sealed record CreateStoreRequest(StoreSettingsInput Settings, string IdempotencyKey);
public sealed record UpdateStoreRequest(StoreSettingsInput Settings, uint ExpectedVersion);
public sealed record ActivateStoreRequest(uint ExpectedVersion, string IdempotencyKey);
public sealed record SetStoreProductVisibilityRequest(string ProductId, StoreProductVisibility Visibility, uint ExpectedVersion, string IdempotencyKey);
public sealed record StorePublicationQuery(int Page, int PageSize);
public sealed record DeliveryRuleQuery(int Page, int PageSize);
public sealed record CreateDeliveryRuleRequest(DeliveryRuleInput Rule, string IdempotencyKey);
public sealed record UpdateDeliveryRuleRequest(string RuleId, DeliveryRuleInput Rule, uint ExpectedVersion);
public sealed record StorefrontQuoteRequest(IReadOnlyList<StorefrontQuoteLineRequest> Lines, StorefrontDestinationInput Destination);
public sealed record StorefrontQuoteLineRequest(string VariantId, int Quantity);
public sealed record StorefrontDestinationInput(string CountryCode, string District, string? Municipality, string? Locality);
public sealed record CheckoutCustomerInput(string DisplayName, string Phone, string? Email, bool SaveContact, bool PrivacyAcknowledged);
public sealed record CheckoutAddressInput(string AddressLine1, string? AddressLine2, string District, string? Municipality, string? Locality, string? Landmark);
public sealed record CreateCheckoutSessionRequest(string QuoteToken, CheckoutCustomerInput Customer, CheckoutAddressInput Address, string IdempotencyKey);

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
