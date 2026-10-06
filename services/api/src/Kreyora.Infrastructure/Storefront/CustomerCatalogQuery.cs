using Kreyora.Application.Storefront;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Storefront;

/// <summary>
/// The catalog exactly as the store's public storefront shows it (reuses <see cref="IPublicStorefrontService"/>), with
/// stock from <see cref="IStorefrontInventoryReadService"/> reduced to bands (M09-S04 Q3). Runs in the current tenant.
/// </summary>
public sealed class CustomerCatalogQuery(
    AppDbContext dbContext,
    IPublicStorefrontContextAccessor publicContext,
    IPublicStorefrontService storefront,
    IStorefrontInventoryReadService inventory) : ICustomerCatalogQuery
{
    private const int MaxSearchTerms = 4;
    private const int CandidatesPerTerm = 20;

    public async Task<IReadOnlyList<CustomerProductSummary>> SearchAsync(string storeId, string query, int limit, int lowStockThreshold, CancellationToken cancellationToken = default)
    {
        var scope = await BeginStoreScopeAsync(storeId, cancellationToken);
        if (scope is null || string.IsNullOrWhiteSpace(query)) return [];
        using (scope)
        {
            // The whole phrase first, then single words (any language): products matching more terms rank higher.
            var phrase = query.Trim();
            var terms = new List<string>();
            if (phrase.Length <= 64) terms.Add(phrase);
            terms.AddRange(KnowledgeTokenizer.Tokens(phrase).Where(t => t.Length >= 2).Distinct(StringComparer.Ordinal).Take(MaxSearchTerms));

            var hits = new Dictionary<string, (PublicCatalogProduct Product, int Score)>(StringComparer.Ordinal);
            foreach (var (term, index) in terms.Distinct(StringComparer.OrdinalIgnoreCase).Select((t, i) => (t, i)))
            {
                var page = await storefront.ListProductsAsync(new PublicCatalogQuery(term, null, CandidatesPerTerm), cancellationToken);
                if (page.IsFailure) continue;
                foreach (var product in page.Value!.Items)
                {
                    var weight = index == 0 && terms.Count > 1 ? 2 : 1;
                    hits[product.Id] = hits.TryGetValue(product.Id, out var hit) ? (product, hit.Score + weight) : (product, weight);
                }
            }

            var results = new List<CustomerProductSummary>();
            foreach (var (product, _) in hits.Values.OrderByDescending(h => h.Score).ThenBy(h => h.Product.Title, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Product.Id, StringComparer.Ordinal).Take(Math.Clamp(limit, 1, 5)))
            {
                var variants = await MapVariantsAsync(product, null, lowStockThreshold, cancellationToken);
                results.Add(new CustomerProductSummary(product.Id, product.Title, OptionsOf(product),
                    product.Variants.Min(v => v.PriceNpr), variants.Any(v => v.Availability != CustomerAvailability.OutOfStock)));
            }

            return results;
        }
    }

    public async Task<CustomerProduct?> GetProductAsync(string storeId, string productId, int? quantity, int lowStockThreshold, CancellationToken cancellationToken = default)
    {
        // Tenant query filter: another tenant's product ID resolves to nothing.
        var slug = await dbContext.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Slug).SingleOrDefaultAsync(cancellationToken);
        return slug is null ? null : await GetBySlugAsync(storeId, slug, quantity, lowStockThreshold, cancellationToken);
    }

    public Task<CustomerProduct?> GetProductBySlugAsync(string storeId, string productSlug, int lowStockThreshold, CancellationToken cancellationToken = default) =>
        GetBySlugAsync(storeId, productSlug, null, lowStockThreshold, cancellationToken);

    private async Task<CustomerProduct?> GetBySlugAsync(string storeId, string slug, int? quantity, int lowStockThreshold, CancellationToken cancellationToken)
    {
        var scope = await BeginStoreScopeAsync(storeId, cancellationToken);
        if (scope is null) return null;
        using (scope)
        {
            try
            {
                var product = await storefront.GetProductAsync(slug, cancellationToken);
                if (product.IsFailure) return null; // unpublished, hidden or unknown
                var value = product.Value!;
                return new CustomerProduct(value.Id, value.Title, value.Slug, await MapVariantsAsync(value, quantity, lowStockThreshold, cancellationToken));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    private async Task<IReadOnlyList<CustomerVariant>> MapVariantsAsync(PublicCatalogProduct product, int? quantity, int lowStockThreshold, CancellationToken cancellationToken)
    {
        var variants = new List<CustomerVariant>(product.Variants.Count);
        foreach (var variant in product.Variants)
        {
            var available = await inventory.GetAvailableQuantityAsync(variant.Id, cancellationToken) ?? 0;
            variants.Add(new CustomerVariant(variant.Id, variant.Name, variant.Options, variant.PriceNpr, variant.CompareAtPriceNpr,
                Band(available, lowStockThreshold), quantity is null ? null : available >= quantity));
        }

        return variants;
    }

    /// <summary>Stock band shown to customers; exact counts never leave this method.</summary>
    public static CustomerAvailability Band(int available, int lowStockThreshold) =>
        available <= 0 ? CustomerAvailability.OutOfStock
        : available <= lowStockThreshold ? CustomerAvailability.LowStock
        : CustomerAvailability.InStock;

    private static Dictionary<string, IReadOnlyList<string>> OptionsOf(PublicCatalogProduct product) =>
        product.Variants.SelectMany(v => v.Options)
            .GroupBy(o => o.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(o => o.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);

    private async Task<IDisposable?> BeginStoreScopeAsync(string storeId, CancellationToken cancellationToken)
    {
        var store = await dbContext.Stores.AsNoTracking().Where(s => s.Id == storeId).Select(s => new { s.TenantId, s.PlatformSlug }).SingleOrDefaultAsync(cancellationToken);
        return store is null ? null : publicContext.BeginScope(new PublicStorefrontContext(store.TenantId, storeId, store.PlatformSlug));
    }
}
