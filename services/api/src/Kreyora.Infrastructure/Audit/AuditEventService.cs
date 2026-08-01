using System.Text;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Audit;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Audit;

public sealed class AuditEventService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ICorrelationContext correlation,
    ITenantPermissionAuthorizer permissionAuthorizer) : IAuditEventService
{
    public async Task AppendAsync(AuditEventWrite write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var context = tenantContext.RequireCurrent();
        var actor = write.ActorUserId ?? context.UserId
            ?? throw new InvalidOperationException("Audit events require an actor user.");
        dbContext.AuditEvents.Add(AuditEvent.Create(
            context.TenantId, actor, write.Action, write.TargetType, write.TargetId,
            DateTimeOffset.UtcNow, correlation.CorrelationId, write.Reason,
            AuditMetadataSanitizer.Sanitize(write.Metadata),
            context.IsReadOnlySupport ? context.UserId : null,
            context.IsReadOnlySupport ? context.SupportAccessGrantId : null));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CursorPage<AuditEventItem>> GetPageAsync(string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.AuditRead);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var context = tenantContext.RequireCurrent();
        var query = dbContext.AuditEvents.AsNoTracking()
            .Where(item => item.TenantId == context.TenantId)
            .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id);
        var marker = DecodeCursor(cursor);
        if (marker is not null)
        {
            query = query.Where(item => item.OccurredAt < marker.Value.OccurredAt ||
                (item.OccurredAt == marker.Value.OccurredAt && item.Id.CompareTo(marker.Value.Id) < 0))
                .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id);
        }

        var records = await query.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = records.Count > pageSize;
        var items = records.Take(pageSize).Select(item => new AuditEventItem(
            item.Id, item.ActorUserId, item.EffectiveSupportActorUserId, item.Action, item.TargetType,
            item.TargetId, item.OccurredAt, item.Reason, item.CorrelationId, item.Metadata)).ToArray();
        var last = items.LastOrDefault();
        var next = hasMore && last is not null ? EncodeCursor(last.OccurredAt, last.Id) : null;
        return new CursorPage<AuditEventItem>(items, next);
    }

    private static string EncodeCursor(DateTimeOffset occurredAt, string id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{occurredAt.UtcTicks}|{id}"));

    private static (DateTimeOffset OccurredAt, string Id)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            return parts.Length == 2 && long.TryParse(parts[0], out var ticks) && !string.IsNullOrWhiteSpace(parts[1])
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]) : null;
        }
        catch (FormatException)
        {
            throw new ArgumentException("The audit cursor is invalid.", nameof(cursor));
        }
    }
}
