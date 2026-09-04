using Kreyora.Application.Storefront;
using Kreyora.Domain.Catalog;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Catalog;

public sealed class StorefrontCatalogReadService(AppDbContext dbContext) : IStorefrontCatalogReadService
{
    public Task<bool> IsPublishedPurchasableAsync(string productId, CancellationToken cancellationToken = default) =>
        dbContext.Products.AnyAsync(product => product.Id == productId && product.PublishState == ProductPublishState.Published &&
            product.Variants.Any(variant => variant.IsPublished && variant.PriceNpr > 0 && variant.Sku != string.Empty), cancellationToken);
}
