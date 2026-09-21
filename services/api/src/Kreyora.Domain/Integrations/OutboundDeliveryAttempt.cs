using Kreyora.Domain.Common;

namespace Kreyora.Domain.Integrations;

public sealed class OutboundDeliveryAttempt : BaseEntity, ITenantOwned
{
    public const int ProviderMessageIdMaxLength = 256;
    public const int ProviderErrorCodeMaxLength = 512;
    public const int ProviderErrorMessageMaxLength = 2048;

    public string TenantId { get; private set; } = string.Empty;
    public string OutboundMessageId { get; private set; } = string.Empty;
    public int AttemptNumber { get; private set; }
    public ChannelType Channel { get; private set; }
    public string ConnectionId { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool Succeeded { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    private OutboundDeliveryAttempt() { }

    public static OutboundDeliveryAttempt Create(
        string tenantId,
        string outboundMessageId,
        int attemptNumber,
        ChannelType channel,
        string connectionId,
        DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outboundMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be >= 1.");
        }

        return new OutboundDeliveryAttempt
        {
            TenantId = tenantId.Trim(),
            OutboundMessageId = outboundMessageId.Trim(),
            AttemptNumber = attemptNumber,
            Channel = channel,
            ConnectionId = connectionId.Trim(),
            StartedAt = startedAt,
            Succeeded = false
        };
    }

    public void CompleteSuccess(string providerMessageId, DateTimeOffset completedAt)
    {
        if (completedAt < StartedAt)
        {
            throw new ArgumentException("CompletedAt cannot be before StartedAt.", nameof(completedAt));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);

        Succeeded = true;
        CompletedAt = completedAt;
        ProviderMessageId = RequireBounded(providerMessageId, nameof(providerMessageId), ProviderMessageIdMaxLength);
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
    }

    public void CompleteFailure(string? errorCode, string errorMessage, DateTimeOffset completedAt)
    {
        if (completedAt < StartedAt)
        {
            throw new ArgumentException("CompletedAt cannot be before StartedAt.", nameof(completedAt));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        Succeeded = false;
        CompletedAt = completedAt;
        ProviderErrorCode = OptionalBounded(errorCode, ProviderErrorCodeMaxLength);
        ProviderErrorMessage = Truncate(errorMessage, ProviderErrorMessageMaxLength);
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

