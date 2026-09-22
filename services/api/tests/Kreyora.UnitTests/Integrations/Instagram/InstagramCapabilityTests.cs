using Kreyora.Application.Integrations.Instagram;
using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations.Instagram;

public sealed class InstagramCapabilityTests
{
    [Fact]
    public void InstagramGraphApi_MatchesEvidencedCapabilities()
    {
        var caps = ChannelCapabilities.InstagramGraphApi();

        Assert.True(caps.CanReceiveText);
        Assert.True(caps.CanReceiveMedia);
        Assert.True(caps.CanSendText);
        Assert.True(caps.CanSendMedia);
        Assert.True(caps.RequiresSignatureVerification);
        Assert.True(caps.Enforces24HourWindow);
        Assert.True(caps.SupportsReadReceipts);
        Assert.False(caps.SupportsDeliveryReceipts);
        Assert.False(caps.RequiresTemplatesOutsideWindow);
        Assert.Equal(caps, ChannelCapabilities.ForChannel(ChannelType.Instagram));
    }

    [Fact]
    public void PermissionSet_Required_IsComplete()
    {
        var permissions = InstagramPermissionSet.Required();

        Assert.True(permissions.IsComplete);
        Assert.False(new InstagramPermissionSet(true, true, false).IsComplete);
    }

    [Fact]
    public void CredentialDescriptor_ExposesNoSecrets_AndTracksReadiness()
    {
        var descriptor = new InstagramCredentialDescriptor(
            PageId: "page_123",
            InstagramAccountId: "igsid_456",
            Permissions: InstagramPermissionSet.Required(),
            UsesLongLivedPageToken: true,
            HasKeyVersion: true);

        Assert.True(descriptor.IsReadyForMessaging);
        Assert.False((descriptor with { HasKeyVersion = false }).IsReadyForMessaging);

        var secretLike = typeof(InstagramCredentialDescriptor)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string)
                && (p.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || p.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || p.Name.Contains("password", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Assert.Empty(secretLike);
    }

    [Theory]
    [InlineData(1, InstagramWindowState.WindowOpen)]
    [InlineData(23, InstagramWindowState.WindowOpen)]
    [InlineData(25, InstagramWindowState.WindowExpiredHumanAgentEligible)]
    [InlineData(167, InstagramWindowState.WindowExpiredHumanAgentEligible)]
    [InlineData(169, InstagramWindowState.WindowExhausted)]
    public void WindowEvaluator_MapsElapsedHours_ToDocumentedStates(
        int elapsedHours,
        InstagramWindowState expected)
    {
        var now = DateTimeOffset.UtcNow;

        var actual = InstagramWindowEvaluator.Evaluate(now.AddHours(-elapsedHours), now);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WindowEvaluator_NullOrFutureTimestamp_ReturnsUnknown()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(InstagramWindowState.Unknown, InstagramWindowEvaluator.Evaluate(null, now));
        Assert.Equal(InstagramWindowState.Unknown, InstagramWindowEvaluator.Evaluate(now.AddHours(1), now));
    }

    [Fact]
    public void Restrictions_DocumentDeliveryReceiptGap_WithFallback()
    {
        var restrictions = InstagramCapabilityRestriction.Documented();

        Assert.Equal(4, restrictions.Count);
        var receipts = restrictions.Single(r => r.Capability == "Delivery receipts");
        Assert.Contains("Read", receipts.FallbackUx);
        Assert.All(restrictions, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.FallbackUx));
            Assert.False(string.IsNullOrWhiteSpace(r.Evidence));
        });
    }
}
