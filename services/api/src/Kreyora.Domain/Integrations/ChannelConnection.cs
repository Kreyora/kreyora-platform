using Kreyora.Domain.Common;

namespace Kreyora.Domain.Integrations;

public sealed class ChannelConnection : BaseEntity, ITenantOwned
{
    public const int ExternalAccountIdMaxLength = 128;
    public const int DisplayNameMaxLength = 128;
    public const int HealthSummaryMaxLength = 256;
    public const int HealthDetailsMaxLength = 1024;
    public const int WebhookVerificationTokenMaxLength = 128;

    private ChannelConnection() { }

    public string TenantId { get; private set; } = string.Empty;
    public string? StoreId { get; private set; }
    public ChannelType Channel { get; private set; }
    public string ExternalAccountId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public ChannelConnectionStatus Status { get; private set; }
    public EncryptedSecret? EncryptedCredentials { get; private set; }
    public ChannelCapabilities Capabilities { get; private set; } = ChannelCapabilities.FullSimulator();
    public DateTimeOffset? TokenExpiresAt { get; private set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public DateTimeOffset? LastValidatedAt { get; private set; }
    public DateTimeOffset? LastHealthCheckAt { get; private set; }
    public string? HealthSummary { get; private set; }
    public string? HealthDetails { get; private set; }
    public string? WebhookVerificationToken { get; private set; }

    public static ChannelConnection Create(
        string tenantId,
        ChannelType channel,
        string externalAccountId,
        string displayName,
        string? storeId = null,
        EncryptedSecret? encryptedCredentials = null,
        ChannelCapabilities? capabilities = null,
        string? webhookVerificationToken = null,
        DateTimeOffset? tokenExpiresAt = null,
        DateTimeOffset? refreshTokenExpiresAt = null,
        ChannelConnectionStatus status = ChannelConnectionStatus.Active)
    {
        var connection = new ChannelConnection
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            StoreId = Optional(storeId, 26),
            Channel = channel,
            ExternalAccountId = Require(externalAccountId, nameof(externalAccountId), ExternalAccountIdMaxLength),
            DisplayName = Require(displayName, nameof(displayName), DisplayNameMaxLength),
            Status = status,
            EncryptedCredentials = encryptedCredentials,
            Capabilities = capabilities ?? ChannelCapabilities.ForChannel(channel),
            WebhookVerificationToken = Optional(webhookVerificationToken, WebhookVerificationTokenMaxLength),
            TokenExpiresAt = tokenExpiresAt,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
            LastValidatedAt = encryptedCredentials != null ? DateTimeOffset.UtcNow : null
        };

        return connection;
    }

    public void UpdateMetadata(string displayName, string? storeId)
    {
        DisplayName = Require(displayName, nameof(displayName), DisplayNameMaxLength);
        StoreId = Optional(storeId, 26);
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCredentials(
        EncryptedSecret encryptedCredentials,
        DateTimeOffset? tokenExpiresAt = null,
        DateTimeOffset? refreshTokenExpiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(encryptedCredentials);

        EncryptedCredentials = encryptedCredentials;
        TokenExpiresAt = tokenExpiresAt;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
        LastRefreshedAt = DateTimeOffset.UtcNow;
        LastValidatedAt = DateTimeOffset.UtcNow;
        ModifiedAt = DateTimeOffset.UtcNow;

        if (Status is ChannelConnectionStatus.Expired or ChannelConnectionStatus.Revoked or ChannelConnectionStatus.Degraded)
        {
            Status = ChannelConnectionStatus.Active;
        }
    }

    public void RotateSecret(Func<EncryptedSecret, string, EncryptedSecret> reEncryptFunc, string targetKeyVersion)
    {
        ArgumentNullException.ThrowIfNull(reEncryptFunc);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKeyVersion);

        if (EncryptedCredentials is null)
        {
            throw new InvalidOperationException("Cannot rotate secrets on a connection with no credentials.");
        }

        EncryptedCredentials = reEncryptFunc(EncryptedCredentials, targetKeyVersion);
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateHealth(
        bool isHealthy,
        ChannelConnectionStatus status,
        string? summary,
        string? details,
        DateTimeOffset checkedAt)
    {
        Status = status;
        HealthSummary = Optional(summary, HealthSummaryMaxLength);
        HealthDetails = Optional(details, HealthDetailsMaxLength);
        LastHealthCheckAt = checkedAt;
        if (isHealthy)
        {
            LastValidatedAt = checkedAt;
        }
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Disable(string? reason = null)
    {
        Status = ChannelConnectionStatus.Disabled;
        HealthSummary = Optional(reason, HealthSummaryMaxLength) ?? "Connection disabled by administrator";
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Enable()
    {
        if (Status == ChannelConnectionStatus.Revoked)
        {
            throw new InvalidOperationException("Cannot enable a revoked connection without updating credentials.");
        }

        if (TokenExpiresAt.HasValue && TokenExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            Status = ChannelConnectionStatus.Expired;
            throw new InvalidOperationException("Cannot enable an expired connection without refreshing credentials.");
        }

        Status = ChannelConnectionStatus.Active;
        HealthSummary = "Connection enabled";
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void Revoke(string? reason = null)
    {
        Status = ChannelConnectionStatus.Revoked;
        HealthSummary = Optional(reason, HealthSummaryMaxLength) ?? "Connection revoked";
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string paramName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", paramName) : value.Trim();
        return normalized.Length > maxLength ? throw new ArgumentOutOfRangeException(paramName, $"Value cannot exceed {maxLength} characters.") : normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length > maxLength ? throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maxLength} characters.") : normalized;
    }
}

