using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public sealed record ChannelConnectionDto(
    string Id,
    string TenantId,
    string? StoreId,
    ChannelType Channel,
    string ExternalAccountId,
    string DisplayName,
    ChannelConnectionStatus Status,
    bool HasCredentials,
    string? KeyVersion,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    DateTimeOffset? LastRefreshedAt,
    DateTimeOffset? LastValidatedAt,
    DateTimeOffset? LastHealthCheckAt,
    string? HealthSummary,
    string? HealthDetails,
    ChannelCapabilities Capabilities,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record CreateChannelConnectionRequest(
    ChannelType Channel,
    string ExternalAccountId,
    string DisplayName,
    string? StoreId = null,
    string? PlainTextSecret = null,
    string? WebhookVerificationToken = null,
    DateTimeOffset? TokenExpiresAt = null,
    DateTimeOffset? RefreshTokenExpiresAt = null);

public sealed record UpdateChannelConnectionRequest(
    string? DisplayName = null,
    string? StoreId = null,
    string? PlainTextSecret = null,
    DateTimeOffset? TokenExpiresAt = null,
    DateTimeOffset? RefreshTokenExpiresAt = null);

