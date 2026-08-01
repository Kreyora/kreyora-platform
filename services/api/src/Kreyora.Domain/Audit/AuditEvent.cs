using Kreyora.Domain.Common;

namespace Kreyora.Domain.Audit;

/// <summary>Immutable, tenant-scoped security and business audit record.</summary>
public sealed class AuditEvent : BaseEntity, ITenantOwned
{
    public const int ActionMaxLength = 120;
    public const int TargetTypeMaxLength = 120;
    public const int TargetIdMaxLength = 160;
    public const int ReasonMaxLength = 500;
    public const int CorrelationIdMaxLength = 128;

    private AuditEvent()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string ActorUserId { get; private set; } = string.Empty;
    public string? EffectiveSupportActorUserId { get; private set; }
    public string? SupportAccessGrantId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string TargetId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reason { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? Metadata { get; private set; }

    public static AuditEvent Create(
        string tenantId,
        string actorUserId,
        string action,
        string targetType,
        string targetId,
        DateTimeOffset occurredAt,
        string correlationId,
        string? reason = null,
        string? metadata = null,
        string? effectiveSupportActorUserId = null,
        string? supportAccessGrantId = null) => new()
        {
            TenantId = Require(tenantId, nameof(tenantId), TargetIdMaxLength),
            ActorUserId = Require(actorUserId, nameof(actorUserId), TargetIdMaxLength),
            Action = Require(action, nameof(action), ActionMaxLength),
            TargetType = Require(targetType, nameof(targetType), TargetTypeMaxLength),
            TargetId = Require(targetId, nameof(targetId), TargetIdMaxLength),
            OccurredAt = occurredAt,
            CorrelationId = Require(correlationId, nameof(correlationId), CorrelationIdMaxLength),
            Reason = Optional(reason, ReasonMaxLength),
            Metadata = metadata,
            EffectiveSupportActorUserId = string.IsNullOrWhiteSpace(effectiveSupportActorUserId) ? null : effectiveSupportActorUserId,
            SupportAccessGrantId = string.IsNullOrWhiteSpace(supportAccessGrantId) ? null : supportAccessGrantId
        };

    private static string Require(string value, string paramName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", paramName) : value.Trim();
        return normalized.Length > maxLength ? throw new ArgumentOutOfRangeException(paramName) : normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length > maxLength ? throw new ArgumentOutOfRangeException(nameof(value)) : normalized;
    }
}
