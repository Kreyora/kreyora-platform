using Kreyora.Application.Models;
using Kreyora.Domain.Common;

namespace Kreyora.Application.Audit;

public interface IAuditEventService
{
    Task AppendAsync(AuditEventWrite write, CancellationToken cancellationToken = default);
    Task<CursorPage<AuditEventItem>> GetPageAsync(string? cursor, int pageSize, CancellationToken cancellationToken = default);
}

public sealed record AuditEventWrite(
    string Action,
    string TargetType,
    string TargetId,
    string? Reason = null,
    string? Metadata = null,
    string? ActorUserId = null,
    CommerceActorKind? ActorKind = null);

public sealed record AuditEventItem(
    string Id, string? ActorUserId, CommerceActorKind ActorKind, string? EffectiveSupportActorUserId, string Action,
    string TargetType, string TargetId, DateTimeOffset OccurredAt, string? Reason,
    string CorrelationId, string? Metadata);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
