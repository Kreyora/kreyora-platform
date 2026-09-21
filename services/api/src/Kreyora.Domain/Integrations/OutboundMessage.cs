using Kreyora.Domain.Common;
using Kreyora.Domain.Tenancy;

namespace Kreyora.Domain.Integrations;

public sealed class OutboundMessage : BaseEntity, ITenantOwned
{
    public const int RecipientChannelIdMaxLength = 256;
    public const int IdempotencyKeyMaxLength = 128;
    public const int TextContentMaxLength = 4096;
    public const int MediaUrlMaxLength = 2048;
    public const int MediaContentTypeMaxLength = 128;
    public const int CaptionMaxLength = 1024;
    public const int TemplateCodeMaxLength = 256;
    public const int ProviderMessageIdMaxLength = 256;
    public const int LastErrorMessageMaxLength = 2048;
    public const int ConversationIdMaxLength = 64;

    public string TenantId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public ChannelType Channel { get; private set; }
    public string? ConversationId { get; private set; }
    public string RecipientChannelId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public OutboundMessageType MessageType { get; private set; }
    public string? TextContent { get; private set; }
    public string? MediaUrl { get; private set; }
    public string? MediaContentType { get; private set; }
    public string? Caption { get; private set; }
    public string? TemplateCode { get; private set; }
    public string? TemplateParametersJson { get; private set; }
    public string? MetadataJson { get; private set; }
    public OutboundMessageStatus Status { get; private set; } = OutboundMessageStatus.Queued;
    public string? ProviderMessageId { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = WebhookRetryPolicy.DefaultMaxAttempts;
    public DateTimeOffset? NextRetryAt { get; private set; }
    public WebhookFailureClassification? FailureClassification { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    private OutboundMessage() { }

    public static OutboundMessage Create(
        string tenantId,
        string connectionId,
        ChannelType channel,
        string recipientChannelId,
        string idempotencyKey,
        OutboundMessageType messageType,
        string? textContent = null,
        string? mediaUrl = null,
        string? mediaContentType = null,
        string? caption = null,
        string? templateCode = null,
        string? templateParametersJson = null,
        string? metadataJson = null,
        string? conversationId = null,
        DateTimeOffset? queuedAt = null,
        int maxAttempts = WebhookRetryPolicy.DefaultMaxAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientChannelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be at least 1.");
        }

        var trimmedRecipient = RequireBounded(recipientChannelId, nameof(recipientChannelId), RecipientChannelIdMaxLength);
        var trimmedIdempotencyKey = RequireBounded(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength);
        var trimmedConversationId = OptionalBounded(conversationId, ConversationIdMaxLength);

        // Validate content against message type
        switch (messageType)
        {
            case OutboundMessageType.Text:
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    throw new ArgumentException("Text content is required for text messages.", nameof(textContent));
                }
                break;

            case OutboundMessageType.Media:
                if (string.IsNullOrWhiteSpace(mediaUrl))
                {
                    throw new ArgumentException("Media URL is required for media messages.", nameof(mediaUrl));
                }
                break;

            case OutboundMessageType.LinkPreview:
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    throw new ArgumentException("Text content with URL is required for link preview messages.", nameof(textContent));
                }
                break;

            case OutboundMessageType.Template:
                if (string.IsNullOrWhiteSpace(templateCode))
                {
                    throw new ArgumentException("Template code is required for template messages.", nameof(templateCode));
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(messageType), $"Unsupported message type: {messageType}");
        }

        return new OutboundMessage
        {
            TenantId = tenantId.Trim(),
            ConnectionId = connectionId.Trim(),
            Channel = channel,
            RecipientChannelId = trimmedRecipient,
            IdempotencyKey = trimmedIdempotencyKey,
            MessageType = messageType,
            TextContent = OptionalBounded(textContent, TextContentMaxLength),
            MediaUrl = OptionalBounded(mediaUrl, MediaUrlMaxLength),
            MediaContentType = OptionalBounded(mediaContentType, MediaContentTypeMaxLength),
            Caption = OptionalBounded(caption, CaptionMaxLength),
            TemplateCode = OptionalBounded(templateCode, TemplateCodeMaxLength),
            TemplateParametersJson = templateParametersJson,
            MetadataJson = metadataJson,
            ConversationId = trimmedConversationId,
            Status = OutboundMessageStatus.Queued,
            QueuedAt = queuedAt ?? DateTimeOffset.UtcNow,
            AttemptCount = 0,
            MaxAttempts = maxAttempts
        };
    }

    public void MarkSending()
    {
        if (Status != OutboundMessageStatus.Queued && Status != OutboundMessageStatus.Failed)
        {
            throw new InvalidOperationException($"Cannot transition to Sending from status '{Status}'.");
        }

        AttemptCount++;
        Status = OutboundMessageStatus.Sending;
    }

    public void RecordDeliverySuccess(string providerMessageId, DateTimeOffset sentAt)
    {
        if (Status != OutboundMessageStatus.Sending)
        {
            throw new InvalidOperationException($"Cannot record delivery success for message in status '{Status}'. Expected '{OutboundMessageStatus.Sending}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);

        Status = OutboundMessageStatus.Sent;
        ProviderMessageId = RequireBounded(providerMessageId, nameof(providerMessageId), ProviderMessageIdMaxLength);
        SentAt = sentAt;
        NextRetryAt = null;
        LastErrorMessage = null;
        FailureClassification = null;
    }

    public void RecordDeliveryFailure(
        string error,
        WebhookFailureClassification classification,
        DateTimeOffset now,
        WebhookRetryPolicy? policy = null)
    {
        if (Status != OutboundMessageStatus.Sending)
        {
            throw new InvalidOperationException($"Cannot record delivery failure for message in status '{Status}'. Expected '{OutboundMessageStatus.Sending}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        LastErrorMessage = Truncate(error, LastErrorMessageMaxLength);
        FailedAt = now;

        if (classification == WebhookFailureClassification.Permanent || AttemptCount >= MaxAttempts)
        {
            Status = OutboundMessageStatus.DeadLetter;
            DeadLetteredAt = now;
            NextRetryAt = null;
            FailureClassification = classification == WebhookFailureClassification.Permanent
                ? WebhookFailureClassification.Permanent
                : WebhookFailureClassification.Exhausted;
        }
        else
        {
            Status = OutboundMessageStatus.Failed;
            FailureClassification = classification;
            var effectivePolicy = policy ?? WebhookRetryPolicy.Default;
            NextRetryAt = now.Add(effectivePolicy.GetBackoffForAttempt(AttemptCount));
        }
    }

    public void UpdateProviderStatus(MessageDeliveryStatus receiptStatus, DateTimeOffset receiptTimestamp)
    {
        switch (receiptStatus)
        {
            case MessageDeliveryStatus.Delivered:
                if (Status == OutboundMessageStatus.Sent)
                {
                    Status = OutboundMessageStatus.Delivered;
                    DeliveredAt = receiptTimestamp;
                }
                break;

            case MessageDeliveryStatus.Read:
                if (Status == OutboundMessageStatus.Sent || Status == OutboundMessageStatus.Delivered)
                {
                    Status = OutboundMessageStatus.Read;
                    ReadAt = receiptTimestamp;
                    DeliveredAt ??= receiptTimestamp;
                }
                break;

            case MessageDeliveryStatus.Failed:
                if (Status == OutboundMessageStatus.Sent || Status == OutboundMessageStatus.Delivered)
                {
                    Status = OutboundMessageStatus.Failed;
                    FailedAt = receiptTimestamp;
                    FailureClassification = WebhookFailureClassification.Permanent;
                    LastErrorMessage = "Provider reported delivery failure receipt.";
                }
                break;
        }
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status != OutboundMessageStatus.Queued && Status != OutboundMessageStatus.Failed)
        {
            throw new InvalidOperationException($"Cannot cancel message in status '{Status}'. Only Queued or Failed messages may be cancelled.");
        }

        Status = OutboundMessageStatus.Cancelled;
        CancelledAt = now;
        NextRetryAt = null;
    }

    public void Replay(DateTimeOffset now, int? newMaxAttempts = null)
    {
        if (Status != OutboundMessageStatus.DeadLetter && Status != OutboundMessageStatus.Failed)
        {
            throw new InvalidOperationException($"Cannot replay message in status '{Status}'. Only DeadLetter or Failed messages may be replayed.");
        }

        Status = OutboundMessageStatus.Queued;
        AttemptCount = 0;
        NextRetryAt = null;
        DeadLetteredAt = null;
        FailedAt = null;
        FailureClassification = null;
        LastErrorMessage = null;
        QueuedAt = now;

        if (newMaxAttempts.HasValue)
        {
            if (newMaxAttempts.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(newMaxAttempts), "MaxAttempts must be at least 1.");
            }

            MaxAttempts = newMaxAttempts.Value;
        }
    }

    private static string RequireBounded(string value, string paramName, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", paramName);
        }

        return trimmed;
    }

    private static string? OptionalBounded(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

