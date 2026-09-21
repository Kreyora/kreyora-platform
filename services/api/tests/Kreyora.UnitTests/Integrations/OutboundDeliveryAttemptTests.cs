using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class OutboundDeliveryAttemptTests
{
    private const string TenantId = "tenant_test_123";
    private const string MessageId = "msg_out_test_456";
    private const string ConnectionId = "conn_test_789";

    [Fact]
    public void Create_WithValidArgs_Succeeds()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempt = OutboundDeliveryAttempt.Create(
            TenantId,
            MessageId,
            attemptNumber: 1,
            ChannelType.Simulator,
            ConnectionId,
            startedAt);

        Assert.Equal(TenantId, attempt.TenantId);
        Assert.Equal(MessageId, attempt.OutboundMessageId);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal(ChannelType.Simulator, attempt.Channel);
        Assert.Equal(ConnectionId, attempt.ConnectionId);
        Assert.Equal(startedAt, attempt.StartedAt);
        Assert.False(attempt.Succeeded);
        Assert.Null(attempt.CompletedAt);
        Assert.Null(attempt.ProviderMessageId);
        Assert.Null(attempt.ProviderErrorCode);
        Assert.Null(attempt.ProviderErrorMessage);
    }

    [Fact]
    public void Create_WithInvalidAttemptNumber_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboundDeliveryAttempt.Create(
            TenantId, MessageId, 0, ChannelType.Simulator, ConnectionId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CompleteSuccess_SetsPropertiesCorrectly()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempt = OutboundDeliveryAttempt.Create(
            TenantId, MessageId, 1, ChannelType.Simulator, ConnectionId, startedAt);

        var completedAt = startedAt.AddMilliseconds(250);
        attempt.CompleteSuccess("prov_msg_999", completedAt);

        Assert.True(attempt.Succeeded);
        Assert.Equal(completedAt, attempt.CompletedAt);
        Assert.Equal("prov_msg_999", attempt.ProviderMessageId);
        Assert.Null(attempt.ProviderErrorCode);
        Assert.Null(attempt.ProviderErrorMessage);
    }

    [Fact]
    public void CompleteSuccess_WithCompletedAtBeforeStartedAt_ThrowsArgumentException()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempt = OutboundDeliveryAttempt.Create(
            TenantId, MessageId, 1, ChannelType.Simulator, ConnectionId, startedAt);

        Assert.Throws<ArgumentException>(() =>
            attempt.CompleteSuccess("prov_msg_1", startedAt.AddSeconds(-1)));
    }

    [Fact]
    public void CompleteFailure_SetsPropertiesCorrectly()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempt = OutboundDeliveryAttempt.Create(
            TenantId, MessageId, 1, ChannelType.Simulator, ConnectionId, startedAt);

        var completedAt = startedAt.AddMilliseconds(500);
        attempt.CompleteFailure("HTTP_429", "Rate limit exceeded", completedAt);

        Assert.False(attempt.Succeeded);
        Assert.Equal(completedAt, attempt.CompletedAt);
        Assert.Equal("HTTP_429", attempt.ProviderErrorCode);
        Assert.Equal("Rate limit exceeded", attempt.ProviderErrorMessage);
        Assert.Null(attempt.ProviderMessageId);
    }

    [Fact]
    public void CompleteFailure_WithCompletedAtBeforeStartedAt_ThrowsArgumentException()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempt = OutboundDeliveryAttempt.Create(
            TenantId, MessageId, 1, ChannelType.Simulator, ConnectionId, startedAt);

        Assert.Throws<ArgumentException>(() =>
            attempt.CompleteFailure("ERR", "Error", startedAt.AddSeconds(-5)));
    }
}

