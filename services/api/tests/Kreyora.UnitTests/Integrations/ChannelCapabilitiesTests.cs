using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class ChannelCapabilitiesTests
{
    [Theory]
    [InlineData(ChannelType.WhatsApp, true, true, true, true)]
    [InlineData(ChannelType.Instagram, false, false, true, true)]
    [InlineData(ChannelType.Messenger, false, true, true, true)]
    [InlineData(ChannelType.Viber, false, true, false, false)]
    [InlineData(ChannelType.Telegram, false, false, false, false)]
    [InlineData(ChannelType.Simulator, true, true, true, true)]
    public void ForChannel_ReturnsExpectedCapabilities(
        ChannelType channel,
        bool expectedRequiresTemplatesOutsideWindow,
        bool expectedSupportsDeliveryReceipts,
        bool expectedEnforces24HourWindow,
        bool expectedSupportsTokenRefresh)
    {
        var caps = ChannelCapabilities.ForChannel(channel);

        Assert.NotNull(caps);
        Assert.True(caps.CanReceiveText);
        Assert.True(caps.CanReceiveMedia);
        Assert.True(caps.CanSendText);
        Assert.True(caps.CanSendMedia);
        Assert.True(caps.CanSendLinkPreview);
        Assert.True(caps.RequiresSignatureVerification);

        Assert.Equal(expectedRequiresTemplatesOutsideWindow, caps.RequiresTemplatesOutsideWindow);
        Assert.Equal(expectedSupportsDeliveryReceipts, caps.SupportsDeliveryReceipts);
        Assert.Equal(expectedEnforces24HourWindow, caps.Enforces24HourWindow);
        Assert.Equal(expectedSupportsTokenRefresh, caps.SupportsTokenRefresh);
    }

    [Fact]
    public void ForChannel_WithInvalidChannel_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChannelCapabilities.ForChannel((ChannelType)999));
    }
}
