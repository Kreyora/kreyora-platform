using Kreyora.Application.Storefront;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Inventory;

public sealed class StorefrontInventoryReadService(AppDbContext dbContext) : IStorefrontInventoryReadService
{
    public Task<int?> GetAvailableQuantityAsync(string variantId, CancellationToken cancellationToken = default) =>
        dbContext.InventoryItems.Where(item => item.VariantId == variantId)
            .Select(item => (int?)(item.OnHandQuantity - item.ReservedQuantity))
            .SingleOrDefaultAsync(cancellationToken);
}
