using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;

namespace Kreyora.Infrastructure.Integrations.Simulator;

public sealed class SimulatorChannelProvider : IChannelProvider
{
    public const string DefaultValidSignature = "sha256=valid_test_signature";
    public const string DefaultVerifyToken = "simulator_verify_token";
    public const int DefaultReplayWindowSeconds = 300;

    public ChannelType Channel => ChannelType.Simulator;
    public ChannelCapabilities Capabilities => ChannelCapabilities.FullSimulator();

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        WebhookValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Handle GET verification challenge (e.g. Meta hub.challenge pattern)
        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(HandleVerificationChallenge(request));
        }

        // 2. Replay window validation (if timestamp header provided)
        if (TryGetTimestamp(request, out var timestamp))
        {
            var delta = Math.Abs((DateTimeOffset.UtcNow - timestamp).TotalSeconds);
            if (delta > DefaultReplayWindowSeconds)
            {
                return Task.FromResult(WebhookValidationResult.Failed("Timestamp outside replay window"));
            }
        }

        // 3. Signature verification
        if (!request.Headers.TryGetValue("X-Hub-Signature-256", out var signature) &&
            !request.Headers.TryGetValue("X-Signature", out signature))
        {
            return Task.FromResult(WebhookValidationResult.Failed("Missing signature header"));
        }

        var isSignatureValid = false;
        if (signature == DefaultValidSignature)
        {
            isSignatureValid = true;
        }
        else if (!string.IsNullOrEmpty(request.Secret))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(request.Secret));
            var hash = hmac.ComputeHash(request.RawBody);
            var expectedSignature = "sha256=" + Convert.ToHexStringLower(hash);
            isSignatureValid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSignature));
        }

        if (!isSignatureValid)
        {
            return Task.FromResult(WebhookValidationResult.Failed("Invalid signature header"));
        }

        // 4. Extract ProviderEventId and ExternalAccountId
        var providerEventId = ExtractProviderEventId(request);
        var externalAccountId = ExtractExternalAccountId(request);

        return Task.FromResult(WebhookValidationResult.Success(
            challengeResponse: null,
            providerEventId: providerEventId,
            externalAccountId: externalAccountId));
    }

    public Task<IReadOnlyList<NormalizedInboundEnvelope>> NormalizeInboundAsync(
        RawWebhookPayload rawPayload,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var envelopes = new List<NormalizedInboundEnvelope>();

        using var doc = JsonDocument.Parse(rawPayload.RawBody);
        var root = doc.RootElement;

        // Simulated failure flags for reliability and failure testing
        if (root.TryGetProperty("throw_transient", out var transProp) && transProp.GetBoolean())
        {
            throw new HttpRequestException("Simulated transient network timeout.");
        }

        if ((root.TryGetProperty("throw_permanent", out var permProp) && permProp.GetBoolean()) ||
            (root.TryGetProperty("is_poison", out var poisonProp) && poisonProp.GetBoolean()))
        {
            throw new System.Text.Json.JsonException("Simulated poison payload with malformed syntax.");
        }

        var schemaVersion = NormalizedInboundEnvelope.CurrentSchemaVersion;
        if (root.TryGetProperty("schema_version", out var svProp))
        {
            schemaVersion = svProp.GetString() ?? schemaVersion;
        }

        if (root.TryGetProperty("messages", out var messagesArray) && messagesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in messagesArray.EnumerateArray())
            {
                envelopes.Add(ParseEnvelope(item, rawPayload, now, schemaVersion));
            }
        }
        else
        {
            envelopes.Add(ParseEnvelope(root, rawPayload, now, schemaVersion));
        }

        return Task.FromResult<IReadOnlyList<NormalizedInboundEnvelope>>(envelopes);
    }

    private static NormalizedInboundEnvelope ParseEnvelope(
        JsonElement element,
        RawWebhookPayload rawPayload,
        DateTimeOffset now,
        string schemaVersion)
    {
        var text = "Simulator message";
        var messageId = "msg_sim_" + Guid.NewGuid().ToString("N");
        var senderId = "user_sim_1";
        var type = "text";

        if (element.TryGetProperty("occurred_at", out var occProp) && occProp.TryGetDateTimeOffset(out var occ))
        {
            now = occ;
        }
        else if (element.TryGetProperty("timestamp", out var tsProp) && tsProp.TryGetDateTimeOffset(out var ts))
        {
            now = ts;
        }
        else if (rawPayload.ReceivedAt.HasValue)
        {
            now = rawPayload.ReceivedAt.Value;
        }

        if (element.TryGetProperty("text", out var textProp))
        {
            text = textProp.GetString() ?? text;
        }

        if (element.TryGetProperty("message_id", out var msgProp))
        {
            messageId = msgProp.GetString() ?? messageId;
        }

        if (element.TryGetProperty("sender_id", out var sndProp))
        {
            senderId = sndProp.GetString() ?? senderId;
        }

        if (element.TryGetProperty("type", out var typeProp))
        {
            type = typeProp.GetString() ?? type;
        }

        NormalizedInboundPayload payload = type.ToLowerInvariant() switch
        {
            "media" => new MediaMessageReceivedPayload(
                messageId,
                senderId,
                "Simulator User",
                element.TryGetProperty("media_url", out var mu) ? mu.GetString() ?? "https://example.com/image.jpg" : "https://example.com/image.jpg",
                element.TryGetProperty("content_type", out var ct) ? ct.GetString() ?? "image/jpeg" : "image/jpeg",
                1024,
                text,
                now),
            "status" => new MessageStatusUpdatedPayload(
                messageId,
                senderId,
                MessageDeliveryStatus.Delivered,
                null,
                null,
                now),
            "reaction" => new ReactionReceivedPayload(
                messageId,
                senderId,
                element.TryGetProperty("emoji", out var em) ? em.GetString() ?? "👍" : "👍",
                false,
                now),
            "profile" => new CustomerProfileUpdatedPayload(
                senderId,
                "Simulator User",
                "https://example.com/avatar.jpg",
                "+9779800000000"),
            _ => new TextMessageReceivedPayload(messageId, senderId, "Simulator User", text, now)
        };

        return NormalizedInboundEnvelope.Create(
            eventId: "evt_sim_" + Guid.NewGuid().ToString("N"),
            tenantId: rawPayload.TenantId,
            connectionId: rawPayload.ConnectionId,
            channel: ChannelType.Simulator,
            occurredAt: now,
            payload: payload,
            schemaVersion: schemaVersion);
    }

    public Task<OutboundDeliveryResult> SendMessageAsync(
        ChannelConnectionSnapshot connection,
        OutboundMessageRequest message,
        CancellationToken cancellationToken = default)
    {
        // 1. Simulated failure triggers via metadata or text content
        if (message.Metadata?.TryGetValue("throw_transient", out var trans) == true && trans.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            message.Text?.Contains("[SIMULATE_TRANSIENT]", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new HttpRequestException("Simulated transient network timeout during send.");
        }

        if (message.Metadata?.TryGetValue("throw_rate_limit", out var rateLimit) == true && rateLimit.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            message.Text?.Contains("[SIMULATE_RATE_LIMIT]", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new HttpRequestException("Provider returned 429 Too Many Requests.");
        }

        if (message.Metadata?.TryGetValue("throw_permanent", out var perm) == true && perm.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            message.Text?.Contains("[SIMULATE_PERMANENT]", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException("Permanent failure: recipient account does not exist or has blocked the sender.");
        }

        if (message.Metadata?.TryGetValue("fail_delivery", out var fail) == true && fail.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            message.Text?.Contains("[SIMULATE_FAILED_DELIVERY]", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Task.FromResult(OutboundDeliveryResult.Failure(
                "DELIVERY_REJECTED",
                "Provider rejected delivery to recipient."));
        }

        var providerMessageId = message.Metadata?.TryGetValue("provider_message_id", out var customId) == true
            ? customId
            : "out_sim_" + Guid.NewGuid().ToString("N");

        return Task.FromResult(OutboundDeliveryResult.Delivered(
            providerMessageId: providerMessageId,
            deliveredAt: DateTimeOffset.UtcNow));
    }

    public Task<ConnectionHealthResult> ValidateOrRefreshConnectionAsync(
        ChannelConnectionSnapshot connection,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ConnectionHealthResult.Healthy());
    }

    private static WebhookValidationResult HandleVerificationChallenge(WebhookValidationRequest request)
    {
        if (!request.QueryParameters.TryGetValue("hub.mode", out var mode) || mode != "subscribe")
        {
            return WebhookValidationResult.Failed("Missing or invalid hub.mode parameter");
        }

        if (!request.QueryParameters.TryGetValue("hub.verify_token", out var token))
        {
            return WebhookValidationResult.Failed("Missing hub.verify_token parameter");
        }

        var expectedToken = !string.IsNullOrEmpty(request.Secret) ? request.Secret : DefaultVerifyToken;
        if (token != expectedToken && token != DefaultVerifyToken)
        {
            return WebhookValidationResult.Failed("Verification token mismatch");
        }

        if (!request.QueryParameters.TryGetValue("hub.challenge", out var challenge))
        {
            return WebhookValidationResult.Failed("Missing hub.challenge parameter");
        }

        return WebhookValidationResult.Success(challengeResponse: challenge);
    }

    private static bool TryGetTimestamp(WebhookValidationRequest request, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (request.Headers.TryGetValue("X-Hub-Timestamp", out var tsStr) ||
            request.Headers.TryGetValue("X-Timestamp", out tsStr))
        {
            if (long.TryParse(tsStr, out var epochSeconds))
            {
                timestamp = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
                return true;
            }

            if (DateTimeOffset.TryParse(tsStr, out timestamp))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractProviderEventId(WebhookValidationRequest request)
    {
        if (request.Headers.TryGetValue("X-Provider-Event-Id", out var headerId) && !string.IsNullOrWhiteSpace(headerId))
        {
            return headerId.Trim();
        }

        if (request.Headers.TryGetValue("X-Event-Id", out var evId) && !string.IsNullOrWhiteSpace(evId))
        {
            return evId.Trim();
        }

        if (request.RawBody.Length > 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(request.RawBody);
                if (doc.RootElement.TryGetProperty("id", out var idProp))
                {
                    var idStr = idProp.GetString();
                    if (!string.IsNullOrWhiteSpace(idStr))
                    {
                        return idStr.Trim();
                    }
                }

                if (doc.RootElement.TryGetProperty("event_id", out var evProp))
                {
                    var idStr = evProp.GetString();
                    if (!string.IsNullOrWhiteSpace(idStr))
                    {
                        return idStr.Trim();
                    }
                }
            }
            catch
            {
                // ignore json parse failures here, fallback below
            }
        }

        return "evt_sim_" + Guid.NewGuid().ToString("N");
    }

    private static string? ExtractExternalAccountId(WebhookValidationRequest request)
    {
        if (request.Headers.TryGetValue("X-External-Account-Id", out var accId) && !string.IsNullOrWhiteSpace(accId))
        {
            return accId.Trim();
        }

        if (request.RawBody.Length > 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(request.RawBody);
                if (doc.RootElement.TryGetProperty("account_id", out var accProp))
                {
                    return accProp.GetString();
                }

                if (doc.RootElement.TryGetProperty("external_account_id", out var extProp))
                {
                    return extProp.GetString();
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}

