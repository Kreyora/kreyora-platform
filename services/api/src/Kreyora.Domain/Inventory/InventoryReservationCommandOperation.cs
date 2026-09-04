namespace Kreyora.Domain.Inventory;

public enum InventoryReservationCommandOperation
{
    Reserve,
    Commit,
    Release,
    Expire
}
