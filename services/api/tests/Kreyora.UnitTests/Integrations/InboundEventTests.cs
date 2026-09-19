using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class InboundEventTests
{
    [Fact]
    public void Create_WithValidParameters_InitializesCorrectly()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-2);
        var inbound = InboundEvent.Create(
            tenantId: "tenant_123",
            connectionId: "conn_456",
            webhookEventId: "wh_789",
            channel: ChannelType.Simulator,
            providerMessageId: "wamid_abc123",
            schemaVersion: "v1",
            eventType: "text",
            payloadJson: "{\"text\":\"hello world\"}",
            occurredAt: occurredAt);

        Assert.Equal("tenant_123", inbound.TenantId);
        Assert.Equal("conn_456", inbound.ConnectionId);
        Assert.Equal("wh_789", inbound.WebhookEventId);
        Assert.Equal(ChannelType.Simulator, inbound.Channel);
        Assert.Equal("wamid_abc123", inbound.ProviderMessageId);
        Assert.Equal("v1", inbound.SchemaVersion);
        Assert.Equal("text", inbound.EventType);
        Assert.Equal("{\"text\":\"hello world\"}", inbound.PayloadJson);
        Assert.Equal(occurredAt, inbound.OccurredAt);
        Assert.True(inbound.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("", "conn_1", "wh_1", "v1", "text", "{}")]
    [InlineData("tenant_1", "", "wh_1", "v1", "text", "{}")]
    [InlineData("tenant_1", "conn_1", "", "v1", "text", "{}")]
    [InlineData("tenant_1", "conn_1", "wh_1", "", "text", "{}")]
    [InlineData("tenant_1", "conn_1", "wh_1", "v1", "", "{}")]
    [InlineData("tenant_1", "conn_1", "wh_1", "v1", "text", "")]
    public void Create_WithMissingRequiredParameters_ThrowsArgumentException(
        string tenantId,
        string connectionId,
        string webhookEventId,
        string schemaVersion,
        string eventType,
        string payloadJson)
    {
        Assert.ThrowsAny<ArgumentException>(() => InboundEvent.Create(
            tenantId,
            connectionId,
            webhookEventId,
            ChannelType.Simulator,
            "msg_1",
            schemaVersion,
            eventType,
            payloadJson,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_WithOversizedFields_ThrowsArgumentOutOfRangeException()
    {
        var longMsgId = new string('x', InboundEvent.ProviderMessageIdMaxLength + 1);
        var longSchemaVersion = new string('v', InboundEvent.SchemaVersionMaxLength + 1);
        var longEventType = new string('e', InboundEvent.EventTypeMaxLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => InboundEvent.Create(
            "tenant_1", "conn_1", "wh_1", ChannelType.Simulator,
            longMsgId, "v1", "text", "{}", DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentOutOfRangeException>(() => InboundEvent.Create(
            "tenant_1", "conn_1", "wh_1", ChannelType.Simulator,
            "msg_1", longSchemaVersion, "text", "{}", DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentOutOfRangeException>(() => InboundEvent.Create(
            "tenant_1", "conn_1", "wh_1", ChannelType.Simulator,
            "msg_1", "v1", longEventType, "{}", DateTimeOffset.UtcNow));
    }
}

