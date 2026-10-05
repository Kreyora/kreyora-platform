using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Integrations.Instagram;

/// <summary>
/// Live Instagram Graph API client: connection lifecycle validation (M08-S02) and text sends (M08-S05).
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

    public async Task<InstagramSendResult> SendTextAsync(
        string pageAccessToken,
        string recipientId,
        string text,
        string? messagingTag = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.TokenExpired, null, "A Page access token is required.");
        }

        if (string.IsNullOrWhiteSpace(recipientId) || string.IsNullOrWhiteSpace(text))
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.Rejected, "invalid_request", "A recipient and message text are required.");
        }

        // Meta Send API (Messenger Platform, Instagram): POST /me/messages with the Page token resolves to the Page.
        object payload = string.IsNullOrWhiteSpace(messagingTag)
            ? new { recipient = new { id = recipientId }, messaging_type = "RESPONSE", message = new { text } }
            : new { recipient = new { id = recipientId }, messaging_type = "MESSAGE_TAG", tag = messagingTag, message = new { text } };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{options.BaseAddress.TrimEnd('/')}/{options.ApiVersion.Trim('/')}/me/messages")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pageAccessToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request may already have been processed; retrying could double-message the customer.
            return InstagramSendResult.Failed(InstagramSendOutcome.Unconfirmed, "timeout",
                "The provider did not answer in time; the message may or may not have been delivered.");
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.ConnectionError
                                                  or HttpRequestError.NameResolutionError
                                                  or HttpRequestError.SecureConnectionError)
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.Transient, "transport_not_sent",
                "The provider could not be reached. Retry later.");
        }
        catch (HttpRequestException)
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.Unconfirmed, "transport",
                "The connection failed after sending; the message may or may not have been delivered.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var messageId = TryReadString(body, "message_id");
                return string.IsNullOrWhiteSpace(messageId)
                    ? InstagramSendResult.Failed(InstagramSendOutcome.Unconfirmed, "invalid_response",
                        "The provider accepted the request but returned no message ID.")
                    : InstagramSendResult.Sent(messageId);
            }

            return MapSendError(response.StatusCode, body);
        }
    }

    private static InstagramSendResult MapSendError(HttpStatusCode statusCode, string body)
    {
        var (code, subcode, isTransient) = TryReadError(body);
        var codeText = code is null
            ? ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : subcode is null
                ? code.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"{code.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{subcode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        if (code == 190)
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.TokenExpired, codeText,
                "The Page access token is expired or invalid. Reauthorize the connection.");
        }

        // Throttling codes per Meta's rate-limiting reference; 80002 is the Instagram business-use-case limit.
        if (code is 4 or 17 or 32 or 613 or 80002 || statusCode == HttpStatusCode.TooManyRequests || isTransient)
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.Transient, codeText,
                "The provider throttled or temporarily failed the request. It will be retried.");
        }

        if (code is null && (int)statusCode >= 500)
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.Unconfirmed, codeText,
                "The provider returned a server error; the message may or may not have been delivered.");
        }

        // Outside the messaging window (Meta Send API error reference: 10/2018278 and 2534022). Reported with the
        // stable reason code the inbox already explains; the raw Meta code stays in the message for diagnostics.
        if ((code == 10 && subcode == 2018278) || code == 2534022 || subcode == 2534022)
        {
            return InstagramSendResult.Failed(InstagramSendOutcome.Rejected, ConversationDenialReasons.WindowClosed,
                $"The 24-hour standard messaging window has expired (Meta {codeText}).");
        }

        return InstagramSendResult.Failed(InstagramSendOutcome.Rejected, codeText, code switch
        {
            _ when code == 551 || subcode == 1545041 => "This person is not available to receive messages.",
            10 or 200 => "The connection lacks permission to send messages.",
            _ => "The provider rejected the message."
        });
    }

    private static (int? Code, int? Subcode, bool IsTransient) TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return (null, null, false);
            }

            int? code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var cv) ? cv : null;
            int? subcode = error.TryGetProperty("error_subcode", out var sc) && sc.TryGetInt32(out var sv) ? sv : null;
            var transient = error.TryGetProperty("is_transient", out var t) && t.ValueKind == JsonValueKind.True;
            return (code, subcode, transient);
        }
        catch (JsonException)
        {
            return (null, null, false);
        }
    }

    private static string? TryReadString(string body, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
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
            4 or 17 or 32 or 613 or 80002 => InstagramValidationResult.Failed(
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
