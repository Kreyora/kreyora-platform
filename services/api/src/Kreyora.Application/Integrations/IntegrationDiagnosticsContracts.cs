using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public sealed record IntegrationOverviewDto(
    int TotalConnections,
    int ActiveConnections,
    int DegradedConnections,
    int ExpiredConnections,
    int EventsProcessed24h,
    int EventsFailed24h,
    int InboundDeadLetterCount,
    int OutboundDeadLetterCount,
    DateTimeOffset ComputedAt);

public sealed record ConnectionDiagnosticsDto(
    string ConnectionId,
    ChannelType Channel,
    string DisplayName,
    ChannelConnectionStatus Status,
    bool IsHealthy,
    string? HealthSummary,
    string? LastErrorMessage,
    DateTimeOffset? LastHealthCheckAt,
    DateTimeOffset? TokenExpiresAt,
    string WebhookUrl,
    int EventsProcessed24h,
    int EventsFailed24h,
    int DeadLetterCount,
    DateTimeOffset? LastEventAt);

public sealed record WebhookEventDto(
    string Id,
    string ConnectionId,
    ChannelType Channel,
    string ProviderEventId,
    string? EventType,
    WebhookProcessingStatus Status,
    int AttemptCount,
    int MaxAttempts,
    WebhookFailureClassification? FailureClassification,
    string? ErrorMessage,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? DeadLetteredAt);

public sealed record WebhookEventDetailDto(
    string Id,
    string ConnectionId,
    ChannelType Channel,
    string ProviderEventId,
    string? EventType,
    WebhookProcessingStatus Status,
    int AttemptCount,
    int MaxAttempts,
    WebhookFailureClassification? FailureClassification,
    string? ErrorMessage,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? DeadLetteredAt,
    string RawPayload,
    bool IsPayloadRedacted,
    string CorrelationId);

public enum SimulatorScenarioType
{
    HealthyInbound = 1,
    DuplicateDelivery = 2,
    OutOfOrderDelivery = 3,
    RateLimit429 = 4,
    TransientFailure = 5,
    PermanentPoison = 6,
    TokenExpiry = 7,
    Reconnect = 8,
    DeliveryReceipt = 9
}

public sealed record SimulatorScenarioRequest(
    string ConnectionId,
    SimulatorScenarioType Scenario,
    int? LatencyMs = null,
    string? CustomPayload = null);

public sealed record SimulatorScenarioResult(
    SimulatorScenarioType Scenario,
    bool Succeeded,
    string Summary,
    string? GeneratedEventId,
    string? CorrelationId,
    DateTimeOffset ExecutedAt);

