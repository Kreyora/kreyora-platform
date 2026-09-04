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
}

public sealed record CreateStoreRequest(StoreSettingsInput Settings, string IdempotencyKey);
public sealed record UpdateStoreRequest(StoreSettingsInput Settings, uint ExpectedVersion);
public sealed record ActivateStoreRequest(uint ExpectedVersion, string IdempotencyKey);
public sealed record SetStoreProductVisibilityRequest(string ProductId, StoreProductVisibility Visibility, uint ExpectedVersion, string IdempotencyKey);
public sealed record StorePublicationQuery(int Page, int PageSize);

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
