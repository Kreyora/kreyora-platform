using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class OutboundMessageTests
{
    private const string TenantId = "tenant_test_123";
    private const string ConnectionId = "conn_test_456";
    private const string RecipientId = "+9779800000000";
    private const string IdempotencyKey = "idemp_msg_001";

    [Fact]
    public void Create_WithValidTextContent_SucceedsWithQueuedStatus()
    {
        var msg = OutboundMessage.Create(
            tenantId: TenantId,
            connectionId: ConnectionId,
            channel: ChannelType.Simulator,
            recipientChannelId: RecipientId,
            idempotencyKey: IdempotencyKey,
            messageType: OutboundMessageType.Text,
            textContent: "Namaste! Your order is ready.");

        Assert.Equal(TenantId, msg.TenantId);
        Assert.Equal(ConnectionId, msg.ConnectionId);
        Assert.Equal(ChannelType.Simulator, msg.Channel);
        Assert.Equal(RecipientId, msg.RecipientChannelId);
        Assert.Equal(IdempotencyKey, msg.IdempotencyKey);
        Assert.Equal(OutboundMessageType.Text, msg.MessageType);
        Assert.Equal("Namaste! Your order is ready.", msg.TextContent);
        Assert.Equal(OutboundMessageStatus.Queued, msg.Status);
        Assert.Equal(0, msg.AttemptCount);
        Assert.Equal(WebhookRetryPolicy.DefaultMaxAttempts, msg.MaxAttempts);
        Assert.Null(msg.NextRetryAt);
        Assert.Null(msg.SentAt);
        Assert.Null(msg.ProviderMessageId);
    }

    [Fact]
    public void Create_WithValidMediaContent_Succeeds()
    {
        var msg = OutboundMessage.Create(
            tenantId: TenantId,
            connectionId: ConnectionId,
            channel: ChannelType.WhatsApp,
            recipientChannelId: RecipientId,
            idempotencyKey: IdempotencyKey,
            messageType: OutboundMessageType.Media,
            mediaUrl: "https://example.com/receipt.pdf",
            mediaContentType: "application/pdf",
            caption: "Payment receipt");

        Assert.Equal(OutboundMessageType.Media, msg.MessageType);
        Assert.Equal("https://example.com/receipt.pdf", msg.MediaUrl);
        Assert.Equal("application/pdf", msg.MediaContentType);
        Assert.Equal("Payment receipt", msg.Caption);
    }

    [Fact]
    public void Create_WithValidTemplateContent_Succeeds()
    {
        var msg = OutboundMessage.Create(
            tenantId: TenantId,
            connectionId: ConnectionId,
            channel: ChannelType.WhatsApp,
            recipientChannelId: RecipientId,
            idempotencyKey: IdempotencyKey,
            messageType: OutboundMessageType.Template,
            templateCode: "order_dispatched_v1",
            templateParametersJson: "{\"order_id\":\"ORD-123\"}");

        Assert.Equal(OutboundMessageType.Template, msg.MessageType);
        Assert.Equal("order_dispatched_v1", msg.TemplateCode);
        Assert.Equal("{\"order_id\":\"ORD-123\"}", msg.TemplateParametersJson);
    }

    [Theory]
    [InlineData(OutboundMessageType.Text)]
    [InlineData(OutboundMessageType.LinkPreview)]
    public void Create_TextOrLinkPreviewWithoutText_ThrowsArgumentException(OutboundMessageType type)
    {
        Assert.Throws<ArgumentException>(() => OutboundMessage.Create(
            TenantId, ConnectionId, ChannelType.Simulator, RecipientId, IdempotencyKey,
            type, textContent: null));

        Assert.Throws<ArgumentException>(() => OutboundMessage.Create(
            TenantId, ConnectionId, ChannelType.Simulator, RecipientId, IdempotencyKey,
            type, textContent: "   "));
    }

    [Fact]
    public void Create_MediaWithoutUrl_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => OutboundMessage.Create(
            TenantId, ConnectionId, ChannelType.Simulator, RecipientId, IdempotencyKey,
            OutboundMessageType.Media, mediaUrl: ""));
    }

    [Fact]
    public void Create_TemplateWithoutCode_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => OutboundMessage.Create(
            TenantId, ConnectionId, ChannelType.Simulator, RecipientId, IdempotencyKey,
            OutboundMessageType.Template, templateCode: null));
    }

    [Fact]
    public void Create_WithInvalidMaxAttempts_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboundMessage.Create(
            TenantId, ConnectionId, ChannelType.Simulator, RecipientId, IdempotencyKey,
            OutboundMessageType.Text, textContent: "Hello", maxAttempts: 0));
    }

    [Fact]
    public void MarkSending_FromQueuedOrFailed_TransitionsToSendingAndIncrementsAttemptCount()
    {
        var msg = CreateSampleMessage();
        Assert.Equal(OutboundMessageStatus.Queued, msg.Status);
        Assert.Equal(0, msg.AttemptCount);

        msg.MarkSending();
        Assert.Equal(OutboundMessageStatus.Sending, msg.Status);
        Assert.Equal(1, msg.AttemptCount);

        // Fail it, then mark sending again
        var now = DateTimeOffset.UtcNow;
        msg.RecordDeliveryFailure("Network timeout", WebhookFailureClassification.Transient, now);
        Assert.Equal(OutboundMessageStatus.Failed, msg.Status);

        msg.MarkSending();
        Assert.Equal(OutboundMessageStatus.Sending, msg.Status);
        Assert.Equal(2, msg.AttemptCount);
    }

    [Fact]
    public void MarkSending_FromInvalidStatus_ThrowsInvalidOperationException()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        msg.RecordDeliverySuccess("prov_msg_1", DateTimeOffset.UtcNow);
        Assert.Equal(OutboundMessageStatus.Sent, msg.Status);

        Assert.Throws<InvalidOperationException>(() => msg.MarkSending());
    }

    [Fact]
    public void RecordDeliverySuccess_FromSending_TransitionsToSent()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        var sentAt = DateTimeOffset.UtcNow;

        msg.RecordDeliverySuccess("prov_msg_abc", sentAt);

        Assert.Equal(OutboundMessageStatus.Sent, msg.Status);
        Assert.Equal("prov_msg_abc", msg.ProviderMessageId);
        Assert.Equal(sentAt, msg.SentAt);
        Assert.Null(msg.NextRetryAt);
        Assert.Null(msg.LastErrorMessage);
        Assert.Null(msg.FailureClassification);
    }

    [Fact]
    public void RecordDeliverySuccess_FromNonSending_ThrowsInvalidOperationException()
    {
        var msg = CreateSampleMessage();
        Assert.Throws<InvalidOperationException>(() =>
            msg.RecordDeliverySuccess("prov_1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RecordDeliveryFailure_TransientBelowMaxAttempts_TransitionsToFailedAndSchedulesRetry()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        var now = DateTimeOffset.UtcNow;

        msg.RecordDeliveryFailure("Rate limit 429", WebhookFailureClassification.Transient, now);

        Assert.Equal(OutboundMessageStatus.Failed, msg.Status);
        Assert.Equal(WebhookFailureClassification.Transient, msg.FailureClassification);
        Assert.Equal("Rate limit 429", msg.LastErrorMessage);
        Assert.NotNull(msg.NextRetryAt);
        Assert.True(msg.NextRetryAt > now);
        Assert.Null(msg.DeadLetteredAt);
    }

    [Fact]
    public void RecordDeliveryFailure_TransientReachingMaxAttempts_TransitionsToDeadLetter()
    {
        var msg = OutboundMessage.Create(
            TenantId, ConnectionId, ChannelType.Simulator, RecipientId, IdempotencyKey,
            OutboundMessageType.Text, textContent: "Hello", maxAttempts: 2);

        var now = DateTimeOffset.UtcNow;

        // Attempt 1 -> Failed
        msg.MarkSending();
        msg.RecordDeliveryFailure("Error 1", WebhookFailureClassification.Transient, now);
        Assert.Equal(OutboundMessageStatus.Failed, msg.Status);

        // Attempt 2 -> DeadLetter (exhausted)
        msg.MarkSending();
        msg.RecordDeliveryFailure("Error 2", WebhookFailureClassification.Transient, now);
        Assert.Equal(OutboundMessageStatus.DeadLetter, msg.Status);
        Assert.Equal(WebhookFailureClassification.Exhausted, msg.FailureClassification);
        Assert.Equal(now, msg.DeadLetteredAt);
        Assert.Null(msg.NextRetryAt);
    }

    [Fact]
    public void RecordDeliveryFailure_Permanent_TransitionsImmediatelyToDeadLetter()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        var now = DateTimeOffset.UtcNow;

        msg.RecordDeliveryFailure("Recipient number does not exist", WebhookFailureClassification.Permanent, now);

        Assert.Equal(OutboundMessageStatus.DeadLetter, msg.Status);
        Assert.Equal(WebhookFailureClassification.Permanent, msg.FailureClassification);
        Assert.Equal(now, msg.DeadLetteredAt);
        Assert.Null(msg.NextRetryAt);
    }

    [Fact]
    public void UpdateProviderStatus_MonotonicProgression_SentToDeliveredToRead()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        var sentAt = DateTimeOffset.UtcNow;
        msg.RecordDeliverySuccess("prov_123", sentAt);

        var deliveredAt = sentAt.AddSeconds(5);
        msg.UpdateProviderStatus(MessageDeliveryStatus.Delivered, deliveredAt);
        Assert.Equal(OutboundMessageStatus.Delivered, msg.Status);
        Assert.Equal(deliveredAt, msg.DeliveredAt);

        var readAt = deliveredAt.AddSeconds(10);
        msg.UpdateProviderStatus(MessageDeliveryStatus.Read, readAt);
        Assert.Equal(OutboundMessageStatus.Read, msg.Status);
        Assert.Equal(readAt, msg.ReadAt);
        Assert.Equal(deliveredAt, msg.DeliveredAt);

        // Deliberate regression attempt: receipt for Delivered after already Read
        msg.UpdateProviderStatus(MessageDeliveryStatus.Delivered, deliveredAt.AddSeconds(15));
        Assert.Equal(OutboundMessageStatus.Read, msg.Status); // Status stays Read!
    }

    [Fact]
    public void UpdateProviderStatus_DirectToRead_BackfillsDeliveredAt()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        msg.RecordDeliverySuccess("prov_123", DateTimeOffset.UtcNow);

        var readAt = DateTimeOffset.UtcNow.AddSeconds(5);
        msg.UpdateProviderStatus(MessageDeliveryStatus.Read, readAt);

        Assert.Equal(OutboundMessageStatus.Read, msg.Status);
        Assert.Equal(readAt, msg.ReadAt);
        Assert.Equal(readAt, msg.DeliveredAt); // Backfilled!
    }

    [Fact]
    public void Cancel_FromQueuedOrFailed_Succeeds()
    {
        var msg = CreateSampleMessage();
        var now = DateTimeOffset.UtcNow;

        msg.Cancel(now);

        Assert.Equal(OutboundMessageStatus.Cancelled, msg.Status);
        Assert.Equal(now, msg.CancelledAt);

        // Cannot cancel already cancelled
        Assert.Throws<InvalidOperationException>(() => msg.Cancel(now));
    }

    [Fact]
    public void Cancel_FromSentOrDelivered_ThrowsInvalidOperationException()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        msg.RecordDeliverySuccess("prov_1", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => msg.Cancel(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Replay_FromDeadLetter_ResetsToQueued()
    {
        var msg = CreateSampleMessage();
        msg.MarkSending();
        var now = DateTimeOffset.UtcNow;
        msg.RecordDeliveryFailure("Fatal", WebhookFailureClassification.Permanent, now);
        Assert.Equal(OutboundMessageStatus.DeadLetter, msg.Status);

        var replayAt = now.AddMinutes(5);
        msg.Replay(replayAt, newMaxAttempts: 3);

        Assert.Equal(OutboundMessageStatus.Queued, msg.Status);
        Assert.Equal(0, msg.AttemptCount);
        Assert.Equal(3, msg.MaxAttempts);
        Assert.Null(msg.NextRetryAt);
        Assert.Null(msg.DeadLetteredAt);
        Assert.Null(msg.FailedAt);
        Assert.Null(msg.FailureClassification);
        Assert.Null(msg.LastErrorMessage);
        Assert.Equal(replayAt, msg.QueuedAt);
    }

    [Fact]
    public void Replay_FromQueuedOrSent_ThrowsInvalidOperationException()
    {
        var msg = CreateSampleMessage();
        Assert.Throws<InvalidOperationException>(() => msg.Replay(DateTimeOffset.UtcNow));
    }

    private static OutboundMessage CreateSampleMessage() =>
        OutboundMessage.Create(
            TenantId,
            ConnectionId,
            ChannelType.Simulator,
            RecipientId,
            IdempotencyKey,
            OutboundMessageType.Text,
            textContent: "Sample text message");
}

