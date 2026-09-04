namespace Kreyora.Domain.Inventory;

public enum StockMovementType
{
    OpeningBalance,
    Receipt,
    CorrectionIncrease,
    CorrectionDecrease,
    Damage,
    ReservationCommitted
}
