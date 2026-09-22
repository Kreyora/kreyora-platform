using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations.Instagram;

/// <summary>
/// Pure mapping from <see cref="InstagramValidationResult"/> to connection-health transitions.
/// Keeps provider-specific status policy unit-testable without a database.
/// </summary>
public static class InstagramHealthMapper
{
    public static (bool IsHealthy, ChannelConnectionStatus Status, string Summary, string? Details) ToHealth(
        InstagramValidationResult result,
        DateTimeOffset checkedAt)
    {
        _ = checkedAt;

        return result.Kind switch
        {
            InstagramValidationKind.Valid =>
                (true, ChannelConnectionStatus.Active, "Validated against Instagram Graph API",
                    string.IsNullOrWhiteSpace(result.InstagramUsername)
                        ? null
                        : $"Connected as @{result.InstagramUsername}."),

            InstagramValidationKind.TokenExpired =>
                (false, ChannelConnectionStatus.Expired, "Instagram token expired",
                    "Graph API reported an expired or invalid session (code 190). Reauthorize with a fresh Page access token."),

            InstagramValidationKind.PermissionDenied =>
                (false, ChannelConnectionStatus.Revoked, "Instagram authorization revoked or insufficient",
                    "Graph API denied access (code 10). Reauthorize with instagram_basic, instagram_manage_messages, and pages_manage_metadata."),

            InstagramValidationKind.IdentityMismatch =>
                (false, ChannelConnectionStatus.Degraded, "Instagram account mismatch",
                    "The Page is not linked to the expected Instagram business account."),

            InstagramValidationKind.Throttled =>
                (false, ChannelConnectionStatus.Degraded, "Instagram rate limited",
                    "Graph API throttled the request. Retry after the limit window resets."),

            _ =>
                (false, ChannelConnectionStatus.Degraded, "Instagram validation unavailable",
                    string.IsNullOrWhiteSpace(result.Message)
                        ? "The provider did not answer conclusively. Retry later."
                        : result.Message)
        };
    }
}
