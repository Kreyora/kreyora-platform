namespace Kreyora.Application.Integrations.Instagram;

/// <summary>Outcome classification for Instagram Graph API connection validation.</summary>
public enum InstagramValidationKind
{
    Unknown = 0,
    Valid = 1,
    TokenExpired = 2,
    PermissionDenied = 3,
    IdentityMismatch = 4,
    Throttled = 5,
    Transient = 6,
    ProviderError = 7
}

/// <summary>Result of validating a Page access token against the Instagram Graph API.</summary>
public sealed record InstagramValidationResult(
    InstagramValidationKind Kind,
    string? InstagramUsername,
    string? ProviderErrorCode,
    string? Message)
{
    public bool IsValid => Kind == InstagramValidationKind.Valid;

    public static InstagramValidationResult Valid(string instagramUsername) =>
        new(InstagramValidationKind.Valid, instagramUsername, null, null);

    public static InstagramValidationResult Failed(
        InstagramValidationKind kind,
        string? providerErrorCode,
        string message) =>
        new(kind, null, providerErrorCode, message);
}

/// <summary>
/// Live Instagram Graph API validation for connection lifecycle (M08-S02).
/// Implemented by infrastructure over HTTP; tests substitute a stub handler.
/// Never receives, returns, or logs secret values beyond the token passed in by the caller.
/// </summary>
public interface IInstagramGraphClient
{
    /// <summary>
    /// Verifies the Page is linked to the expected Instagram business account.
    /// Used at connect/reauthorize time; the Page linkage is owner-provisioned and stable.
    /// </summary>
    Task<InstagramValidationResult> ValidatePageLinkAsync(
        string pageAccessToken,
        string pageId,
        string instagramAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the token can read the Instagram business account profile.
    /// Used at connect/reauthorize time and for recurring health checks.
    /// </summary>
    Task<InstagramValidationResult> ValidateAccountAsync(
        string pageAccessToken,
        string instagramAccountId,
        CancellationToken cancellationToken = default);
}
