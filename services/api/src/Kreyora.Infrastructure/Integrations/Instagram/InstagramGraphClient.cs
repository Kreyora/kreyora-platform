using System.Net;
using System.Text.Json;
using Kreyora.Application.Integrations.Instagram;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Integrations.Instagram;

/// <summary>
/// Live Instagram Graph API client used only for connection lifecycle validation (M08-S02).
/// Sends the caller's Page access token as a Bearer header (never in URLs) and never logs it.
/// </summary>
public sealed class InstagramGraphClient : IInstagramGraphClient
{
    private readonly HttpClient httpClient;
    private readonly InstagramGraphOptions options;

    public InstagramGraphClient(HttpClient httpClient, IOptions<InstagramGraphOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<InstagramValidationResult> ValidatePageLinkAsync(
        string pageAccessToken,
        string pageId,
        string instagramAccountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
        {
            return InstagramValidationResult.Failed(
                InstagramValidationKind.PermissionDenied, null, "A Page access token is required.");
        }

        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(instagramAccountId))
        {
            return InstagramValidationResult.Failed(
                InstagramValidationKind.IdentityMismatch, null, "Page ID and Instagram account ID are required.");
        }

        var linkedAccountId = await GetLinkedInstagramAccountIdAsync(pageAccessToken, pageId, cancellationToken);
        if (!linkedAccountId.IsValid)
        {
            return linkedAccountId.Result;
        }

        if (!string.Equals(linkedAccountId.AccountId, instagramAccountId, StringComparison.Ordinal))
        {
            return InstagramValidationResult.Failed(
                InstagramValidationKind.IdentityMismatch, null,
                "The Page is not linked to the expected Instagram business account.");
        }

        return InstagramValidationResult.Valid(string.Empty);
    }

    public async Task<InstagramValidationResult> ValidateAccountAsync(
        string pageAccessToken,
        string instagramAccountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
        {
            return InstagramValidationResult.Failed(
                InstagramValidationKind.PermissionDenied, null, "A Page access token is required.");
        }

        if (string.IsNullOrWhiteSpace(instagramAccountId))
        {
            return InstagramValidationResult.Failed(
                InstagramValidationKind.IdentityMismatch, null, "Instagram account ID is required.");
        }

        return await GetAccountUsernameAsync(pageAccessToken, instagramAccountId, cancellationToken);
    }

    private async Task<(bool IsValid, string? AccountId, InstagramValidationResult Result)> GetLinkedInstagramAccountIdAsync(
        string pageAccessToken,
        string pageId,
        CancellationToken cancellationToken)
    {
        var outcome = await GetAsync(
            pageAccessToken,
            $"{pageId}?fields=id,instagram_business_account",
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            return (false, null, outcome.Result);
        }

        try
        {
            using var document = JsonDocument.Parse(outcome.Body);
            if (document.RootElement.TryGetProperty("instagram_business_account", out var linked)
                && linked.TryGetProperty("id", out var id)
                && id.GetString() is { Length: > 0 } accountId)
            {
                return (true, accountId, outcome.Result);
            }

            return (false, null, InstagramValidationResult.Failed(
                InstagramValidationKind.IdentityMismatch, null,
                "The Page has no linked Instagram business account."));
        }
        catch (JsonException)
        {
            return (false, null, InstagramValidationResult.Failed(
                InstagramValidationKind.ProviderError, "invalid_response",
                "The provider returned an unreadable response."));
        }
    }

    private async Task<InstagramValidationResult> GetAccountUsernameAsync(
        string pageAccessToken,
        string instagramAccountId,
        CancellationToken cancellationToken)
    {
        var outcome = await GetAsync(
            pageAccessToken,
            $"{instagramAccountId}?fields=id,username",
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            return outcome.Result;
        }

        try
        {
            using var document = JsonDocument.Parse(outcome.Body);
            var username = document.RootElement.TryGetProperty("username", out var name)
                ? name.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(username))
            {
                return InstagramValidationResult.Failed(
                    InstagramValidationKind.ProviderError, "invalid_response",
                    "The provider response did not include a username.");
            }

            return InstagramValidationResult.Valid(username);
        }
        catch (JsonException)
        {
            return InstagramValidationResult.Failed(
                InstagramValidationKind.ProviderError, "invalid_response",
                "The provider returned an unreadable response.");
        }
    }

    private async Task<(bool IsSuccess, string Body, InstagramValidationResult Result)> GetAsync(
        string pageAccessToken,
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{options.BaseAddress.TrimEnd('/')}/{options.ApiVersion.Trim('/')}/{relativePath}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pageAccessToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, string.Empty, InstagramValidationResult.Failed(
                InstagramValidationKind.Transient, "timeout",
                "The provider timed out. Retry later."));
        }
        catch (HttpRequestException)
        {
            return (false, string.Empty, InstagramValidationResult.Failed(
                InstagramValidationKind.Transient, "transport",
                "The provider could not be reached. Retry later."));
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (true, body, InstagramValidationResult.Valid(string.Empty));
            }

            return (false, string.Empty, MapError(response.StatusCode, body));
        }
    }

    private static InstagramValidationResult MapError(HttpStatusCode statusCode, string body)
    {
        var code = TryReadErrorCode(body);

        return code switch
        {
            190 => InstagramValidationResult.Failed(
                InstagramValidationKind.TokenExpired, "190",
                "The Page access token is expired or invalid. Reauthorize to continue."),
            10 => InstagramValidationResult.Failed(
                InstagramValidationKind.PermissionDenied, "10",
                "The token lacks the required permissions or does not match the Page."),
            4 or 17 or 32 or 613 => InstagramValidationResult.Failed(
                InstagramValidationKind.Throttled, code.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "The provider throttled the request. Retry after the limit window resets."),
            _ when (int)statusCode >= 500 => InstagramValidationResult.Failed(
                InstagramValidationKind.Transient, ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "The provider returned a server error. Retry later."),
            _ => InstagramValidationResult.Failed(
                InstagramValidationKind.ProviderError, code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "The provider rejected the request.")
        };
    }

    private static int? TryReadErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var code)
                && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
