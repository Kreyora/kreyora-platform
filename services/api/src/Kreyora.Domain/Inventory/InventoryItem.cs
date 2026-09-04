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

    public void Reserve(int quantity)
    {
        RequirePositive(quantity, nameof(quantity));
        if (quantity > AvailableQuantity)
        {
            throw new InvalidOperationException("The requested quantity exceeds available inventory.");
        }

        ReservedQuantity = checked(ReservedQuantity + quantity);
    }

    public void ReleaseReservation(int quantity)
    {
        RequirePositive(quantity, nameof(quantity));
        if (quantity > ReservedQuantity)
        {
            throw new InvalidOperationException("The reservation quantity exceeds currently reserved inventory.");
        }

        ReservedQuantity -= quantity;
    }

    public void CommitReservation(int quantity)
    {
        RequirePositive(quantity, nameof(quantity));
        if (quantity > ReservedQuantity || quantity > OnHandQuantity)
        {
            throw new InvalidOperationException("The reservation cannot be committed from the current inventory balance.");
        }

        ReservedQuantity -= quantity;
        OnHandQuantity -= quantity;
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Quantity must be greater than zero.");
        }
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
