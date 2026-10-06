using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Integrations.Instagram;

/// <summary>
/// Instagram Messaging channel adapter: app-level webhook verification challenge, HMAC-SHA256 request
/// validation, per-account delivery splitting, inbound normalization to v1 envelopes with event-level
/// deduplication keys (ADR-015), echo normalization and text sends through the Send API (M08-S05, ADR-017).
/// Connection health stays with the S02 graph client.
/// Evidence: docs/architecture/PROVIDER_READINESS_EVALUATION.md; Meta Webhooks docs (accessed 2026-10-05).
/// </summary>
public sealed partial class InstagramChannelProvider : IChannelProvider
{
    public const string SignatureHeader = "X-Hub-Signature-256";

    private const string ObjectType = "instagram";

    public ChannelType Channel => ChannelType.Instagram;

    public ChannelCapabilities Capabilities => ChannelCapabilities.InstagramGraphApi();

    /// <summary>Signatures use the app secret, not a per-connection secret.</summary>
    public bool UsesConnectionSecretForSignature => false;

    /// <summary>Meta: "respond to all Event Notifications with 200 OK".</summary>
    public int AcknowledgementStatusCode => 200;

    private readonly InstagramWebhookOptions webhookOptions;
    private readonly ILogger<InstagramChannelProvider> logger;
    private readonly IInstagramGraphClient? graphClient;
    private readonly ISecretEncryptionService? encryption;

    public InstagramChannelProvider(
        IOptions<InstagramWebhookOptions> webhookOptions,
        ILogger<InstagramChannelProvider>? logger = null,
        IInstagramGraphClient? graphClient = null,
        ISecretEncryptionService? encryption = null)
    {
        this.webhookOptions = webhookOptions.Value;
        this.logger = logger ?? NullLogger<InstagramChannelProvider>.Instance;
        this.graphClient = graphClient;
        this.encryption = encryption;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Instagram webhook item skipped for connection {ConnectionId}: {Reason}")]
    private static partial void LogItemSkipped(ILogger logger, string connectionId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Instagram webhook entry for another account skipped for connection {ConnectionId}")]
    private static partial void LogForeignEntrySkipped(ILogger logger, string connectionId);

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        WebhookValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(HandleVerificationChallenge(request));
        }

        if (!request.Headers.TryGetValue(SignatureHeader, out var signature)
            || string.IsNullOrWhiteSpace(signature))
        {
            return Task.FromResult(WebhookValidationResult.Failed("Missing signature header"));
        }

        if (string.IsNullOrEmpty(webhookOptions.AppSecret))
        {
            return Task.FromResult(WebhookValidationResult.Failed("Instagram webhook secret is not configured"));
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookOptions.AppSecret));
        var hash = hmac.ComputeHash(request.RawBody);
        var expectedSignature = "sha256=" + Convert.ToHexStringLower(hash);

        if (!FixedTimeEquals(signature, expectedSignature))
        {
            return Task.FromResult(WebhookValidationResult.Failed("Invalid signature header"));
        }

        return Task.FromResult(WebhookValidationResult.Success(
            providerEventId: ComputeBodyEventId(request.RawBody),
            externalAccountId: ExtractAccountId(request.RawBody)));
    }

    public Task<IReadOnlyList<NormalizedInboundEnvelope>> NormalizeInboundAsync(
        RawWebhookPayload rawPayload,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(rawPayload.RawBody);

        if (!IsInstagramObject(document.RootElement))
        {
            return Task.FromResult<IReadOnlyList<NormalizedInboundEnvelope>>(
                Array.Empty<NormalizedInboundEnvelope>());
        }

        var envelopes = new List<NormalizedInboundEnvelope>();
        if (!document.RootElement.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return Task.FromResult<IReadOnlyList<NormalizedInboundEnvelope>>(envelopes);
        }

        foreach (var entry in entries.EnumerateArray())
        {
            // Defense in depth (ADR-015): a connection only normalizes entries for its own account.
            if (!string.IsNullOrWhiteSpace(rawPayload.ExternalAccountId)
                && !string.Equals(ReadEntryId(entry), rawPayload.ExternalAccountId, StringComparison.Ordinal))
            {
                LogForeignEntrySkipped(logger, rawPayload.ConnectionId);
                continue;
            }

            var entryTime = ReadUnixMilliseconds(entry, "time");
            if (!entry.TryGetProperty("messaging", out var messaging)
                || messaging.ValueKind != JsonValueKind.Array)
            {
                LogItemSkipped(logger, rawPayload.ConnectionId, "entry without messaging array");
                continue;
            }

            foreach (var item in messaging.EnumerateArray())
            {
                NormalizeItem(rawPayload, item, entryTime, envelopes);
            }
        }

        return Task.FromResult<IReadOnlyList<NormalizedInboundEnvelope>>(envelopes);
    }

    public string? ResolveExternalAccountId(JsonDocument body)
    {
        if (!IsInstagramObject(body.RootElement)
            || !body.RootElement.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (ReadEntryId(entry) is { Length: > 0 } accountId)
            {
                return accountId;
            }
        }

        return null;
    }

    /// <summary>
    /// Groups <c>entry[]</c> by <c>entry.id</c> (the Instagram professional account), preserving order.
    /// Each slice keeps the original entry JSON text. Entries without an account ID are dropped.
    /// </summary>
    public IReadOnlyList<WebhookAccountSlice>? SplitByAccount(JsonDocument body)
    {
        if (!IsInstagramObject(body.RootElement)
            || !body.RootElement.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<WebhookAccountSlice>();
        }

        var order = new List<string>();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            if (ReadEntryId(entry) is not { Length: > 0 } accountId)
            {
                continue;
            }

            if (!groups.TryGetValue(accountId, out var raw))
            {
                raw = [];
                groups[accountId] = raw;
                order.Add(accountId);
            }

            raw.Add(entry.GetRawText());
        }

        return order
            .Select(accountId => new WebhookAccountSlice(
                accountId,
                "{\"object\":\"" + ObjectType + "\",\"entry\":[" + string.Join(",", groups[accountId]) + "]}"))
            .ToList();
    }

    /// <summary>
    /// Text-only send via the Send API. The Page token is decrypted here and only here (ADR-013); it travels
    /// as a Bearer header and is never logged. Outcomes map to ADR-017 retry semantics.
    /// </summary>
    public async Task<OutboundDeliveryResult> SendMessageAsync(
        ChannelConnectionSnapshot connection,
        OutboundMessageRequest message,
        CancellationToken cancellationToken = default)
    {
        if (graphClient is null || encryption is null)
        {
            return OutboundDeliveryResult.Failure("provider_unconfigured", "Instagram sending is not configured in this host.");
        }

        if (!string.IsNullOrWhiteSpace(message.MediaUrl) || string.IsNullOrWhiteSpace(message.Text))
        {
            return OutboundDeliveryResult.Failure(ConversationDenialReasons.CapabilityUnsupported,
                "Instagram sends support text messages only in this release.");
        }

        if (connection.EncryptedCredentials is null)
        {
            return OutboundDeliveryResult.Failure("credentials_missing", "The connection has no Page access token. Reauthorize it.");
        }

        var token = encryption.Decrypt(connection.EncryptedCredentials);
        var tag = message.Metadata?.GetValueOrDefault(ConversationGateResult.MessagingTagMetadataKey);
        var result = await graphClient.SendTextAsync(token, message.RecipientChannelId, message.Text, tag, cancellationToken);

        return result.Outcome switch
        {
            InstagramSendOutcome.Sent => OutboundDeliveryResult.Success(result.MessageId!, DateTimeOffset.UtcNow),
            InstagramSendOutcome.Transient => OutboundDeliveryResult.TransientFailure(result.ProviderErrorCode ?? "transient", result.Message ?? "Transient provider failure."),
            InstagramSendOutcome.TokenExpired => OutboundDeliveryResult.Failure("190", result.Message ?? "The Page access token is expired."),
            InstagramSendOutcome.Unconfirmed => OutboundDeliveryResult.Failure(ConversationDenialReasons.DeliveryUnconfirmed, result.Message ?? "Delivery could not be confirmed."),
            _ => OutboundDeliveryResult.Failure(result.ProviderErrorCode ?? "rejected", result.Message ?? "The provider rejected the message.")
        };
    }

    public Task<ConnectionHealthResult> ValidateOrRefreshConnectionAsync(
        ChannelConnectionSnapshot connection,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Instagram connection health is served by the S02 graph client in M08-S03.");

    /// <summary>
    /// Meta's verify token is configured once per app in the App Dashboard, so the challenge compares
    /// against the app-level option and ignores any connection-scoped secret.
    /// </summary>
    private WebhookValidationResult HandleVerificationChallenge(WebhookValidationRequest request)
    {
        if (!request.QueryParameters.TryGetValue("hub.mode", out var mode) || mode != "subscribe")
        {
            return WebhookValidationResult.Failed("Missing or invalid hub.mode parameter");
        }

        if (!request.QueryParameters.TryGetValue("hub.verify_token", out var token)
            || string.IsNullOrWhiteSpace(token))
        {
            return WebhookValidationResult.Failed("Missing hub.verify_token parameter");
        }

        if (string.IsNullOrEmpty(webhookOptions.VerifyToken) || !FixedTimeEquals(token, webhookOptions.VerifyToken))
        {
            return WebhookValidationResult.Failed("Verification token mismatch");
        }

        if (!request.QueryParameters.TryGetValue("hub.challenge", out var challenge)
            || string.IsNullOrWhiteSpace(challenge))
        {
            return WebhookValidationResult.Failed("Missing hub.challenge parameter");
        }

        return WebhookValidationResult.Success(challengeResponse: challenge);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static bool IsInstagramObject(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("object", out var kind)
        && kind.ValueKind == JsonValueKind.String
        && kind.GetString() == ObjectType;

    private static string? ReadEntryId(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

    private static string ComputeBodyEventId(byte[] rawBody)
    {
        var hash = SHA256.HashData(rawBody);
        return "ig_" + Convert.ToHexStringLower(hash);
    }

    private static string? ExtractAccountId(byte[] rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            if (document.RootElement.TryGetProperty("entry", out var entries)
                && entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (ReadEntryId(entry) is { Length: > 0 } accountId)
                    {
                        return accountId;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private void NormalizeItem(
        RawWebhookPayload rawPayload,
        JsonElement item,
        DateTimeOffset? entryTime,
        List<NormalizedInboundEnvelope> envelopes)
    {
        var senderId = item.TryGetProperty("sender", out var sender)
            && sender.TryGetProperty("id", out var senderIdProp)
            ? senderIdProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(senderId))
        {
            LogItemSkipped(logger, rawPayload.ConnectionId, "item without sender");
            return;
        }

        var itemTimestamp = ReadUnixMilliseconds(item, "timestamp");
        var occurredAt = itemTimestamp ?? entryTime ?? rawPayload.ReceivedAt ?? DateTimeOffset.UnixEpoch;

        if (item.TryGetProperty("message", out var messageElement))
        {
            string? echoRecipient = null;
            if (IsTrue(messageElement, "is_echo"))
            {
                // Echo = a business-sent message; the customer is the recipient (ADR-017, M08-S05).
                echoRecipient = item.TryGetProperty("recipient", out var recipient)
                    && recipient.TryGetProperty("id", out var recipientIdProp)
                    ? recipientIdProp.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(echoRecipient))
                {
                    LogItemSkipped(logger, rawPayload.ConnectionId, "echo without recipient");
                    return;
                }
            }

            NormalizeMessage(rawPayload, senderId, messageElement, occurredAt, envelopes, echoRecipient);
            return;
        }

        if (item.TryGetProperty("reaction", out var reaction))
        {
            NormalizeReaction(rawPayload, senderId, reaction, item, occurredAt, envelopes);
            return;
        }

        if (item.TryGetProperty("read", out var read))
        {
            NormalizeRead(rawPayload, senderId, read, occurredAt, envelopes);
            return;
        }

        LogItemSkipped(logger, rawPayload.ConnectionId,
            item.TryGetProperty("postback", out _) ? "postback (not normalized in v1)" : "unsupported messaging item");
    }

    private void NormalizeMessage(
        RawWebhookPayload rawPayload,
        string senderId,
        JsonElement message,
        DateTimeOffset occurredAt,
        List<NormalizedInboundEnvelope> envelopes,
        string? echoRecipient = null)
    {
        var mid = message.TryGetProperty("mid", out var midProp) ? midProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(mid))
        {
            LogItemSkipped(logger, rawPayload.ConnectionId, "message without mid");
            return;
        }

        if (IsTrue(message, "is_deleted") || IsTrue(message, "is_unsupported"))
        {
            LogItemSkipped(logger, rawPayload.ConnectionId, "deleted or unsupported message");
            return;
        }

        var isEcho = echoRecipient is not null;

        var contents = new List<NormalizedInboundPayload>();

        var text = message.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(text)
            && message.TryGetProperty("quick_reply", out var quickReply)
            && quickReply.TryGetProperty("payload", out var payloadProp))
        {
            text = payloadProp.GetString();
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            contents.Add(new TextMessageReceivedPayload(mid, senderId, null, text, occurredAt)
            {
                IsEcho = isEcho,
                RecipientChannelId = echoRecipient
            });
        }

        if (message.TryGetProperty("attachments", out var attachments)
            && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var attachment in attachments.EnumerateArray())
            {
                var type = attachment.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                var url = attachment.TryGetProperty("payload", out var payload)
                    && payload.TryGetProperty("url", out var urlProp)
                    ? urlProp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(url))
                {
                    LogItemSkipped(logger, rawPayload.ConnectionId, "attachment without type or url");
                    continue;
                }

                contents.Add(new MediaMessageReceivedPayload(
                    mid,
                    senderId,
                    null,
                    url,
                    type,
                    null,
                    null,
                    occurredAt)
                {
                    IsEcho = isEcho,
                    RecipientChannelId = echoRecipient,
                    SharedPostId = SharedPostIdentifier(payload)
                });
            }
        }

        if (contents.Count == 0)
        {
            LogItemSkipped(logger, rawPayload.ConnectionId, "message without normalizable content");
        }

        for (var i = 0; i < contents.Count; i++)
        {
            var messageId = contents.Count == 1 ? mid : $"{mid}#p{i}";
            envelopes.Add(NormalizedInboundEnvelope.Create(
                eventId: isEcho ? $"ig_echo_{messageId}" : $"ig_{messageId}",
                tenantId: rawPayload.TenantId,
                connectionId: rawPayload.ConnectionId,
                channel: ChannelType.Instagram,
                occurredAt: occurredAt,
                payload: RenameMessageId(contents[i], messageId),
                // Message identity equals the (part) message ID, matching the M07 legacy key.
                deduplicationKey: messageId));
        }

        static NormalizedInboundPayload RenameMessageId(NormalizedInboundPayload payload, string messageId) => payload switch
        {
            TextMessageReceivedPayload textPayload => textPayload with { MessageId = messageId },
            MediaMessageReceivedPayload media => media with { MessageId = messageId },
            _ => payload
        };
    }

    private void NormalizeReaction(
        RawWebhookPayload rawPayload,
        string senderId,
        JsonElement reaction,
        JsonElement item,
        DateTimeOffset occurredAt,
        List<NormalizedInboundEnvelope> envelopes)
    {
        var mid = reaction.TryGetProperty("mid", out var midProp) ? midProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(mid))
        {
            LogItemSkipped(logger, rawPayload.ConnectionId, "reaction without mid");
            return;
        }

        var action = reaction.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : null;
        var isRemoved = string.Equals(action, "unreact", StringComparison.OrdinalIgnoreCase);
        var emoji = reaction.TryGetProperty("emoji", out var emojiProp) ? emojiProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(emoji))
        {
            emoji = reaction.TryGetProperty("reaction", out var nameProp) ? nameProp.GetString() : null;
        }

        if (string.IsNullOrWhiteSpace(emoji))
        {
            if (!isRemoved)
            {
                LogItemSkipped(logger, rawPayload.ConnectionId, "reaction without emoji");
                return;
            }

            // Meta's unreact payload may omit the emoji; removal is still recorded, with an empty emoji.
            emoji = string.Empty;
        }

        // Reactions have no provider event ID: identity = target message + reactor + action + event time.
        // Redeliveries repeat all four; react/unreact/re-react differ by action or timestamp.
        var timeKey = ItemTimeKey(item, occurredAt);
        envelopes.Add(NormalizedInboundEnvelope.Create(
            eventId: $"ig_{mid}_reaction",
            tenantId: rawPayload.TenantId,
            connectionId: rawPayload.ConnectionId,
            channel: ChannelType.Instagram,
            occurredAt: occurredAt,
            payload: new ReactionReceivedPayload(mid, senderId, emoji, isRemoved, occurredAt),
            deduplicationKey: $"reaction:{mid}:{senderId}:{(isRemoved ? "unreact" : "react")}:{timeKey}"));
    }

    private void NormalizeRead(
        RawWebhookPayload rawPayload,
        string senderId,
        JsonElement read,
        DateTimeOffset occurredAt,
        List<NormalizedInboundEnvelope> envelopes)
    {
        var mid = read.TryGetProperty("mid", out var midProp) ? midProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(mid))
        {
            LogItemSkipped(logger, rawPayload.ConnectionId, "read receipt without mid");
            return;
        }

        envelopes.Add(NormalizedInboundEnvelope.Create(
            eventId: $"ig_{mid}_read",
            tenantId: rawPayload.TenantId,
            connectionId: rawPayload.ConnectionId,
            channel: ChannelType.Instagram,
            occurredAt: occurredAt,
            payload: new MessageStatusUpdatedPayload(
                mid,
                senderId,
                MessageDeliveryStatus.Read,
                null,
                null,
                occurredAt),
            // A message is read once per reader; repeated seen events for it are idempotent.
            deduplicationKey: $"read:{mid}:{senderId}"));
    }

    private static string ItemTimeKey(JsonElement item, DateTimeOffset occurredAt) =>
        item.TryGetProperty("timestamp", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : occurredAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsTrue(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? ReadUnixMilliseconds(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var milliseconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The first <c>*_id</c> field of an attachment payload (for example a shared reel's <c>reel_video_id</c>), kept as
    /// <c>name:value</c> without interpretation. Which fields Meta sends for shared posts is verified live in M09-S07
    /// before any product matching uses them (M09-S04 Q6-A).
    /// </summary>
    public static string? SharedPostIdentifier(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in payload.EnumerateObject())
        {
            if (!property.Name.EndsWith("_id", StringComparison.Ordinal)) continue;
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(value)) return $"{property.Name}:{value}";
        }

        return null;
    }
}
