using Kreyora.Domain.Common;

namespace Kreyora.Infrastructure.Persistence.Entities;

public sealed class StoreCommandIdempotency : BaseEntity, ITenantOwned
{
    private StoreCommandIdempotency()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;

    public static StoreCommandIdempotency Create(string tenantId, string operation, string idempotencyKey, string requestFingerprint, string resourceId) => new()
    {
        TenantId = Require(tenantId, nameof(tenantId), 26),
        Operation = Require(operation, nameof(operation), 100),
        IdempotencyKey = Require(idempotencyKey, nameof(idempotencyKey), 256),
        RequestFingerprint = Require(requestFingerprint, nameof(requestFingerprint), 64),
        ResourceId = Require(resourceId, nameof(resourceId), 26)
    };

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
}
