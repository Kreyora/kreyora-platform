using Kreyora.Domain.Notifications;

namespace Kreyora.UnitTests.Domain;

public sealed class NotificationRequestTests
{
    private readonly string tenantId = "01J00000000000000000000001";
    private readonly string sourceEventId = "01J00000000000000000000002";

    [Fact]
    public void Create_ValidInput_InitializesPendingNotification()
    {
        var request = NotificationRequest.Create(
            tenantId,
            "order.confirmed.v1",
            sourceEventId,
            "order_confirmed",
            1,
            NotificationChannel.Email,
            "customer@example.com",
            "Hari Thapa");

        Assert.Equal(tenantId, request.TenantId);
        Assert.Equal("order.confirmed.v1", request.SourceEventType);
        Assert.Equal(sourceEventId, request.SourceEventId);
        Assert.Equal("order_confirmed", request.TemplateCode);
        Assert.Equal(1, request.TemplateVersion);
        Assert.Equal(NotificationChannel.Email, request.Channel);
        Assert.Equal("customer@example.com", request.RecipientContact);
        Assert.Equal("Hari Thapa", request.RecipientName);
        Assert.Equal(NotificationStatus.Pending, request.Status);
        Assert.Equal(3, request.MaxAttempts);
        Assert.Equal(0, request.AttemptCount);
        Assert.Null(request.NextRetryAt);
        Assert.Null(request.DeliveredAt);
        Assert.Null(request.DeadLetteredAt);
        Assert.Equal($"{sourceEventId}:order_confirmed:1", request.IdempotencyKey);
    }

    [Fact]
    public void MarkDelivering_IncrementsAttempts_AndSetsStatus()
    {
        var request = CreateValid();
        var now = DateTimeOffset.UtcNow;

        request.MarkDelivering(now);

        Assert.Equal(NotificationStatus.Delivering, request.Status);
        Assert.Equal(1, request.AttemptCount);
        Assert.Null(request.NextRetryAt);
    }

    [Fact]
    public void RecordSuccess_SetsDeliveredStatus_AndTimestamp()
    {
        var request = CreateValid();
        var now = DateTimeOffset.UtcNow;
        request.MarkDelivering(now);

        request.RecordSuccess("smtp-msg-12345", now.AddSeconds(2));

        Assert.Equal(NotificationStatus.Delivered, request.Status);
        Assert.Equal(now.AddSeconds(2), request.DeliveredAt);
        Assert.Null(request.LastRedactedError);
    }

    [Fact]
    public void RecordFailure_AttemptsRemain_SetsFailedAndNextRetry()
    {
        var request = CreateValid(maxAttempts: 3);
        var policy = new NotificationRetryPolicy();
        var now = DateTimeOffset.UtcNow;

        request.MarkDelivering(now);
        request.RecordFailure("Connection timed out", now, policy);

        Assert.Equal(NotificationStatus.Failed, request.Status);
        Assert.Equal("Connection timed out", request.LastRedactedError);
        Assert.Equal(now.AddMinutes(1), request.NextRetryAt);
        Assert.Null(request.DeadLetteredAt);
    }

    [Fact]
    public void RecordFailure_MaxAttemptsReached_SetsDeadLettered()
    {
        var request = CreateValid(maxAttempts: 2);
        var policy = new NotificationRetryPolicy();
        var now = DateTimeOffset.UtcNow;

        // Attempt 1 -> Failed
        request.MarkDelivering(now);
        request.RecordFailure("First failure", now, policy);
        Assert.Equal(NotificationStatus.Failed, request.Status);

        // Attempt 2 -> DeadLettered
        request.MarkDelivering(now.AddMinutes(1));
        request.RecordFailure("Second failure", now.AddMinutes(1), policy);

        Assert.Equal(NotificationStatus.DeadLettered, request.Status);
        Assert.Equal("Second failure", request.LastRedactedError);
        Assert.Equal(now.AddMinutes(1), request.DeadLetteredAt);
        Assert.Null(request.NextRetryAt);
    }

    [Fact]
    public void Replay_FromDeadLettered_ResetsToPending()
    {
        var request = CreateValid(maxAttempts: 1);
        var policy = new NotificationRetryPolicy();
        var now = DateTimeOffset.UtcNow;

        request.MarkDelivering(now);
        request.RecordFailure("Terminal failure", now, policy);
        Assert.Equal(NotificationStatus.DeadLettered, request.Status);

        request.Replay(now.AddHours(1));

        Assert.Equal(NotificationStatus.Pending, request.Status);
        Assert.Equal(0, request.AttemptCount);
        Assert.Null(request.DeadLetteredAt);
        Assert.Null(request.LastRedactedError);
        Assert.Null(request.NextRetryAt);
    }

    [Fact]
    public void InvalidTransitions_ThrowInvalidOperationException()
    {
        var request = CreateValid();
        var now = DateTimeOffset.UtcNow;

        // Cannot RecordSuccess directly from Pending
        Assert.Throws<InvalidOperationException>(() => request.RecordSuccess("ref", now));

        // Cannot RecordFailure directly from Pending
        Assert.Throws<InvalidOperationException>(() => request.RecordFailure("err", now, new NotificationRetryPolicy()));

        // Cannot Replay from Pending
        Assert.Throws<InvalidOperationException>(() => request.Replay(now));

        // Mark delivering
        request.MarkDelivering(now);

        // Cannot Replay from Delivering
        Assert.Throws<InvalidOperationException>(() => request.Replay(now));

        // Record success
        request.RecordSuccess("ref", now);

        // Cannot MarkDelivering from Delivered
        Assert.Throws<InvalidOperationException>(() => request.MarkDelivering(now));

        // Cannot Replay from Delivered
        Assert.Throws<InvalidOperationException>(() => request.Replay(now));
    }

    [Fact]
    public void NotificationRetryPolicy_CalculatesBackoffAccurately()
    {
        var policy = new NotificationRetryPolicy();

        Assert.Equal(TimeSpan.FromMinutes(1), policy.GetBackoffForAttempt(1));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetBackoffForAttempt(2));
        Assert.Equal(TimeSpan.FromMinutes(15), policy.GetBackoffForAttempt(3));
        Assert.Equal(TimeSpan.FromMinutes(15), policy.GetBackoffForAttempt(4)); // Capped at last interval
    }

    private NotificationRequest CreateValid(int maxAttempts = 3) =>
        NotificationRequest.Create(
            tenantId,
            "order.confirmed.v1",
            sourceEventId,
            "order_confirmed",
            1,
            NotificationChannel.Email,
            "customer@example.com",
            "Hari Thapa",
            maxAttempts);
}

