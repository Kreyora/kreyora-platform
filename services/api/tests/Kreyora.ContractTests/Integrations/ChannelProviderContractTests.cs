using System.Text;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations;

namespace Kreyora.ContractTests.Integrations;

public sealed class FakeSimulatorChannelProvider : IChannelProvider
{
    public ChannelType Channel => ChannelType.Simulator;
    public ChannelCapabilities Capabilities => ChannelCapabilities.FullSimulator();

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        WebhookValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Headers.TryGetValue("X-Hub-Signature-256", out var sig) && sig == "sha256=valid_test_signature")
        {
            return Task.FromResult(WebhookValidationResult.Success());
        }

        return Task.FromResult(WebhookValidationResult.Failed("Invalid signature header"));
    }

    public Task<IReadOnlyList<NormalizedInboundEnvelope>> NormalizeInboundAsync(
        RawWebhookPayload rawPayload,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var envelopes = new List<NormalizedInboundEnvelope>
        {
            NormalizedInboundEnvelope.Create(
                eventId: "evt_sim_1",
                tenantId: rawPayload.TenantId,
                connectionId: rawPayload.ConnectionId,
                channel: ChannelType.Simulator,
                occurredAt: now,
                payload: new TextMessageReceivedPayload("msg_sim_1", "user_1", "Test User", "Hello Simulator", now))
        };

        return Task.FromResult<IReadOnlyList<NormalizedInboundEnvelope>>(envelopes);
    }

    public Task<OutboundDeliveryResult> SendMessageAsync(
        ChannelConnectionSnapshot connection,
        OutboundMessageRequest message,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OutboundDeliveryResult.Delivered(
            providerMessageId: "out_sim_123",
            deliveredAt: DateTimeOffset.UtcNow));
    }

    public Task<ConnectionHealthResult> ValidateOrRefreshConnectionAsync(
        ChannelConnectionSnapshot connection,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ConnectionHealthResult.Healthy());
    }
}

public sealed class ChannelProviderContractTests
{
    [Fact]
    public void Registry_ResolvesRegisteredProvider_Successfully()
    {
        var provider = new FakeSimulatorChannelProvider();
        var registry = new ChannelProviderRegistry(new[] { provider });

        var resolved = registry.GetProvider(ChannelType.Simulator);

        Assert.NotNull(resolved);
        Assert.Same(provider, resolved);
        Assert.Equal(ChannelType.Simulator, resolved.Channel);
    }

    [Fact]
    public void Registry_TryGetProvider_ReturnsTrueForRegistered_AndFalseForUnregistered()
    {
        var provider = new FakeSimulatorChannelProvider();
        var registry = new ChannelProviderRegistry(new[] { provider });

        var exists = registry.TryGetProvider(ChannelType.Simulator, out var simProvider);
        Assert.True(exists);
        Assert.NotNull(simProvider);

        var notExists = registry.TryGetProvider(ChannelType.WhatsApp, out var waProvider);
        Assert.False(notExists);
        Assert.Null(waProvider);
    }

    [Fact]
    public void Registry_GetProvider_ForUnregisteredChannel_ThrowsNotSupportedException()
    {
        var registry = new ChannelProviderRegistry(Array.Empty<IChannelProvider>());

        var ex = Assert.Throws<NotSupportedException>(() => registry.GetProvider(ChannelType.WhatsApp));
        Assert.Contains("WhatsApp", ex.Message);
    }

    [Fact]
    public async Task Provider_ValidateWebhookAsync_EnforcesSignatureContract()
    {
        var provider = new FakeSimulatorChannelProvider();

        var validReq = WebhookValidationRequest.Create(
            rawBody: "{}",
            headers: new Dictionary<string, string> { ["X-Hub-Signature-256"] = "sha256=valid_test_signature" },
            secret: "test_secret");

        var invalidReq = WebhookValidationRequest.Create(
            rawBody: "{}",
            headers: new Dictionary<string, string> { ["X-Hub-Signature-256"] = "sha256=bad_sig" },
            secret: "test_secret");

        var validResult = await provider.ValidateWebhookAsync(validReq);
        var invalidResult = await provider.ValidateWebhookAsync(invalidReq);

        Assert.True(validResult.IsValid);
        Assert.Null(validResult.FailureReason);

        Assert.False(invalidResult.IsValid);
        Assert.Equal("Invalid signature header", invalidResult.FailureReason);
    }

    [Fact]
    public async Task Provider_NormalizeInboundAsync_ReturnsNormalizedEnvelopesWithTenantIsolation()
    {
        var provider = new FakeSimulatorChannelProvider();

        var raw = new RawWebhookPayload(
            RawBody: "{\"text\":\"Hello Simulator\"}",
            Headers: new Dictionary<string, string> { ["content-type"] = "application/json" },
            ContentType: "application/json",
            TenantId: "tenant_kreyora_1",
            ConnectionId: "conn_kreyora_1",
            PayloadId: "raw_123",
            Channel: ChannelType.Simulator,
            ReceivedAt: DateTimeOffset.UtcNow);

        var envelopes = await provider.NormalizeInboundAsync(raw);

        Assert.NotEmpty(envelopes);
        var envelope = envelopes[0];
        Assert.Equal("tenant_kreyora_1", envelope.TenantId);
        Assert.Equal("conn_kreyora_1", envelope.ConnectionId);
        Assert.Equal(ChannelType.Simulator, envelope.Channel);
        Assert.Equal("v1", envelope.SchemaVersion);

        var textPayload = Assert.IsType<TextMessageReceivedPayload>(envelope.Payload);
        Assert.Equal("Hello Simulator", textPayload.Text);
    }

    [Fact]
    public async Task Provider_SendMessageAsync_ReturnsDeliveryResult()
    {
        var provider = new FakeSimulatorChannelProvider();

        var connection = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator,
            status: ChannelConnectionStatus.Connected);

        var message = OutboundMessageRequest.TextMessage(
            recipientChannelId: "+9779800000000",
            text: "Order confirmed");

        var result = await provider.SendMessageAsync(connection, message);

        Assert.True(result.IsSuccess);
        Assert.Equal("out_sim_123", result.ProviderMessageId);
        Assert.NotNull(result.DeliveredAt);
    }

    [Fact]
    public async Task Provider_ValidateOrRefreshConnectionAsync_ReturnsHealthy()
    {
        var provider = new FakeSimulatorChannelProvider();

        var connection = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator,
            status: ChannelConnectionStatus.Connected);

        var health = await provider.ValidateOrRefreshConnectionAsync(connection);

        Assert.True(health.IsHealthy);
        Assert.Equal(ChannelConnectionStatus.Connected, health.NewStatus);
    }
}
