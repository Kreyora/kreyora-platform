using Kreyora.Domain.Common;

namespace Kreyora.Domain.Notifications;

public sealed class NotificationDeliveryAttempt : BaseEntity, ITenantOwned
{
    public const int ProviderNameMaxLength = 128;
    public const int ProviderReferenceMaxLength = 512;
    public const int RedactedErrorMaxLength = 2000;

    private NotificationDeliveryAttempt() { }

    public string TenantId { get; private set; } = string.Empty;
    public string NotificationRequestId { get; private set; } = string.Empty;
    public int AttemptNumber { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool Succeeded { get; private set; }
    public string? RedactedError { get; private set; }
    public string? ProviderReference { get; private set; }

    public static NotificationDeliveryAttempt Create(
        string tenantId,
        string notificationRequestId,
        int attemptNumber,
        string providerName,
        DateTimeOffset startedAt)
    {
        if (attemptNumber < 1) throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be >= 1.");
        return new NotificationDeliveryAttempt
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            NotificationRequestId = Require(notificationRequestId, nameof(notificationRequestId), 26),
            AttemptNumber = attemptNumber,
            ProviderName = Require(providerName, nameof(providerName), ProviderNameMaxLength),
            StartedAt = startedAt,
            Succeeded = false
        };
    }

    public void CompleteSuccess(string? providerReference, DateTimeOffset completedAt)
    {
        if (completedAt < StartedAt) throw new ArgumentException("CompletedAt cannot be before StartedAt.", nameof(completedAt));
        Succeeded = true;
        CompletedAt = completedAt;
        ProviderReference = Optional(providerReference, ProviderReferenceMaxLength);
        RedactedError = null;
    }

    public void CompleteFailure(string redactedError, DateTimeOffset completedAt)
    {
        if (completedAt < StartedAt) throw new ArgumentException("CompletedAt cannot be before StartedAt.", nameof(completedAt));
        Succeeded = false;
        CompletedAt = completedAt;
        RedactedError = Require(redactedError, nameof(redactedError), RedactedErrorMaxLength);
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

