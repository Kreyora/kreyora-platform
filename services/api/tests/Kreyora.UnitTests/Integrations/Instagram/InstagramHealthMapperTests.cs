using Kreyora.Application.Integrations.Instagram;
using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations.Instagram;

public sealed class InstagramHealthMapperTests
{
    [Theory]
    [InlineData(InstagramValidationKind.Valid, true, ChannelConnectionStatus.Active)]
    [InlineData(InstagramValidationKind.TokenExpired, false, ChannelConnectionStatus.Expired)]
    [InlineData(InstagramValidationKind.PermissionDenied, false, ChannelConnectionStatus.Revoked)]
    [InlineData(InstagramValidationKind.IdentityMismatch, false, ChannelConnectionStatus.Degraded)]
    [InlineData(InstagramValidationKind.Throttled, false, ChannelConnectionStatus.Degraded)]
    [InlineData(InstagramValidationKind.Transient, false, ChannelConnectionStatus.Degraded)]
    [InlineData(InstagramValidationKind.ProviderError, false, ChannelConnectionStatus.Degraded)]
    [InlineData(InstagramValidationKind.Unknown, false, ChannelConnectionStatus.Degraded)]
    public void ToHealth_MapsEveryKind(
        InstagramValidationKind kind,
        bool expectedHealthy,
        ChannelConnectionStatus expectedStatus)
    {
        var result = kind == InstagramValidationKind.Valid
            ? InstagramValidationResult.Valid("test.shop")
            : InstagramValidationResult.Failed(kind, "code", "message");

        var (isHealthy, status, summary, _) = InstagramHealthMapper.ToHealth(result, DateTimeOffset.UtcNow);

        Assert.Equal(expectedHealthy, isHealthy);
        Assert.Equal(expectedStatus, status);
        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    [Fact]
    public void ToHealth_Valid_IncludesUsernameInDetails()
    {
        var (_, _, _, details) = InstagramHealthMapper.ToHealth(
            InstagramValidationResult.Valid("test.shop"), DateTimeOffset.UtcNow);

        Assert.Contains("test.shop", details);
    }
}
