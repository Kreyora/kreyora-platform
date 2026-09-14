using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Models;
using Kreyora.Application.Notifications;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Notifications;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Errors;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Notifications;

public sealed class NotificationService(
    AppDbContext dbContext,
    ITenantContextAccessor contextAccessor,
    ITenantPermissionAuthorizer authorizer,
    IAuditEventService auditService,
    ITimeProvider clock) : INotificationService
{
    public async Task<Result<PagedResult<NotificationSummary>>> GetNotificationsAsync(
        NotificationQuery query, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = RequireContext();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var queryable = dbContext.NotificationRequests.AsNoTracking()
            .Where(n => n.TenantId == context.TenantId);

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(n => n.Status == query.Status.Value);
        }

        if (query.Channel.HasValue)
        {
            queryable = queryable.Where(n => n.Channel == query.Channel.Value);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);
        var items = await queryable
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var summaries = items.Select(MapSummary).ToList();

        return Result<PagedResult<NotificationSummary>>.Success(new PagedResult<NotificationSummary>
        {
            Items = summaries,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<NotificationDetail>> GetNotificationAsync(
        string notificationId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = RequireContext();

        var item = await dbContext.NotificationRequests
            .Include(n => n.DeliveryAttempts)
            .FirstOrDefaultAsync(n => n.TenantId == context.TenantId && n.Id == notificationId, cancellationToken);

        if (item is null)
        {
            return Result<NotificationDetail>.NotFound(
                $"Notification with ID '{notificationId}' was not found.");
        }

        return Result<NotificationDetail>.Success(MapDetail(item));
    }

    public async Task<Result<PagedResult<NotificationSummary>>> GetDeadLetterAsync(
        NotificationDeadLetterQuery query, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = RequireContext();

        if (context.Role is not TenantRole.Owner and not TenantRole.Admin)
        {
            return Result<PagedResult<NotificationSummary>>.Forbidden(
                "Viewing dead-letter notifications requires Owner or Admin role.");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var queryable = dbContext.NotificationRequests.AsNoTracking()
            .Where(n => n.TenantId == context.TenantId && n.Status == NotificationStatus.DeadLettered);

        var totalCount = await queryable.CountAsync(cancellationToken);
        var items = await queryable
            .OrderByDescending(n => n.DeadLetteredAt ?? n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var summaries = items.Select(MapSummary).ToList();

        return Result<PagedResult<NotificationSummary>>.Success(new PagedResult<NotificationSummary>
        {
            Items = summaries,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<NotificationDetail>> ReplayAsync(
        ReplayNotificationRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersWrite);
        var context = RequireContext();

        if (context.Role is not TenantRole.Owner and not TenantRole.Admin)
        {
            return Result<NotificationDetail>.Forbidden(
                "Replaying notifications requires Owner or Admin role.");
        }

        var item = await dbContext.NotificationRequests
            .Include(n => n.DeliveryAttempts)
            .FirstOrDefaultAsync(n => n.TenantId == context.TenantId && n.Id == request.NotificationId, cancellationToken);

        if (item is null)
        {
            return Result<NotificationDetail>.NotFound(
                $"Notification with ID '{request.NotificationId}' was not found.");
        }

        if (item.Status != NotificationStatus.DeadLettered && item.Status != NotificationStatus.Failed)
        {
            return Result<NotificationDetail>.Conflict(
                $"Cannot replay notification with status '{item.Status}'. Only DeadLettered or Failed notifications can be replayed.");
        }

        item.Replay(clock.UtcNow);

        await auditService.AppendAsync(new AuditEventWrite(
            "notification.replay",
            "NotificationRequest",
            item.Id,
            Reason: "Manual replay requested by operator",
            Metadata: JsonSerializer.Serialize(new
            {
                request.IdempotencyKey,
                item.TemplateCode,
                Channel = item.Channel.ToString(),
                item.SourceEventId
            })), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<NotificationDetail>.Success(MapDetail(item));
    }

    private TenantContext RequireContext()
    {
        var context = contextAccessor.Current;
        if (context is null)
        {
            throw new InvalidOperationException("Tenant context must be established.");
        }
        return context;
    }

    private static NotificationSummary MapSummary(NotificationRequest n) => new(
        n.Id,
        n.TenantId,
        n.SourceEventType,
        n.SourceEventId,
        n.TemplateCode,
        n.TemplateVersion,
        n.Channel,
        n.RecipientName,
        PiiRedaction.RedactContact(n.RecipientContact, n.Channel),
        n.Status,
        n.MaxAttempts,
        n.AttemptCount,
        n.NextRetryAt,
        n.DeliveredAt,
        n.DeadLetteredAt,
        n.LastRedactedError,
        n.CreatedAt);

    private static NotificationDetail MapDetail(NotificationRequest n) => new(
        n.Id,
        n.TenantId,
        n.SourceEventType,
        n.SourceEventId,
        n.TemplateCode,
        n.TemplateVersion,
        n.Channel,
        n.RecipientName,
        PiiRedaction.RedactContact(n.RecipientContact, n.Channel),
        n.Status,
        n.MaxAttempts,
        n.AttemptCount,
        n.NextRetryAt,
        n.DeliveredAt,
        n.DeadLetteredAt,
        n.LastRedactedError,
        n.IdempotencyKey,
        n.CreatedAt,
        n.DeliveryAttempts
            .OrderBy(a => a.AttemptNumber)
            .Select(a => new NotificationDeliveryAttemptSummary(
                a.Id,
                a.AttemptNumber,
                a.ProviderName,
                a.StartedAt,
                a.CompletedAt,
                a.Succeeded,
                a.RedactedError,
                a.ProviderReference))
            .ToList());
}
