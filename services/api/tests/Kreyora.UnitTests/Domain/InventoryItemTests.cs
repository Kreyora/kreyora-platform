using Kreyora.Domain.Inventory;

namespace Kreyora.UnitTests.Domain;

public class InventoryItemTests
{
    private const string TenantId = "01J00000000000000000000001";
    private const string VariantId = "01J00000000000000000000002";

    [Fact]
    public void ApplyMovement_DerivesAvailableQuantity_AndLowStockState()
    {
        var item = InventoryItem.Create(TenantId, VariantId);

        item.ApplyMovement(5);
        item.SetLowStockThreshold(5);

        Assert.Equal(5, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(5, item.AvailableQuantity);
        Assert.True(item.IsLowStock);
    }

    [Fact]
    public void ApplyMovement_RejectsNegativeResult_AndZeroDelta()
    {
        var item = InventoryItem.Create(TenantId, VariantId);
        item.ApplyMovement(2);

        Assert.Throws<InvalidOperationException>(() => item.ApplyMovement(-3));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.ApplyMovement(0));
    }

    [Fact]
    public void SetLowStockThreshold_RejectsNegativeValue()
    {
        var item = InventoryItem.Create(TenantId, VariantId);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.SetLowStockThreshold(-1));
        Assert.False(item.IsLowStock);
    }

    [Fact]
    public void CreateStockMovement_RequiresImmutableLedgerFacts()
    {
        var movement = StockMovement.Create(
            TenantId,
            "01J00000000000000000000003",
            VariantId,
            StockMovementType.Receipt,
            4,
            "Supplier delivery received",
            "01J00000000000000000000004",
            "inventory-adjustment-1",
            new string('A', 64));

        Assert.Equal(4, movement.QuantityDelta);
        Assert.Equal(StockMovementType.Receipt, movement.Type);
        Assert.Throws<ArgumentOutOfRangeException>(() => StockMovement.Create(
            TenantId,
            "01J00000000000000000000003",
            VariantId,
            StockMovementType.Receipt,
            0,
            "Supplier delivery received",
            "01J00000000000000000000004",
            "inventory-adjustment-2",
            new string('B', 64)));
    }
}
