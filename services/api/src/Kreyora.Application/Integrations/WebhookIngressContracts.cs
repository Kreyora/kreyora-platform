using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public sealed record WebhookIngressCommand(
    ChannelType Channel,
    string? ConnectionId,
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> QueryParameters,
    byte[] RawBody,
    string? ContentType,
    string? CorrelationId,
    DateTimeOffset ReceivedAt);

public sealed record WebhookIngressResult(
    bool IsSuccess,
    int StatusCode,
    string? EventId = null,
    bool IsDuplicate = false,
    string? ErrorReason = null,
    string? ResponseBody = null)
{
    public static WebhookIngressResult Success(string eventId, bool isDuplicate = false, string? responseBody = null) =>
        new(true, isDuplicate ? 200 : 202, eventId, isDuplicate, null, responseBody ?? (isDuplicate ? "{\"status\":\"duplicate\"}" : "{\"status\":\"accepted\"}"));

    public static WebhookIngressResult InvalidSignature(string reason) =>
        new(false, 401, null, false, reason, "{\"error\":\"invalid_signature\"}");

    public static WebhookIngressResult PayloadTooLarge() =>
        new(false, 413, null, false, "Payload exceeds size limit", "{\"error\":\"payload_too_large\"}");

    public static WebhookIngressResult UnsupportedMediaType(string? contentType) =>
        new(false, 415, null, false, $"Unsupported media type: {contentType}", "{\"error\":\"unsupported_media_type\"}");

    public static WebhookIngressResult NotFound(string reason) =>
        new(false, 404, null, false, reason, "{\"error\":\"connection_not_found\"}");

    public static WebhookIngressResult Forbidden(string reason) =>
        new(false, 403, null, false, reason, "{\"error\":\"connection_inactive\"}");

    public static WebhookIngressResult ReplayWindowExpired(string reason) =>
        new(false, 400, null, false, reason, "{\"error\":\"replay_window_expired\"}");

    public static WebhookIngressResult InternalError(string message) =>
        new(false, 500, null, false, message, "{\"error\":\"internal_server_error\"}");
}

public sealed record WebhookChallengeCommand(
    ChannelType Channel,
    string? ConnectionId,
    IReadOnlyDictionary<string, string> QueryParameters,
    IReadOnlyDictionary<string, string> Headers);

public sealed record WebhookChallengeResult(
    bool IsValid,
    int StatusCode,
    string? ChallengeResponse = null,
    string? ErrorReason = null)
{
    public static WebhookChallengeResult Valid(string challengeResponse) =>
        new(true, 200, challengeResponse, null);

    public static WebhookChallengeResult Invalid(string errorReason) =>
        new(false, 403, null, errorReason);

    public static WebhookChallengeResult NotFound(string errorReason) =>
        new(false, 404, null, errorReason);
}

