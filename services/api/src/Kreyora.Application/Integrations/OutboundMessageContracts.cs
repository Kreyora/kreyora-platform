using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public sealed record QueueOutboundMessageRequest(
    string ConnectionId,
    string RecipientChannelId,
    string IdempotencyKey,
    OutboundMessageType MessageType,
    string? TextContent = null,
    string? MediaUrl = null,
    string? MediaContentType = null,
    string? Caption = null,
    string? TemplateCode = null,
    string? TemplateParametersJson = null,
    string? MetadataJson = null,
    string? ConversationId = null,
    int? MaxAttempts = null);

public sealed record OutboundMessageDto(
    string Id,
    string TenantId,
    string ConnectionId,
    ChannelType Channel,
    string? ConversationId,
    string RecipientChannelId,
    string IdempotencyKey,
    OutboundMessageType MessageType,
    string? TextContent,
    string? MediaUrl,
    string? MediaContentType,
    string? Caption,
    string? TemplateCode,
    string? TemplateParametersJson,
    string? MetadataJson,
    OutboundMessageStatus Status,
    string? ProviderMessageId,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? NextRetryAt,
    WebhookFailureClassification? FailureClassification,
    string? LastErrorMessage,
    DateTimeOffset QueuedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset? DeadLetteredAt,
    DateTimeOffset? CancelledAt);

public sealed record OutboundDeliveryAttemptDto(
    string Id,
    string OutboundMessageId,
    int AttemptNumber,
    ChannelType Channel,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool Succeeded,
    string? ProviderMessageId,
    string? ProviderErrorCode,
    string? ProviderErrorMessage);

public sealed record OutboundMessageResult(
    bool Succeeded,
    string? OutboundMessageId,
    OutboundMessageStatus? Status,
    string? ErrorMessage = null,
    bool IsIdempotentDuplicate = false)
{
    public static OutboundMessageResult Success(string messageId, OutboundMessageStatus status, bool isDuplicate = false) =>
        new(true, messageId, status, IsIdempotentDuplicate: isDuplicate);

    public static OutboundMessageResult Failed(string errorMessage) =>
        new(false, null, null, ErrorMessage: errorMessage);
}

public sealed record OutboundMessageQuery(
    int Page = 1,
    int PageSize = 20,
    OutboundMessageStatus? Status = null,
    ChannelType? Channel = null,
    string? ConnectionId = null);

public sealed record OutboundDeadLetterDto(
    string Id,
    string TenantId,
    string ConnectionId,
    ChannelType Channel,
    string RecipientChannelId,
    string IdempotencyKey,
    OutboundMessageType MessageType,
    string? ContentSummary,
    string? LastErrorMessage,
    WebhookFailureClassification? FailureClassification,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset QueuedAt,
    DateTimeOffset? DeadLetteredAt);

public sealed record OutboundDeadLetterQuery(
    int Page = 1,
    int PageSize = 20,
    ChannelType? Channel = null);

public sealed record ReplayOutboundMessageRequest(string IdempotencyKey);

