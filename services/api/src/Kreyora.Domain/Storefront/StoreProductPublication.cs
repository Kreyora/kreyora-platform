using Kreyora.Domain.Common;

namespace Kreyora.Domain.Storefront;

public sealed class StoreProductPublication : BaseEntity, ITenantOwned
{
    private StoreProductPublication()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string StoreId { get; private set; } = string.Empty;
    public string ProductId { get; private set; } = string.Empty;
    public StoreProductVisibility Visibility { get; private set; }

    public static StoreProductPublication Create(string tenantId, string storeId, string productId, StoreProductVisibility visibility) => new()
    {
        TenantId = Require(tenantId, nameof(tenantId)),
        StoreId = Require(storeId, nameof(storeId)),
        ProductId = Require(productId, nameof(productId)),
        Visibility = visibility
    };

    public void SetVisibility(StoreProductVisibility visibility) => Visibility = visibility;

    private static string Require(string value, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > 26 ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
}
