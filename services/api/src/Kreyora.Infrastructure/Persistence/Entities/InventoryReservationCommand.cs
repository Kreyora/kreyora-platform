using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;

namespace Kreyora.Infrastructure.Persistence.Entities;

public sealed class InventoryReservationCommand : BaseEntity, ITenantOwned
{
    private InventoryReservationCommand()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string ReservationId { get; private set; } = string.Empty;
    public InventoryReservationCommandOperation Operation { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;

    public static InventoryReservationCommand Create(
        string tenantId,
        string reservationId,
        InventoryReservationCommandOperation operation,
        string idempotencyKey,
        string requestFingerprint) => new()
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            ReservationId = Require(reservationId, nameof(reservationId), 26),
            Operation = Enum.IsDefined(operation) ? operation : throw new ArgumentOutOfRangeException(nameof(operation)),
            IdempotencyKey = Require(idempotencyKey, nameof(idempotencyKey), 256),
            RequestFingerprint = Require(requestFingerprint, nameof(requestFingerprint), 64)
        };

    private static string Require(string value, string parameterName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maxLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
}
