using Kreyora.Domain.Common;

namespace Kreyora.Domain.Inventory;

public sealed class InventoryItem : BaseEntity, ITenantOwned
{
    private InventoryItem()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string VariantId { get; private set; } = string.Empty;
    public int OnHandQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int LowStockThreshold { get; private set; }
    public int AvailableQuantity => OnHandQuantity - ReservedQuantity;
    public bool IsLowStock => LowStockThreshold > 0 && AvailableQuantity <= LowStockThreshold;

    public static InventoryItem Create(string tenantId, string variantId) => new()
    {
        TenantId = RequireId(tenantId, nameof(tenantId)),
        VariantId = RequireId(variantId, nameof(variantId))
    };

    public void ApplyMovement(int quantityDelta)
    {
        if (quantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Stock movement quantity cannot be zero.");
        }

        var resultingOnHand = checked(OnHandQuantity + quantityDelta);
        if (resultingOnHand < ReservedQuantity)
        {
            throw new InvalidOperationException("The stock adjustment would make available inventory negative.");
        }

        OnHandQuantity = resultingOnHand;
    }

    public void SetLowStockThreshold(int threshold)
    {
        if (threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Low-stock threshold cannot be negative.");
        }

        LowStockThreshold = threshold;
    }

    private static string RequireId(string value, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
        return normalized.Length > 26
            ? throw new ArgumentOutOfRangeException(parameterName, "Value cannot exceed 26 characters.")
            : normalized;
    }
}
