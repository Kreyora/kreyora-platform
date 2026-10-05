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
/// </summary>
public sealed class Message : BaseEntity, ITenantOwned
{
    public const int ProviderMessageIdMaxLength = 512;
    public const int MediaContentTypeMaxLength = 64;

    private Message() { }

    public string TenantId { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public string? InboundEventId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public MessageOrigin Origin { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public MessageKind Kind { get; private set; }
    public string? Text { get; private set; }
    public string? MediaUrl { get; private set; }
    public string? MediaContentType { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public MessageDeliveryStatus? DeliveryStatus { get; private set; }
    public DateTimeOffset? RedactedAt { get; private set; }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var message = CreateCore(tenantId, conversationId, connectionId, inboundEventId, providerMessageId, MessageKind.Text, occurredAt, receivedAt);
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
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaContentType);
        var message = CreateCore(tenantId, conversationId, connectionId, inboundEventId, providerMessageId, MessageKind.Media, occurredAt, receivedAt);
        message.MediaUrl = mediaUrl;
        message.MediaContentType = mediaContentType.Length > MediaContentTypeMaxLength
            ? mediaContentType[..MediaContentTypeMaxLength]
            : mediaContentType;
        message.Text = string.IsNullOrWhiteSpace(caption) ? null : caption;
        return message;
    }

    /// <summary>
    /// An outbound text already accepted by the provider (it has a provider message ID), starting as
    /// <see cref="MessageDeliveryStatus.Sent"/>. Staff/automation send orchestration arrives in M08-S05.
    /// </summary>
    public static Message CreateOutboundText(
        string tenantId,
        string conversationId,
        string connectionId,
        MessageOrigin origin,
        string providerMessageId,
        string text,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        if (origin == MessageOrigin.Customer)
        {
            throw new ArgumentException("Outbound messages cannot originate from the customer.", nameof(origin));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var message = CreateCore(tenantId, conversationId, connectionId, null, providerMessageId, MessageKind.Text, occurredAt, receivedAt);
        message.Direction = MessageDirection.Outbound;
        message.Origin = origin;
        message.Text = text;
        message.DeliveryStatus = MessageDeliveryStatus.Sent;
        return message;
    }

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
        RedactedAt = now;
        return true;
    }

    private static Message CreateCore(
        string tenantId,
        string conversationId,
        string connectionId,
        string? inboundEventId,
        string providerMessageId,
        MessageKind kind,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        if (providerMessageId.Length > ProviderMessageIdMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(providerMessageId));
        }

        return new Message
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            ConnectionId = connectionId,
            InboundEventId = string.IsNullOrWhiteSpace(inboundEventId) ? null : inboundEventId,
            Direction = MessageDirection.Inbound,
            Origin = MessageOrigin.Customer,
            ProviderMessageId = providerMessageId,
            Kind = kind,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt
        };
    }

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
