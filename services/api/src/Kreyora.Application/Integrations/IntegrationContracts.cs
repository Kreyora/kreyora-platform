using System.Text;
using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public sealed record WebhookValidationRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> QueryParameters,
    byte[] RawBody,
    string? Secret)
{
    public static WebhookValidationRequest Create(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        string? secret = null,
        string method = "POST",
        string path = "/webhook",
        IReadOnlyDictionary<string, string>? queryParameters = null) =>
        new(
            method,
            path,
            headers,
            queryParameters ?? new Dictionary<string, string>(),
            Encoding.UTF8.GetBytes(rawBody),
            secret);
}

public sealed record WebhookValidationResult(
    bool IsValid,
    string? ChallengeResponse = null,
    string? ErrorReason = null,
    string? ProviderEventId = null,
    string? ExternalAccountId = null)
{
    public static WebhookValidationResult Valid(
        string? challengeResponse = null,
        string? providerEventId = null,
        string? externalAccountId = null) =>
        new(true, challengeResponse, null, providerEventId, externalAccountId);

    public static WebhookValidationResult Invalid(string errorReason) =>
        new(false, null, errorReason);

    public static WebhookValidationResult Success(
        string? challengeResponse = null,
        string? providerEventId = null,
        string? externalAccountId = null) =>
        Valid(challengeResponse, providerEventId, externalAccountId);

    public static WebhookValidationResult Failed(string errorReason) =>
        Invalid(errorReason);

    public string? FailureReason => ErrorReason;
}

public sealed record RawWebhookPayload(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    string TenantId,
    string ConnectionId,
    string? PayloadId = null,
    ChannelType? Channel = null,
    DateTimeOffset? ReceivedAt = null)
{
    public string Body => RawBody;
}

public sealed record ChannelConnectionSnapshot(
    string ConnectionId,
    string TenantId,
    string? StoreId,
    ChannelType Channel,
    string ExternalAccountId,
    EncryptedSecret? EncryptedCredentials,
    ChannelCapabilities Capabilities,
    ChannelConnectionStatus Status)
{
    public static ChannelConnectionSnapshot Create(
        string connectionId,
        string tenantId,
        string? storeId,
        ChannelType channel,
        ChannelConnectionStatus status = ChannelConnectionStatus.Active,
        string externalAccountId = "default_account",
        EncryptedSecret? encryptedCredentials = null,
        ChannelCapabilities? capabilities = null) =>
        new(
            connectionId,
            tenantId,
            storeId,
            channel,
            externalAccountId,
            encryptedCredentials,
            capabilities ?? ChannelCapabilities.ForChannel(channel),
            status);
}

public sealed record OutboundMessageRequest(
    string MessageId,
    string ConversationId,
    string RecipientChannelId,
    string? Text,
    string? MediaUrl = null,
    string? MediaContentType = null,
    string? Caption = null,
    string? TemplateCode = null,
    IReadOnlyDictionary<string, string>? TemplateParameters = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null)
{
    public static OutboundMessageRequest TextMessage(
        string recipientChannelId,
        string text,
        string? messageId = null,
        string? conversationId = null) =>
        new(
            MessageId: messageId ?? Guid.NewGuid().ToString("N"),
            ConversationId: conversationId ?? Guid.NewGuid().ToString("N"),
            RecipientChannelId: recipientChannelId,
            Text: text);
}

public sealed record OutboundDeliveryResult(
    bool Succeeded,
    string? ProviderMessageId,
    string? ProviderErrorCode,
    string? ProviderErrorMessage,
    DateTimeOffset? SentAt)
{
    public static OutboundDeliveryResult Success(string providerMessageId, DateTimeOffset sentAt) =>
        new(true, providerMessageId, null, null, sentAt);

    public static OutboundDeliveryResult Failure(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage, null);

    public static OutboundDeliveryResult Delivered(string providerMessageId, DateTimeOffset deliveredAt) =>
        Success(providerMessageId, deliveredAt);

    public bool IsSuccess => Succeeded;
    public DateTimeOffset? DeliveredAt => SentAt;
}

public sealed record ConnectionHealthResult(
    bool IsHealthy,
    ChannelConnectionStatus Status,
    string? DiagnosticMessage,
    DateTimeOffset CheckedAt)
{
    public static ConnectionHealthResult Healthy(string? diagnosticMessage = null) =>
        new(true, ChannelConnectionStatus.Active, diagnosticMessage, DateTimeOffset.UtcNow);

    public static ConnectionHealthResult Degraded(string diagnosticMessage) =>
        new(false, ChannelConnectionStatus.Degraded, diagnosticMessage, DateTimeOffset.UtcNow);

    public static ConnectionHealthResult Failed(ChannelConnectionStatus status, string diagnosticMessage) =>
        new(false, status, diagnosticMessage, DateTimeOffset.UtcNow);

    public ChannelConnectionStatus NewStatus => Status;
}
