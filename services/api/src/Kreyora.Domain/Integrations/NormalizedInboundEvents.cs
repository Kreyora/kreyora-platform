using System.Text.Json.Serialization;

namespace Kreyora.Domain.Integrations;

public enum MessageDeliveryStatus
{
    Sent = 1,
    Delivered = 2,
    Read = 3,
    Failed = 4
}

public sealed record NormalizedInboundEnvelope(
    string EventId,
    string TenantId,
    string ConnectionId,
    ChannelType Channel,
    DateTimeOffset OccurredAt,
    string SchemaVersion,
    NormalizedInboundPayload Payload)
{
    public const string CurrentSchemaVersion = "v1";

    public static NormalizedInboundEnvelope Create(
        string eventId,
        string tenantId,
        string connectionId,
        ChannelType channel,
        DateTimeOffset occurredAt,
        NormalizedInboundPayload payload,
        string schemaVersion = CurrentSchemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentNullException.ThrowIfNull(payload);

        return new NormalizedInboundEnvelope(
            eventId,
            tenantId,
            connectionId,
            channel,
            occurredAt,
            schemaVersion,
            payload);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextMessageReceivedPayload), typeDiscriminator: "text")]
[JsonDerivedType(typeof(MediaMessageReceivedPayload), typeDiscriminator: "media")]
[JsonDerivedType(typeof(MessageStatusUpdatedPayload), typeDiscriminator: "status")]
[JsonDerivedType(typeof(CustomerProfileUpdatedPayload), typeDiscriminator: "profile")]
[JsonDerivedType(typeof(ReactionReceivedPayload), typeDiscriminator: "reaction")]
public abstract record NormalizedInboundPayload;

public sealed record TextMessageReceivedPayload(
    string MessageId,
    string SenderChannelId,
    string? SenderName,
    string Text,
    DateTimeOffset Timestamp) : NormalizedInboundPayload;

public sealed record MediaMessageReceivedPayload(
    string MessageId,
    string SenderChannelId,
    string? SenderName,
    string MediaUrl,
    string ContentType,
    long? ByteSize,
    string? Caption,
    DateTimeOffset Timestamp) : NormalizedInboundPayload;

public sealed record MessageStatusUpdatedPayload(
    string MessageId,
    string RecipientChannelId,
    MessageDeliveryStatus Status,
    string? ProviderErrorCode,
    string? ProviderErrorMessage,
    DateTimeOffset Timestamp) : NormalizedInboundPayload;

public sealed record CustomerProfileUpdatedPayload(
    string SenderChannelId,
    string? DisplayName,
    string? ProfilePictureUrl,
    string? PhoneNumber) : NormalizedInboundPayload;

public sealed record ReactionReceivedPayload(
    string MessageId,
    string SenderChannelId,
    string Emoji,
    bool IsRemoved,
    DateTimeOffset Timestamp) : NormalizedInboundPayload;

