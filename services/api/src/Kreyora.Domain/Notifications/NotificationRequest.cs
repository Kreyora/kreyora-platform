using Kreyora.Domain.Common;

namespace Kreyora.Domain.Notifications;

public sealed class NotificationRequest : BaseEntity, ITenantOwned
{
    public const int SourceEventTypeMaxLength = 128;
    public const int TemplateCodeMaxLength = 128;
    public const int RecipientNameMaxLength = 200;
    public const int RecipientContactMaxLength = 512;
    public const int LastRedactedErrorMaxLength = 2000;
    public const int IdempotencyKeyMaxLength = 256;

    private NotificationRequest() { }

    public string TenantId { get; private set; } = string.Empty;
    public string SourceEventType { get; private set; } = string.Empty;
    public string SourceEventId { get; private set; } = string.Empty;
    public string TemplateCode { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string? RecipientName { get; private set; }
    public string RecipientContact { get; private set; } = string.Empty;
    public NotificationStatus Status { get; private set; }
    public int MaxAttempts { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextRetryAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }
    public string? LastRedactedError { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;

    public List<NotificationDeliveryAttempt> DeliveryAttempts { get; private set; } = [];

    public static NotificationRequest Create(
        string tenantId,
        string sourceEventType,
        string sourceEventId,
        string templateCode,
        int templateVersion,
        NotificationChannel channel,
        string recipientContact,
        string? recipientName = null,
        int maxAttempts = NotificationRetryPolicy.DefaultMaxAttempts,
        string? idempotencyKey = null)
    {
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (templateVersion < 1) throw new ArgumentOutOfRangeException(nameof(templateVersion), "Template version must be >= 1.");
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be >= 1.");

        var validTenantId = Require(tenantId, nameof(tenantId), 26);
        var validSourceEventType = Require(sourceEventType, nameof(sourceEventType), SourceEventTypeMaxLength);
        var validSourceEventId = Require(sourceEventId, nameof(sourceEventId), 26);
        var validTemplateCode = Require(templateCode, nameof(templateCode), TemplateCodeMaxLength);
        var validRecipientContact = Require(recipientContact, nameof(recipientContact), RecipientContactMaxLength);

        var key = idempotencyKey is not null
            ? Require(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength)
            : $"{validSourceEventId}:{validTemplateCode}:{(int)channel}";

        return new NotificationRequest
        {
            TenantId = validTenantId,
            SourceEventType = validSourceEventType,
            SourceEventId = validSourceEventId,
            TemplateCode = validTemplateCode,
            TemplateVersion = templateVersion,
            Channel = channel,
            RecipientName = Optional(recipientName, RecipientNameMaxLength),
            RecipientContact = validRecipientContact,
            Status = NotificationStatus.Pending,
            MaxAttempts = maxAttempts,
            AttemptCount = 0,
            IdempotencyKey = key
        };
    }

    public void MarkDelivering(DateTimeOffset now)
    {
        if (Status != NotificationStatus.Pending && Status != NotificationStatus.Failed)
        {
            throw new InvalidOperationException($"Cannot transition to Delivering from status '{Status}'. Only Pending or Failed notifications can be delivered.");
        }

        Status = NotificationStatus.Delivering;
        AttemptCount++;
        NextRetryAt = null;
    }

    public void RecordSuccess(string? providerReference, DateTimeOffset now)
    {
        if (Status != NotificationStatus.Delivering)
        {
            throw new InvalidOperationException($"Cannot record success when status is '{Status}'. Must be Delivering.");
        }

        Status = NotificationStatus.Delivered;
        DeliveredAt = now;
        LastRedactedError = null;
        NextRetryAt = null;
    }

    public void RecordFailure(string redactedError, DateTimeOffset now, NotificationRetryPolicy policy)
    {
        if (Status != NotificationStatus.Delivering)
        {
            throw new InvalidOperationException($"Cannot record failure when status is '{Status}'. Must be Delivering.");
        }

        LastRedactedError = Require(redactedError, nameof(redactedError), LastRedactedErrorMaxLength);

        if (AttemptCount >= MaxAttempts)
        {
            Status = NotificationStatus.DeadLettered;
            DeadLetteredAt = now;
            NextRetryAt = null;
        }
        else
        {
            Status = NotificationStatus.Failed;
            NextRetryAt = now.Add(policy.GetBackoffForAttempt(AttemptCount));
        }
    }

    public void Replay(DateTimeOffset now)
    {
        if (Status != NotificationStatus.DeadLettered && Status != NotificationStatus.Failed)
        {
            throw new InvalidOperationException($"Cannot replay notification with status '{Status}'. Only DeadLettered or Failed notifications can be replayed.");
        }

        Status = NotificationStatus.Pending;
        AttemptCount = 0;
        NextRetryAt = null;
        DeadLetteredAt = null;
        DeliveredAt = null;
        LastRedactedError = null;
    }

    private static string Require(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength) throw new ArgumentException($"Value cannot exceed {maxLength} characters.", paramName);
        return trimmed;
    }

    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

