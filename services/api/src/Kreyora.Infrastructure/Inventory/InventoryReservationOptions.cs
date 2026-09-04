using System.ComponentModel.DataAnnotations;

namespace Kreyora.Infrastructure.Inventory;

public sealed class InventoryReservationOptions
{
    public const string SectionName = "Inventory:Reservations";

    [Range(1, 60)]
    public int DefaultDurationMinutes { get; init; } = 15;

    [Range(1, 1_000)]
    public int ExpiryBatchSize { get; init; } = 100;

    public bool ExpiryJobEnabled { get; init; } = true;

    public TimeSpan DefaultDuration => TimeSpan.FromMinutes(DefaultDurationMinutes);
}
