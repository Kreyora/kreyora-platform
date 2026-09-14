using Kreyora.Application.Models;
using Kreyora.Domain.Notifications;

namespace Kreyora.Application.Notifications;

public interface INotificationService
{
    Task<Result<PagedResult<NotificationSummary>>> GetNotificationsAsync(
        NotificationQuery query, CancellationToken cancellationToken = default);

    Task<Result<NotificationDetail>> GetNotificationAsync(
        string notificationId, CancellationToken cancellationToken = default);

    Task<Result<NotificationDetail>> ReplayAsync(
        ReplayNotificationRequest request, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<NotificationSummary>>> GetDeadLetterAsync(
        NotificationDeadLetterQuery query, CancellationToken cancellationToken = default);
}

public interface INotificationDeliveryProvider
{
    string ProviderName { get; }

    Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryRequest request, CancellationToken cancellationToken = default);
}

public interface INotificationTemplateRegistry
{
    IReadOnlyList<NotificationTemplateMapping> GetTemplatesForEvent(string eventType);

    RenderedNotification Render(
        string templateCode, int version, IReadOnlyDictionary<string, string> data);
}

public sealed record NotificationQuery(
    int Page = 1,
    int PageSize = 20,
    NotificationStatus? Status = null,
    NotificationChannel? Channel = null);

public sealed record NotificationDeadLetterQuery(
    int Page = 1,
    int PageSize = 20);

public sealed record NotificationSummary(
    string Id,
    string TenantId,
    string SourceEventType,
    string SourceEventId,
    string TemplateCode,
    int TemplateVersion,
    NotificationChannel Channel,
    string? RecipientName,
    string RecipientContactRedacted,
    NotificationStatus Status,
    int MaxAttempts,
    int AttemptCount,
    DateTimeOffset? NextRetryAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? DeadLetteredAt,
    string? LastRedactedError,
    DateTimeOffset CreatedAt);

public sealed record NotificationDetail(
    string Id,
    string TenantId,
    string SourceEventType,
    string SourceEventId,
    string TemplateCode,
    int TemplateVersion,
    NotificationChannel Channel,
    string? RecipientName,
    string RecipientContactRedacted,
    NotificationStatus Status,
    int MaxAttempts,
    int AttemptCount,
    DateTimeOffset? NextRetryAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? DeadLetteredAt,
    string? LastRedactedError,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    IReadOnlyList<NotificationDeliveryAttemptSummary> DeliveryAttempts);

public sealed record NotificationDeliveryAttemptSummary(
    string Id,
    int AttemptNumber,
    string ProviderName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool Succeeded,
    string? RedactedError,
    string? ProviderReference);

public sealed record ReplayNotificationRequest(
    string NotificationId,
    string IdempotencyKey);

public sealed record NotificationDeliveryRequest(
    string NotificationId,
    string TenantId,
    NotificationChannel Channel,
    string RecipientContact,
    string? RecipientName,
    string Subject,
    string Body,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record NotificationDeliveryResult(
    bool Succeeded,
    string? ProviderReference,
    string? RedactedError);

public sealed record RenderedNotification(
    string Subject,
    string Body);

public sealed record NotificationTemplateMapping(
    string TemplateCode,
    int TemplateVersion,
    NotificationChannel Channel);
