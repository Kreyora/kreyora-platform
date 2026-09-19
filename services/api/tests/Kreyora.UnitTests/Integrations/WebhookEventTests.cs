using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class WebhookEventTests
{
    [Fact]
    public void Create_WithValidParameters_InitializesCorrectly()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        var receivedAt = DateTimeOffset.UtcNow;
        var ev = WebhookEvent.Create(
            tenantId: "tenant_1",
            connectionId: "conn_1",
            channel: ChannelType.Simulator,
            providerEventId: "evt_12345",
            eventType: "messages",
            occurredAt: occurredAt,
            receivedAt: receivedAt,
            correlationId: "corr_9876",
            headers: "{\"content-type\":\"application/json\"}",
            rawPayload: "{\"text\":\"hello\"}");

        Assert.Equal("tenant_1", ev.TenantId);
        Assert.Equal("conn_1", ev.ConnectionId);
        Assert.Equal(ChannelType.Simulator, ev.Channel);
        Assert.Equal("evt_12345", ev.ProviderEventId);
        Assert.Equal("messages", ev.EventType);
        Assert.Equal(occurredAt, ev.OccurredAt);
        Assert.Equal(receivedAt, ev.ReceivedAt);
        Assert.Equal(WebhookProcessingStatus.Received, ev.ProcessingStatus);
        Assert.Equal("corr_9876", ev.CorrelationId);
        Assert.Equal("{\"content-type\":\"application/json\"}", ev.Headers);
        Assert.Equal("{\"text\":\"hello\"}", ev.RawPayload);
        Assert.False(ev.IsPurged);
        Assert.Null(ev.PurgedAt);
        Assert.Null(ev.ProcessedAt);
        Assert.Null(ev.ErrorMessage);
    }

    [Theory]
    [InlineData("", "conn_1", "evt_1", "corr_1", "{}", "{}")]
    [InlineData("tenant_1", "", "evt_1", "corr_1", "{}", "{}")]
    [InlineData("tenant_1", "conn_1", "", "corr_1", "{}", "{}")]
    [InlineData("tenant_1", "conn_1", "evt_1", "", "{}", "{}")]
    [InlineData("tenant_1", "conn_1", "evt_1", "corr_1", "", "{}")]
    [InlineData("tenant_1", "conn_1", "evt_1", "corr_1", "{}", "")]
    public void Create_WithMissingRequiredFields_ThrowsArgumentException(
        string tenantId,
        string connectionId,
        string providerEventId,
        string correlationId,
        string headers,
        string rawPayload)
    {
        Assert.ThrowsAny<ArgumentException>(() => WebhookEvent.Create(
            tenantId,
            connectionId,
            ChannelType.Simulator,
            providerEventId,
            "messages",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            correlationId,
            headers,
            rawPayload));
    }

    [Fact]
    public void Create_WithOversizedFields_ThrowsArgumentOutOfRangeException()
    {
        var longEventId = new string('x', WebhookEvent.ProviderEventIdMaxLength + 1);
        var longEventType = new string('x', WebhookEvent.EventTypeMaxLength + 1);
        var longCorrelationId = new string('x', WebhookEvent.CorrelationIdMaxLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => WebhookEvent.Create(
            "tenant_1", "conn_1", ChannelType.Simulator, longEventId, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "corr_1", "{}", "{}"));

        Assert.Throws<ArgumentOutOfRangeException>(() => WebhookEvent.Create(
            "tenant_1", "conn_1", ChannelType.Simulator, "evt_1", longEventType,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "corr_1", "{}", "{}"));

        Assert.Throws<ArgumentOutOfRangeException>(() => WebhookEvent.Create(
            "tenant_1", "conn_1", ChannelType.Simulator, "evt_1", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, longCorrelationId, "{}", "{}"));
    }

    [Fact]
    public void StatusTransitions_UpdateStateCorrectly()
    {
        var ev = WebhookEvent.Create(
            "tenant_1", "conn_1", ChannelType.Simulator, "evt_1", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "corr_1", "{}", "{}");

        Assert.Equal(WebhookProcessingStatus.Received, ev.ProcessingStatus);

        ev.MarkProcessing();
        Assert.Equal(WebhookProcessingStatus.Processing, ev.ProcessingStatus);

        var processedAt = DateTimeOffset.UtcNow;
        ev.MarkProcessed(processedAt);
        Assert.Equal(WebhookProcessingStatus.Processed, ev.ProcessingStatus);
        Assert.Equal(processedAt, ev.ProcessedAt);

        ev.MarkFailed("Processing timeout");
        Assert.Equal(WebhookProcessingStatus.Failed, ev.ProcessingStatus);
        Assert.Equal("Processing timeout", ev.ErrorMessage);

        ev.MarkFailed("Exhausted retries", deadLetter: true);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, ev.ProcessingStatus);
        Assert.Equal("Exhausted retries", ev.ErrorMessage);
    }

    [Fact]
    public void PurgePayload_ReplacesBodyWithPurgedMarkerAndSetsFlags()
    {
        var ev = WebhookEvent.Create(
            "tenant_1", "conn_1", ChannelType.Simulator, "evt_1", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "corr_1", "{}", "{\"pii\":\"customer_phone\"}");

        var purgedAt = DateTimeOffset.UtcNow;
        ev.PurgePayload(purgedAt);

        Assert.Equal(WebhookEvent.PurgedMarker, ev.RawPayload);
        Assert.True(ev.IsPurged);
        Assert.Equal(purgedAt, ev.PurgedAt);
    }
}

