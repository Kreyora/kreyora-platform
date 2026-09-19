using Kreyora.Domain.Common;
using Kreyora.Domain.Tenancy;

namespace Kreyora.Domain.Integrations;

public sealed class WebhookEvent : BaseEntity, ITenantOwned
{
    public const int ProviderEventIdMaxLength = 128;
    public const int EventTypeMaxLength = 64;
    public const int CorrelationIdMaxLength = 64;
    public const int ErrorMessageMaxLength = 1024;
    public const string PurgedMarker = "[PURGED]";

    public string TenantId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public ChannelType Channel { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string? EventType { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public WebhookProcessingStatus ProcessingStatus { get; private set; } = WebhookProcessingStatus.Received;
    public string CorrelationId { get; private set; } = string.Empty;
    public string Headers { get; private set; } = string.Empty;
    public string RawPayload { get; private set; } = string.Empty;
    public bool IsPurged { get; private set; }
    public DateTimeOffset? PurgedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = WebhookRetryPolicy.DefaultMaxAttempts;
    public DateTimeOffset? NextRetryAt { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }
    public DateTimeOffset? LastAttemptedAt { get; private set; }
    public WebhookFailureClassification? FailureClassification { get; private set; }

    private WebhookEvent() { }

    public static WebhookEvent Create(
        string tenantId,
        string connectionId,
        ChannelType channel,
        string providerEventId,
        string? eventType,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string correlationId,
        string headers,
        string rawPayload,
        int maxAttempts = WebhookRetryPolicy.DefaultMaxAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(headers);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPayload);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be at least 1.");
        }

        if (providerEventId.Length > ProviderEventIdMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(providerEventId), $"ProviderEventId cannot exceed {ProviderEventIdMaxLength} characters.");
        }

        if (eventType != null && eventType.Length > EventTypeMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"EventType cannot exceed {EventTypeMaxLength} characters.");
        }

        if (correlationId.Length > CorrelationIdMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(correlationId), $"CorrelationId cannot exceed {CorrelationIdMaxLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        return new WebhookEvent
        {
            TenantId = tenantId,
            ConnectionId = connectionId,
            Channel = channel,
            ProviderEventId = providerEventId,
            EventType = eventType,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            ProcessingStatus = WebhookProcessingStatus.Received,
            CorrelationId = correlationId,
            Headers = headers,
            RawPayload = rawPayload,
            IsPurged = false,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            CreatedAt = now,
            ModifiedAt = now
        };
    }

    public void MarkProcessing()
    {
        ProcessingStatus = WebhookProcessingStatus.Processing;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessingStatus = WebhookProcessingStatus.Processed;
        ProcessedAt = processedAt;
        ErrorMessage = null;
        NextRetryAt = null;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void RecordSuccess(DateTimeOffset processedAt) => MarkProcessed(processedAt);

    public void MarkFailed(string errorMessage, bool deadLetter = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        ProcessingStatus = deadLetter ? WebhookProcessingStatus.DeadLetter : WebhookProcessingStatus.Failed;
        ErrorMessage = errorMessage.Length > ErrorMessageMaxLength
            ? errorMessage[..ErrorMessageMaxLength]
            : errorMessage;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(
        string errorMessage,
        WebhookFailureClassification classification,
        DateTimeOffset now,
        WebhookRetryPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        ArgumentNullException.ThrowIfNull(policy);

        LastAttemptedAt = now;
        AttemptCount++;
        ErrorMessage = errorMessage.Length > ErrorMessageMaxLength
            ? errorMessage[..ErrorMessageMaxLength]
            : errorMessage;

        if (classification == WebhookFailureClassification.Permanent || AttemptCount >= MaxAttempts)
        {
            ProcessingStatus = WebhookProcessingStatus.DeadLetter;
            FailureClassification = classification == WebhookFailureClassification.Permanent
                ? WebhookFailureClassification.Permanent
                : WebhookFailureClassification.Exhausted;
            DeadLetteredAt = now;
            NextRetryAt = null;
        }
        else
        {
            ProcessingStatus = WebhookProcessingStatus.Failed;
            FailureClassification = WebhookFailureClassification.Transient;
            NextRetryAt = now.Add(policy.GetBackoffForAttempt(AttemptCount));
        }

        ModifiedAt = now;
    }

    public void Replay(DateTimeOffset now)
    {
        if (ProcessingStatus != WebhookProcessingStatus.DeadLetter && ProcessingStatus != WebhookProcessingStatus.Failed)
        {
            throw new InvalidOperationException($"Cannot replay webhook event with status '{ProcessingStatus}'. Only DeadLetter or Failed events can be replayed.");
        }

        ProcessingStatus = WebhookProcessingStatus.Received;
        AttemptCount = 0;
        NextRetryAt = null;
        DeadLetteredAt = null;
        ProcessedAt = null;
        ErrorMessage = null;
        FailureClassification = null;
        ModifiedAt = now;
    }

    public void QuarantinePoison(string reason, DateTimeOffset now)
    {
        RecordFailure(reason, WebhookFailureClassification.Permanent, now, new WebhookRetryPolicy());
    }

    public void PurgePayload(DateTimeOffset purgedAt)
    {
        RawPayload = PurgedMarker;
        IsPurged = true;
        PurgedAt = purgedAt;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}

