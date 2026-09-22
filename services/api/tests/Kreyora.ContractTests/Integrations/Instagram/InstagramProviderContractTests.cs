using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations;

namespace Kreyora.ContractTests.Integrations.Instagram;

public sealed class InstagramProviderContractTests
{
    [Fact]
    public void Registry_HasNoInstagramAdapter_Yet()
    {
        var registry = new ChannelProviderRegistry(Array.Empty<IChannelProvider>());

        Assert.False(registry.TryGetProvider(ChannelType.Instagram, out var provider));
        Assert.Null(provider);
        Assert.Throws<NotSupportedException>(() => registry.GetProvider(ChannelType.Instagram));
    }

    [Fact]
    public void InstagramPreset_MapsOnlyVerifiedCapabilities()
    {
        var caps = ChannelCapabilities.ForChannel(ChannelType.Instagram);

        Assert.True(caps.CanReceiveText);
        Assert.True(caps.CanReceiveMedia);
        Assert.True(caps.CanSendText);
        Assert.True(caps.CanSendMedia);
        Assert.True(caps.SupportsReadReceipts);
        Assert.False(caps.SupportsDeliveryReceipts);
        Assert.True(caps.Enforces24HourWindow);
        Assert.True(caps.RequiresSignatureVerification);
    }

    [Fact]
    public void InstagramChannel_AcceptsVersionedEnvelopes_WithoutAdapter()
    {
        var now = DateTimeOffset.UtcNow;
        var envelope = NormalizedInboundEnvelope.Create(
            eventId: "evt_ig_contract_1",
            tenantId: "tenant_kreyora_1",
            connectionId: "conn_ig_1",
            channel: ChannelType.Instagram,
            occurredAt: now,
            payload: new TextMessageReceivedPayload("mid_ig_1", "igsid_1", null, "price?", now));

        Assert.Equal("v1", envelope.SchemaVersion);
        Assert.Equal(ChannelType.Instagram, envelope.Channel);
        Assert.Equal("tenant_kreyora_1", envelope.TenantId);
    }

    [Fact]
    public void InstagramWindow_AllowsSendInsideWindow_BlocksBeyondHumanAgentExtension()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(
            InstagramWindowState.WindowOpen,
            InstagramWindowEvaluator.Evaluate(now.AddHours(-2), now));
        Assert.Equal(
            InstagramWindowState.WindowExpiredHumanAgentEligible,
            InstagramWindowEvaluator.Evaluate(now.AddDays(-3), now));
        Assert.Equal(
            InstagramWindowState.WindowExhausted,
            InstagramWindowEvaluator.Evaluate(now.AddDays(-8), now));
    }
}
