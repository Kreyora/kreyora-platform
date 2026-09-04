using Kreyora.Domain.Common;

namespace Kreyora.Infrastructure.Persistence.Entities;

public sealed class CheckoutSessionCommand : BaseEntity, ITenantOwned
{
    private CheckoutSessionCommand() { }
    public string TenantId { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string CheckoutSessionId { get; private set; } = string.Empty;
    public static CheckoutSessionCommand Create(string tenantId, string operation, string idempotencyKey, string requestFingerprint, string checkoutSessionId) => new()
    {
        TenantId = Require(tenantId, nameof(tenantId), 26), Operation = Require(operation, nameof(operation), 100), IdempotencyKey = Require(idempotencyKey, nameof(idempotencyKey), 256), RequestFingerprint = Require(requestFingerprint, nameof(requestFingerprint), 64), CheckoutSessionId = Require(checkoutSessionId, nameof(checkoutSessionId), 26)
    };
    private static string Require(string value, string parameterName, int max) { var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim(); return normalized.Length > max ? throw new ArgumentOutOfRangeException(parameterName) : normalized; }
}
