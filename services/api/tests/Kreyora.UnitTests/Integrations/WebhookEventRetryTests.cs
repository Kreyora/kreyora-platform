using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class WebhookEventRetryTests
{
    [Fact]
    public void RecordFailure_TransientUnderMaxAttempts_TransitionsToFailedWithNextRetry()
    {
        var ev = CreateTestEvent(maxAttempts: 3);
        var now = DateTimeOffset.UtcNow;
        var policy = new WebhookRetryPolicy(maxAttempts: 3);

        ev.RecordFailure("Connection timed out", WebhookFailureClassification.Transient, now, policy);

        Assert.Equal(WebhookProcessingStatus.Failed, ev.ProcessingStatus);
        Assert.Equal(WebhookFailureClassification.Transient, ev.FailureClassification);
        Assert.Equal(1, ev.AttemptCount);
        Assert.NotNull(ev.NextRetryAt);
        Assert.True(ev.NextRetryAt > now);
        Assert.Null(ev.DeadLetteredAt);
        Assert.Equal("Connection timed out", ev.ErrorMessage);
        Assert.Equal(now, ev.LastAttemptedAt);
    }

    [Fact]
    public void RecordFailure_TransientExhaustingMaxAttempts_TransitionsToDeadLetter()
    {
        var ev = CreateTestEvent(maxAttempts: 2);
        var now = DateTimeOffset.UtcNow;
        var policy = new WebhookRetryPolicy(maxAttempts: 2);

        ev.RecordFailure("First failure", WebhookFailureClassification.Transient, now, policy);
        Assert.Equal(WebhookProcessingStatus.Failed, ev.ProcessingStatus);
        Assert.Equal(1, ev.AttemptCount);

        ev.RecordFailure("Second failure", WebhookFailureClassification.Transient, now.AddMinutes(1), policy);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, ev.ProcessingStatus);
        Assert.Equal(WebhookFailureClassification.Exhausted, ev.FailureClassification);
        Assert.Equal(2, ev.AttemptCount);
        Assert.Null(ev.NextRetryAt);
        Assert.Equal(now.AddMinutes(1), ev.DeadLetteredAt);
    }

    [Fact]
    public void RecordFailure_PermanentClassification_ImmediatelyDeadLetters()
    {
        var ev = CreateTestEvent(maxAttempts: 5);
        var now = DateTimeOffset.UtcNow;
        var policy = new WebhookRetryPolicy(maxAttempts: 5);

        ev.RecordFailure("Poison event: malformed JSON", WebhookFailureClassification.Permanent, now, policy);

        Assert.Equal(WebhookProcessingStatus.DeadLetter, ev.ProcessingStatus);
        Assert.Equal(WebhookFailureClassification.Permanent, ev.FailureClassification);
        Assert.Equal(1, ev.AttemptCount);
        Assert.Null(ev.NextRetryAt);
        Assert.Equal(now, ev.DeadLetteredAt);
        Assert.Equal("Poison event: malformed JSON", ev.ErrorMessage);
    }

    [Fact]
    public void QuarantinePoison_ImmediatelyDeadLettersWithReason()
    {
        var ev = CreateTestEvent();
        var now = DateTimeOffset.UtcNow;

        ev.QuarantinePoison("Unsupported schema version: v99", now);

        Assert.Equal(WebhookProcessingStatus.DeadLetter, ev.ProcessingStatus);
        Assert.Equal(WebhookFailureClassification.Permanent, ev.FailureClassification);
        Assert.Equal(now, ev.DeadLetteredAt);
        Assert.Null(ev.NextRetryAt);
        Assert.Equal("Unsupported schema version: v99", ev.ErrorMessage);
    }

    [Fact]
    public void Replay_FromDeadLetter_ResetsStateToReceived()
    {
        var ev = CreateTestEvent();
        var now = DateTimeOffset.UtcNow;
        ev.QuarantinePoison("Corrupt payload", now);

        ev.Replay(now.AddMinutes(5));

        Assert.Equal(WebhookProcessingStatus.Received, ev.ProcessingStatus);
        Assert.Equal(0, ev.AttemptCount);
        Assert.Null(ev.NextRetryAt);
        Assert.Null(ev.DeadLetteredAt);
        Assert.Null(ev.ProcessedAt);
        Assert.Null(ev.ErrorMessage);
        Assert.Null(ev.FailureClassification);
    }

    [Fact]
    public void Replay_FromNonFailedOrDeadLetterState_ThrowsInvalidOperationException()
    {
        var ev = CreateTestEvent();
        Assert.Equal(WebhookProcessingStatus.Received, ev.ProcessingStatus);

        Assert.Throws<InvalidOperationException>(() => ev.Replay(DateTimeOffset.UtcNow));

        ev.MarkProcessed(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => ev.Replay(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RecordSuccess_ClearsErrorAndNextRetryAt()
    {
        var ev = CreateTestEvent();
        var now = DateTimeOffset.UtcNow;
        var policy = new WebhookRetryPolicy();

        ev.RecordFailure("Transient glitch", WebhookFailureClassification.Transient, now, policy);
        Assert.NotNull(ev.NextRetryAt);

        var processedAt = now.AddSeconds(30);
        ev.RecordSuccess(processedAt);

        Assert.Equal(WebhookProcessingStatus.Processed, ev.ProcessingStatus);
        Assert.Equal(processedAt, ev.ProcessedAt);
        Assert.Null(ev.ErrorMessage);
        Assert.Null(ev.NextRetryAt);
    }

    private static WebhookEvent CreateTestEvent(int maxAttempts = 5) =>
        WebhookEvent.Create(
            tenantId: "tenant_1",
            connectionId: "conn_1",
            channel: ChannelType.Simulator,
            providerEventId: "evt_test_1",
            eventType: "messages",
            occurredAt: DateTimeOffset.UtcNow,
            receivedAt: DateTimeOffset.UtcNow,
            correlationId: "corr_1",
            headers: "{}",
            rawPayload: "{}",
            maxAttempts: maxAttempts);
}

