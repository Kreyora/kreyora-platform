using System.Text.Json;
using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class NormalizedInboundEventTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void Create_WithValidArguments_InitializesEnvelope()
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new TextMessageReceivedPayload(
            MessageId: "msg_123",
            SenderChannelId: "+9779800000000",
            SenderName: "Ram Sharma",
            Text: "Namaste, is this item available?",
            Timestamp: now);

        var envelope = NormalizedInboundEnvelope.Create(
            eventId: "evt_123",
            tenantId: "tenant_abc",
            connectionId: "conn_xyz",
            channel: ChannelType.WhatsApp,
            occurredAt: now,
            payload: payload);

        Assert.Equal("evt_123", envelope.EventId);
        Assert.Equal("tenant_abc", envelope.TenantId);
        Assert.Equal("conn_xyz", envelope.ConnectionId);
        Assert.Equal(ChannelType.WhatsApp, envelope.Channel);
        Assert.Equal(now, envelope.OccurredAt);
        Assert.Equal("v1", envelope.SchemaVersion);
        Assert.Same(payload, envelope.Payload);
    }

    [Theory]
    [InlineData("", "tenant_1", "conn_1")]
    [InlineData("   ", "tenant_1", "conn_1")]
    [InlineData("evt_1", "", "conn_1")]
    [InlineData("evt_1", "tenant_1", "")]
    public void Create_WithInvalidRequiredFields_ThrowsArgumentException(string eventId, string tenantId, string connectionId)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new TextMessageReceivedPayload("m1", "s1", "Ram", "Hi", now);

        Assert.ThrowsAny<ArgumentException>(() => NormalizedInboundEnvelope.Create(
            eventId: eventId,
            tenantId: tenantId,
            connectionId: connectionId,
            channel: ChannelType.WhatsApp,
            occurredAt: now,
            payload: payload));
    }

    [Fact]
    public void Create_WithNullPayload_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => NormalizedInboundEnvelope.Create(
            eventId: "evt_1",
            tenantId: "tenant_1",
            connectionId: "conn_1",
            channel: ChannelType.WhatsApp,
            occurredAt: DateTimeOffset.UtcNow,
            payload: null!));
    }

    [Fact]
    public void TextMessageReceivedPayload_PolymorphicSerialization_RoundTripsSuccessfully()
    {
        var now = DateTimeOffset.UtcNow;
        var original = NormalizedInboundEnvelope.Create(
            "evt_text_1",
            "tenant_1",
            "conn_1",
            ChannelType.WhatsApp,
            now,
            new TextMessageReceivedPayload("msg_1", "+9779800000000", "Ram", "Hello", now));

        var json = JsonSerializer.Serialize(original, JsonOptions);
        Assert.Contains("\"$type\":\"text\"", json);

        var deserialized = JsonSerializer.Deserialize<NormalizedInboundEnvelope>(json, JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(original.EventId, deserialized.EventId);
        var textPayload = Assert.IsType<TextMessageReceivedPayload>(deserialized.Payload);
        Assert.Equal("msg_1", textPayload.MessageId);
        Assert.Equal("+9779800000000", textPayload.SenderChannelId);
        Assert.Equal("Ram", textPayload.SenderName);
        Assert.Equal("Hello", textPayload.Text);
    }

    [Fact]
    public void MediaMessageReceivedPayload_PolymorphicSerialization_RoundTripsSuccessfully()
    {
        var now = DateTimeOffset.UtcNow;
        var original = NormalizedInboundEnvelope.Create(
            "evt_media_1",
            "tenant_1",
            "conn_1",
            ChannelType.Instagram,
            now,
            new MediaMessageReceivedPayload("msg_2", "ig_user_1", "Sita", "https://cdn.example.com/img.jpg", "image/jpeg", 1024, "Look at this", now));

        var json = JsonSerializer.Serialize(original, JsonOptions);
        Assert.Contains("\"$type\":\"media\"", json);

        var deserialized = JsonSerializer.Deserialize<NormalizedInboundEnvelope>(json, JsonOptions);
        Assert.NotNull(deserialized);
        var mediaPayload = Assert.IsType<MediaMessageReceivedPayload>(deserialized.Payload);
        Assert.Equal("https://cdn.example.com/img.jpg", mediaPayload.MediaUrl);
        Assert.Equal("image/jpeg", mediaPayload.ContentType);
        Assert.Equal(1024, mediaPayload.ByteSize);
        Assert.Equal("Look at this", mediaPayload.Caption);
    }

    [Fact]
    public void MessageStatusUpdatedPayload_PolymorphicSerialization_RoundTripsSuccessfully()
    {
        var now = DateTimeOffset.UtcNow;
        var original = NormalizedInboundEnvelope.Create(
            "evt_status_1",
            "tenant_1",
            "conn_1",
            ChannelType.WhatsApp,
            now,
            new MessageStatusUpdatedPayload("msg_out_1", "+9779800000000", MessageDeliveryStatus.Delivered, null, null, now));

        var json = JsonSerializer.Serialize(original, JsonOptions);
        Assert.Contains("\"$type\":\"status\"", json);

        var deserialized = JsonSerializer.Deserialize<NormalizedInboundEnvelope>(json, JsonOptions);
        Assert.NotNull(deserialized);
        var statusPayload = Assert.IsType<MessageStatusUpdatedPayload>(deserialized.Payload);
        Assert.Equal("msg_out_1", statusPayload.MessageId);
        Assert.Equal(MessageDeliveryStatus.Delivered, statusPayload.Status);
    }

    [Fact]
    public void CustomerProfileUpdatedPayload_PolymorphicSerialization_RoundTripsSuccessfully()
    {
        var now = DateTimeOffset.UtcNow;
        var original = NormalizedInboundEnvelope.Create(
            "evt_profile_1",
            "tenant_1",
            "conn_1",
            ChannelType.Messenger,
            now,
            new CustomerProfileUpdatedPayload("fb_user_1", "Gita Thapa", "https://fb.com/pic.jpg", "+9779811111111"));

        var json = JsonSerializer.Serialize(original, JsonOptions);
        Assert.Contains("\"$type\":\"profile\"", json);

        var deserialized = JsonSerializer.Deserialize<NormalizedInboundEnvelope>(json, JsonOptions);
        Assert.NotNull(deserialized);
        var profilePayload = Assert.IsType<CustomerProfileUpdatedPayload>(deserialized.Payload);
        Assert.Equal("Gita Thapa", profilePayload.DisplayName);
        Assert.Equal("https://fb.com/pic.jpg", profilePayload.ProfilePictureUrl);
    }

    [Fact]
    public void ReactionReceivedPayload_PolymorphicSerialization_RoundTripsSuccessfully()
    {
        var now = DateTimeOffset.UtcNow;
        var original = NormalizedInboundEnvelope.Create(
            "evt_rxn_1",
            "tenant_1",
            "conn_1",
            ChannelType.Telegram,
            now,
            new ReactionReceivedPayload("msg_4", "tg_user_1", "👍", false, now));

        var json = JsonSerializer.Serialize(original, JsonOptions);
        Assert.Contains("\"$type\":\"reaction\"", json);

        var deserialized = JsonSerializer.Deserialize<NormalizedInboundEnvelope>(json, JsonOptions);
        Assert.NotNull(deserialized);
        var reactionPayload = Assert.IsType<ReactionReceivedPayload>(deserialized.Payload);
        Assert.Equal("👍", reactionPayload.Emoji);
        Assert.False(reactionPayload.IsRemoved);
    }
}

