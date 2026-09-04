using System.Text;
using Kreyora.Application.Models;
using Kreyora.Application.Catalog;
using Kreyora.Application.Storefront;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Storefront;

public sealed class PublicStorefrontResolver(AppDbContext dbContext) : IPublicStorefrontResolver
{
    public async Task<PublicStorefrontContext?> ResolveAsync(string platformSlug, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = Store.NormalizePlatformSlug(platformSlug).ToUpperInvariant();
        var store = await dbContext.Stores.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(item =>
            item.NormalizedPlatformSlug == normalizedSlug && item.Status == StoreStatus.Active, cancellationToken);
        if (store is null) return null;

        var hasVisibleProduct = await (from publication in dbContext.StoreProductPublications.IgnoreQueryFilters()
                                       join product in dbContext.Products.IgnoreQueryFilters() on publication.ProductId equals product.Id
                                       where publication.TenantId == store.TenantId && publication.StoreId == store.Id && publication.Visibility == StoreProductVisibility.Visible &&
                                             product.PublishState == ProductPublishState.Published && product.Variants.Any(variant => variant.IsPublished && variant.PriceNpr > 0 && variant.Sku != string.Empty)
                                       select publication.Id).AnyAsync(cancellationToken);
        var hasCodDelivery = await dbContext.DeliveryRules.IgnoreQueryFilters().AnyAsync(rule => rule.TenantId == store.TenantId && rule.StoreId == store.Id && rule.IsActive && rule.CodAvailable && rule.Zones.Any(), cancellationToken);
        var profileAndPoliciesReady = !string.IsNullOrWhiteSpace(store.ContactName) &&
            !string.IsNullOrWhiteSpace(store.ContactPhone) &&
            !string.IsNullOrWhiteSpace(store.TermsPolicy) &&
            !string.IsNullOrWhiteSpace(store.PrivacyPolicy) &&
            !string.IsNullOrWhiteSpace(store.ReturnsPolicy) &&
            !string.IsNullOrWhiteSpace(store.PaymentPolicy);
        return hasVisibleProduct && hasCodDelivery && profileAndPoliciesReady
            ? new PublicStorefrontContext(store.TenantId, store.Id, store.PlatformSlug)
            : null;
    }
}

public sealed class PublicStorefrontService(
    AppDbContext dbContext,
    IPublicStorefrontContextAccessor contextAccessor,
    IPrivateObjectStorage storage) : IPublicStorefrontService
{
    public async Task<Result<PublicStorefront>> GetStoreAsync(CancellationToken cancellationToken = default)
    {
        var context = contextAccessor.RequireCurrent();
        var store = await dbContext.Stores.AsNoTracking().SingleOrDefaultAsync(item => item.Id == context.StoreId && item.Status == StoreStatus.Active, cancellationToken);
        return store is null ? Result<PublicStorefront>.NotFound("The storefront is unavailable.") : Result<PublicStorefront>.Success(MapStore(store));
    }

    public async Task<Result<PublicCatalogPage>> ListProductsAsync(PublicCatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = contextAccessor.RequireCurrent();
        try
        {
            var pageSize = Math.Clamp(query.PageSize, 1, 50);
            var search = NormalizeSearch(query.Search);
            var marker = DecodeCursor(query.Cursor);
            var products = dbContext.Products.AsNoTracking()
                .Where(product => product.PublishState == ProductPublishState.Published &&
                    dbContext.StoreProductPublications.Any(publication => publication.StoreId == context.StoreId && publication.ProductId == product.Id && publication.Visibility == StoreProductVisibility.Visible) &&
                    product.Variants.Any(variant => variant.IsPublished && variant.PriceNpr > 0 && variant.Sku != string.Empty));
            if (search is not null)
            {
                products = products.Where(product => EF.Functions.ILike(product.Title, $"%{search}%") || EF.Functions.ILike(product.Slug, $"%{search}%"));
            }

            products = products.OrderBy(product => product.Slug).ThenBy(product => product.Id);
            if (marker is not null)
            {
                products = products.Where(product => product.Slug.CompareTo(marker.Value.Slug) > 0 ||
                    (product.Slug == marker.Value.Slug && product.Id.CompareTo(marker.Value.Id) > 0));
            }

            var page = await products.Include(product => product.Variants).Take(pageSize + 1).ToListAsync(cancellationToken);
            var hasMore = page.Count > pageSize;
            var selected = page.Take(pageSize).ToArray();
            var media = await MediaForProductsAsync(selected.Select(product => product.Id).ToArray(), context.StoreId, cancellationToken);
            var items = selected.Select(product => MapProduct(product, media.GetValueOrDefault(product.Id, []))).ToArray();
            var last = items.LastOrDefault();
            return Result<PublicCatalogPage>.Success(new PublicCatalogPage(items, hasMore && last is not null ? EncodeCursor(last.Slug, last.Id) : null));
        }
        catch (ArgumentException exception)
        {
            return Result<PublicCatalogPage>.ValidationError(exception.Message);
        }
    }

    public async Task<Result<PublicCatalogProduct>> GetProductAsync(string productSlug, CancellationToken cancellationToken = default)
    {
        var context = contextAccessor.RequireCurrent();
        var normalizedSlug = NormalizeSlug(productSlug);
        var product = await dbContext.Products.AsNoTracking().Include(item => item.Variants).SingleOrDefaultAsync(item =>
            item.Slug == normalizedSlug && item.PublishState == ProductPublishState.Published &&
            dbContext.StoreProductPublications.Any(publication => publication.StoreId == context.StoreId && publication.ProductId == item.Id && publication.Visibility == StoreProductVisibility.Visible) &&
            item.Variants.Any(variant => variant.IsPublished && variant.PriceNpr > 0 && variant.Sku != string.Empty), cancellationToken);
        if (product is null) return Result<PublicCatalogProduct>.NotFound("The storefront is unavailable.");
        var media = await MediaForProductsAsync([product.Id], context.StoreId, cancellationToken);
        return Result<PublicCatalogProduct>.Success(MapProduct(product, media.GetValueOrDefault(product.Id, [])));
    }

    public async Task<Result<PublicMediaReadContent>> OpenMediaAsync(string mediaAssetId, CancellationToken cancellationToken = default)
    {
        var context = contextAccessor.RequireCurrent();
        var asset = await (from media in dbContext.MediaAssets.AsNoTracking()
                           join product in dbContext.Products.AsNoTracking() on media.ProductId equals product.Id
                           join publication in dbContext.StoreProductPublications.AsNoTracking() on product.Id equals publication.ProductId
                           where media.Id == mediaAssetId && media.State == MediaAssetState.Ready && publication.StoreId == context.StoreId &&
                                 publication.Visibility == StoreProductVisibility.Visible && product.PublishState == ProductPublishState.Published
                           select media).SingleOrDefaultAsync(cancellationToken);
        if (asset is null) return Result<PublicMediaReadContent>.NotFound("The storefront is unavailable.");
        var content = await storage.OpenReadAsync(asset.ObjectKey, cancellationToken);
        return content is null
            ? Result<PublicMediaReadContent>.NotFound("The storefront is unavailable.")
            : Result<PublicMediaReadContent>.Success(new PublicMediaReadContent(content, asset.ContentType, asset.ByteSize));
    }

    private async Task<Dictionary<string, IReadOnlyList<PublicMediaAsset>>> MediaForProductsAsync(string[] productIds, string storeId, CancellationToken cancellationToken)
    {
        if (productIds.Length == 0) return [];
        var assets = await (from media in dbContext.MediaAssets.AsNoTracking()
                            join publication in dbContext.StoreProductPublications.AsNoTracking() on media.ProductId equals publication.ProductId
                            where productIds.Contains(media.ProductId!) && media.State == MediaAssetState.Ready && publication.StoreId == storeId && publication.Visibility == StoreProductVisibility.Visible
                            orderby media.SortOrder, media.Id
                            select new { media.ProductId, Asset = new PublicMediaAsset(media.Id, media.ContentType, media.AltText, media.SortOrder ?? 0) })
            .ToListAsync(cancellationToken);
        return assets.GroupBy(item => item.ProductId!).ToDictionary(group => group.Key, group => (IReadOnlyList<PublicMediaAsset>)group.Select(item => item.Asset).ToArray());
    }

    private static PublicStorefront MapStore(Store store) => new(store.DisplayName, store.PlatformSlug, store.Tagline, store.ThemePreset, store.BrandAccentHex,
        store.ContactName, store.ContactEmail, store.ContactPhone, store.ContactWhatsApp, store.FacebookUrl, store.InstagramUrl, store.TikTokUrl,
        store.TermsPolicy, store.PrivacyPolicy, store.ReturnsPolicy, store.PaymentPolicy);
    private static PublicCatalogProduct MapProduct(Product product, IReadOnlyList<PublicMediaAsset> media) => new(product.Id, product.Title, product.Description, product.Slug,
        product.Variants.Where(variant => variant.IsPublished && variant.PriceNpr > 0 && variant.Sku != string.Empty)
            .Select(variant => new PublicCatalogVariant(variant.Id, variant.Name, variant.GetOptions(), variant.PriceNpr, variant.CompareAtPriceNpr)).ToArray(), media);
    private static string? NormalizeSearch(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim().Length <= 64 ? search.Trim() : throw new ArgumentOutOfRangeException(nameof(search));
    private static string NormalizeSlug(string slug) => string.IsNullOrWhiteSpace(slug) || slug.Trim().Length > 160 ? throw new ArgumentException("The product is unavailable.", nameof(slug)) : slug.Trim().ToLowerInvariant();
    private static string EncodeCursor(string slug, string id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{slug}|{id}"));
    private static (string Slug, string Id)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            return parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]) ? (parts[0], parts[1]) : throw new ArgumentException("The product cursor is invalid.", nameof(cursor));
        }
        catch (FormatException)
        {
            throw new ArgumentException("The product cursor is invalid.", nameof(cursor));
        }
    }
}
