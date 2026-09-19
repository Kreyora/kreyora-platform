using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public sealed record WebhookProcessingResult(
    bool Succeeded,
    string WebhookEventId,
    WebhookProcessingStatus Status,
    int NormalizedEventsCount,
    bool IsDuplicate = false,
    string? ErrorMessage = null,
    WebhookFailureClassification? FailureClassification = null)
{
    public static WebhookProcessingResult Success(string webhookEventId, int count) =>
        new(true, webhookEventId, WebhookProcessingStatus.Processed, count);

    public static WebhookProcessingResult Duplicate(string webhookEventId) =>
        new(true, webhookEventId, WebhookProcessingStatus.Processed, 0, IsDuplicate: true);

    public static WebhookProcessingResult Failed(
        string webhookEventId,
        WebhookProcessingStatus status,
        string errorMessage,
        WebhookFailureClassification classification) =>
        new(false, webhookEventId, status, 0, ErrorMessage: errorMessage, FailureClassification: classification);
}

public sealed record WebhookDeadLetterDto(
    string Id,
    string TenantId,
    string ConnectionId,
    ChannelType Channel,
    string ProviderEventId,
    string? EventType,
    string? ErrorMessage,
    WebhookFailureClassification? FailureClassification,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? DeadLetteredAt,
    string CorrelationId);

public sealed record WebhookDeadLetterQuery(
    int Page = 1,
    int PageSize = 20,
    ChannelType? Channel = null);

public sealed record WebhookReplayResult(
    bool Succeeded,
    string WebhookEventId,
    WebhookProcessingStatus Status,
    string IdempotencyKey,
    string? ErrorMessage = null)
{
    public static WebhookReplayResult Success(string webhookEventId, string idempotencyKey) =>
        new(true, webhookEventId, WebhookProcessingStatus.Received, idempotencyKey);

    public static WebhookReplayResult Failed(string webhookEventId, string idempotencyKey, string errorMessage) =>
        new(false, webhookEventId, WebhookProcessingStatus.DeadLetter, idempotencyKey, errorMessage);
}

public sealed record ReplayWebhookEventRequest(string IdempotencyKey);

