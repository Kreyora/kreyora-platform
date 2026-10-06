using Kreyora.Domain.Common;
using Kreyora.Domain.Integrations;

namespace Kreyora.Domain.Conversations;

public enum MessageDirection
{
    Inbound = 1,
    Outbound = 2
}

/// <summary>Who produced the message. <see cref="ProviderNative"/> = sent outside Kreyora (e.g. the Instagram app).</summary>
public enum MessageOrigin
{
    Customer = 1,
    Staff = 2,
    Automation = 3,
    ProviderNative = 4
}

public enum MessageKind
{
    Text = 1,
    Media = 2
}

/// <summary>
/// One timeline entry. Ordered by provider time (<see cref="OccurredAt"/>), never by arrival.
/// Provider message IDs are unique per connection so sends and their echoes can never duplicate.
/// A staff reply exists as a pending entry (no provider ID, no delivery status) until the provider accepts it.
/// </summary>
public sealed class Message : BaseEntity, ITenantOwned
{
    public const int ProviderMessageIdMaxLength = 512;
    public const int MediaContentTypeMaxLength = 64;
    public const int SharedPostIdMaxLength = 128;

    private Message() { }

    public string TenantId { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public string? InboundEventId { get; private set; }
    public string? OutboundMessageId { get; private set; }
    public string? ActorUserId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public MessageOrigin Origin { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public MessageKind Kind { get; private set; }
    public string? Text { get; private set; }
    public string? MediaUrl { get; private set; }
    public string? MediaContentType { get; private set; }

    /// <summary>
    /// Identifier of a shared post/reel exactly as the provider sent it (e.g. <c>reel_video_id:123</c>); stored, not
    /// interpreted, until product matching is built from a verified live payload (M09-S04 Q6-A).
    /// </summary>
    public string? SharedPostId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public MessageDeliveryStatus? DeliveryStatus { get; private set; }
    public DateTimeOffset? RedactedAt { get; private set; }

    public bool IsPending => Direction == MessageDirection.Outbound && DeliveryStatus is null;

    public static Message CreateInboundText(
        string tenantId,
        string conversationId,
        string connectionId,
        string? inboundEventId,
        string providerMessageId,
        string text,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var message = CreateCore(tenantId, conversationId, connectionId, providerMessageId, MessageDirection.Inbound, MessageOrigin.Customer, MessageKind.Text, occurredAt, receivedAt);
        message.InboundEventId = Optional(inboundEventId);
        message.Text = text;
        return message;
    }

    public static Message CreateInboundMedia(
        string tenantId,
        string conversationId,
        string connectionId,
        string? inboundEventId,
        string providerMessageId,
        string mediaUrl,
        string mediaContentType,
        string? caption,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string? sharedPostId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        var message = CreateCore(tenantId, conversationId, connectionId, providerMessageId, MessageDirection.Inbound, MessageOrigin.Customer, MessageKind.Media, occurredAt, receivedAt);
        message.InboundEventId = Optional(inboundEventId);
        message.ApplyMedia(mediaUrl, mediaContentType, caption);
        message.SharedPostId = string.IsNullOrWhiteSpace(sharedPostId) ? null
            : sharedPostId.Length > SharedPostIdMaxLength ? sharedPostId[..SharedPostIdMaxLength] : sharedPostId;
        return message;
    }

    /// <summary>A staff reply recorded at enqueue time; it becomes Sent or Failed when delivery finishes.</summary>
    public static Message CreatePendingStaffReply(
        string tenantId,
        string conversationId,
        string connectionId,
        string outboundMessageId,
        string actorUserId,
        string text,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboundMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var message = CreateCore(tenantId, conversationId, connectionId, null, MessageDirection.Outbound, MessageOrigin.Staff, MessageKind.Text, now, now);
        message.OutboundMessageId = outboundMessageId;
        message.ActorUserId = actorUserId;
        message.Text = text;
        return message;
    }

    /// <summary>
    /// An outbound text already accepted by the provider (it has a provider message ID), starting as
    /// <see cref="MessageDeliveryStatus.Sent"/>. Used for automation sends, which enter the timeline only
    /// once accepted (ADR-017).
    /// </summary>
    public static Message CreateOutboundText(
        string tenantId,
        string conversationId,
        string connectionId,
        MessageOrigin origin,
        string providerMessageId,
        string text,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string? outboundMessageId = null,
        string? actorUserId = null)
    {
        if (origin == MessageOrigin.Customer)
        {
            throw new ArgumentException("Outbound messages cannot originate from the customer.", nameof(origin));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var message = CreateCore(tenantId, conversationId, connectionId, providerMessageId, MessageDirection.Outbound, origin, MessageKind.Text, occurredAt, receivedAt);
        message.Text = text;
        message.OutboundMessageId = Optional(outboundMessageId);
        message.ActorUserId = Optional(actorUserId);
        message.DeliveryStatus = MessageDeliveryStatus.Sent;
        return message;
    }

    /// <summary>A business message sent outside Kreyora (provider echo), e.g. typed in the Instagram app.</summary>
    public static Message CreateProviderNativeEcho(
        string tenantId,
        string conversationId,
        string connectionId,
        string? inboundEventId,
        string providerMessageId,
        string? text,
        string? mediaUrl,
        string? mediaContentType,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        var isMedia = !string.IsNullOrWhiteSpace(mediaUrl);
        if (!isMedia && string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("An echo needs text or media.", nameof(text));
        }

        var message = CreateCore(tenantId, conversationId, connectionId, providerMessageId, MessageDirection.Outbound,
            MessageOrigin.ProviderNative, isMedia ? MessageKind.Media : MessageKind.Text, occurredAt, receivedAt);
        message.InboundEventId = Optional(inboundEventId);
        if (isMedia)
        {
            message.ApplyMedia(mediaUrl!, mediaContentType ?? "unknown", text);
        }
        else
        {
            message.Text = text;
        }

        message.DeliveryStatus = MessageDeliveryStatus.Sent;
        return message;
    }

    /// <summary>The provider accepted a pending outbound message.</summary>
    public void RecordProviderAcceptance(string providerMessageId, DateTimeOffset sentAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        if (Direction != MessageDirection.Outbound)
        {
            throw new InvalidOperationException("Only outbound messages can be accepted by a provider.");
        }

        if (providerMessageId.Length > ProviderMessageIdMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(providerMessageId));
        }

        ProviderMessageId = providerMessageId;
        OccurredAt = sentAt;
        AdvanceDeliveryStatus(MessageDeliveryStatus.Sent);
    }

    /// <summary>Terminal delivery failure of a pending outbound message.</summary>
    public bool MarkDeliveryFailed() =>
        IsPending && AdvanceDeliveryStatus(MessageDeliveryStatus.Failed);

    /// <summary>Delivery state only moves forward (Sent → Delivered → Read); a failure never replaces a success.</summary>
    public bool AdvanceDeliveryStatus(MessageDeliveryStatus status)
    {
        if (Direction != MessageDirection.Outbound || Rank(status) <= Rank(DeliveryStatus))
        {
            return false;
        }

        DeliveryStatus = status;
        return true;
    }

    /// <summary>Removes customer content (right to erasure) while keeping the timeline structure.</summary>
    public bool Redact(DateTimeOffset now)
    {
        if (RedactedAt.HasValue)
        {
            return false;
        }

        Text = null;
        MediaUrl = null;
        SharedPostId = null;
        RedactedAt = now;
        return true;
    }

    private void ApplyMedia(string mediaUrl, string mediaContentType, string? caption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaContentType);
        MediaUrl = mediaUrl;
        MediaContentType = mediaContentType.Length > MediaContentTypeMaxLength
            ? mediaContentType[..MediaContentTypeMaxLength]
            : mediaContentType;
        Text = string.IsNullOrWhiteSpace(caption) ? null : caption;
    }

    private static Message CreateCore(
        string tenantId,
        string conversationId,
        string connectionId,
        string? providerMessageId,
        MessageDirection direction,
        MessageOrigin origin,
        MessageKind kind,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (providerMessageId is { Length: > ProviderMessageIdMaxLength })
        {
            throw new ArgumentOutOfRangeException(nameof(providerMessageId));
        }

        return new Message
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            ConnectionId = connectionId,
            Direction = direction,
            Origin = origin,
            ProviderMessageId = Optional(providerMessageId),
            Kind = kind,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt
        };
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int Rank(MessageDeliveryStatus? status) => status switch
    {
        null => 0,
        MessageDeliveryStatus.Failed => 1,
        MessageDeliveryStatus.Sent => 2,
        MessageDeliveryStatus.Delivered => 3,
        MessageDeliveryStatus.Read => 4,
        _ => 0
    };
}
