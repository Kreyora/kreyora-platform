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
    DateTimeOffset? RefreshTokenExpiresAt = null)
{
    /// <summary>
    /// Instagram live-validation input. When set with <see cref="PlainTextSecret"/> on an
    /// Instagram connection, the token is validated against the Graph API before persisting.
    /// </summary>
    public InstagramConnectOptions? Instagram { get; init; }
}

public sealed record UpdateChannelConnectionRequest(
    string? DisplayName = null,
    string? StoreId = null,
    string? PlainTextSecret = null,
    DateTimeOffset? TokenExpiresAt = null,
    DateTimeOffset? RefreshTokenExpiresAt = null)
{
    /// <summary>
    /// Instagram reauthorization input. When set with <see cref="PlainTextSecret"/>, the new
    /// token is validated against the Graph API before replacing stored credentials.
    /// </summary>
    public InstagramConnectOptions? Instagram { get; init; }
}

/// <summary>Non-secret Instagram connection parameters (IDs only, never tokens).</summary>
public sealed record InstagramConnectOptions(
    string PageId,
    string InstagramAccountId);

