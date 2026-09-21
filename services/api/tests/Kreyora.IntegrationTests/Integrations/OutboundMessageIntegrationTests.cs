using System.Text.Json;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Simulator;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kreyora.IntegrationTests.Integrations;

public sealed class OutboundMessageIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public OutboundMessageIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task QueueAndDeliver_Success_MessageTransitionsToSentAndCreatesAttempt()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-success");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-1");

        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000001",
                IdempotencyKey: "idemp_send_1",
                MessageType: OutboundMessageType.Text,
                TextContent: "Namaste! Your order is on the way."));

            Assert.True(queueResult.Succeeded);
            Assert.NotNull(queueResult.OutboundMessageId);
            Assert.Equal(OutboundMessageStatus.Queued, queueResult.Status);

            // Execute delivery
            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId);

            // Verify message updated to Sent
            var msg = await service.GetMessageAsync(queueResult.OutboundMessageId);
            Assert.NotNull(msg);
            Assert.Equal(OutboundMessageStatus.Sent, msg.Status);
            Assert.NotNull(msg.ProviderMessageId);
            Assert.NotNull(msg.SentAt);
            Assert.Null(msg.LastErrorMessage);

            // Verify delivery attempt recorded
            var attempts = await service.GetDeliveryAttemptsAsync(queueResult.OutboundMessageId);
            Assert.Single(attempts);
            Assert.True(attempts[0].Succeeded);
            Assert.Equal(msg.ProviderMessageId, attempts[0].ProviderMessageId);
        }
    }

    [Fact]
    public async Task QueueMessage_IdempotentDuplicate_ReturnsExistingMessageWithoutDuplicateRow()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-idemp");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-idemp");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var req = new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000002",
                IdempotencyKey: "same_key_123",
                MessageType: OutboundMessageType.Text,
                TextContent: "Order confirmed.");

            var res1 = await service.QueueMessageAsync(req);
            Assert.True(res1.Succeeded);
            Assert.False(res1.IsIdempotentDuplicate);

            var res2 = await service.QueueMessageAsync(req);
            Assert.True(res2.Succeeded);
            Assert.True(res2.IsIdempotentDuplicate);
            Assert.Equal(res1.OutboundMessageId, res2.OutboundMessageId);

            var totalCount = await db.OutboundMessages.CountAsync(m => m.ConnectionId == connection.Id);
            Assert.Equal(1, totalCount);
        }
    }

    [Fact]
    public async Task QueueMessage_UnsupportedCapability_FailsValidation()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-cap");

        // Create connection with Text only (CanSendMedia = false)
        var caps = new ChannelCapabilities(
            CanReceiveText: true,
            CanReceiveMedia: false,
            CanSendText: true,
            CanSendMedia: false,
            CanSendLinkPreview: false,
            RequiresTemplatesOutsideWindow: false,
            SupportsReactions: false,
            SupportsDeliveryReceipts: false,
            SupportsReadReceipts: false,
            Enforces24HourWindow: false,
            SupportsTokenRefresh: false,
            RequiresSignatureVerification: false);

        var connection = await CreateConnectionWithCapabilitiesAsync(db, accessor, tenant.Id, "conn-text-only", caps);
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var req = new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000003",
                IdempotencyKey: "media_key_1",
                MessageType: OutboundMessageType.Media,
                MediaUrl: "https://example.com/photo.jpg");

            var result = await service.QueueMessageAsync(req);
            Assert.False(result.Succeeded);
            Assert.Contains("does not support sending media", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task ProcessDelivery_TransientFailure_SchedulesRetryAndRecordsFailedAttempt()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-transient");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-trans");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000004",
                IdempotencyKey: "transient_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Testing [SIMULATE_TRANSIENT] retry."));

            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);

            var msg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.NotNull(msg);
            Assert.Equal(OutboundMessageStatus.Failed, msg.Status);
            Assert.Equal(WebhookFailureClassification.Transient, msg.FailureClassification);
            Assert.NotNull(msg.NextRetryAt);
            Assert.Equal(1, msg.AttemptCount);

            var attempts = await service.GetDeliveryAttemptsAsync(queueResult.OutboundMessageId!);
            Assert.Single(attempts);
            Assert.False(attempts[0].Succeeded);
            Assert.Contains("transient", attempts[0].ProviderErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ProcessDelivery_RateLimit429_ClassifiesAsTransientAndSchedulesRetry()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-ratelimit");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-429");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000005",
                IdempotencyKey: "ratelimit_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Testing [SIMULATE_RATE_LIMIT] rate limit."));

            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);

            var msg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.NotNull(msg);
            Assert.Equal(OutboundMessageStatus.Failed, msg.Status);
            Assert.Equal(WebhookFailureClassification.Transient, msg.FailureClassification);
            Assert.NotNull(msg.NextRetryAt);
        }
    }

    [Fact]
    public async Task ProcessDelivery_PermanentFailure_TransitionsImmediatelyToDeadLetter()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-perm");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-perm");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000006",
                IdempotencyKey: "perm_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Testing [SIMULATE_PERMANENT] failure."));

            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);

            var msg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.NotNull(msg);
            Assert.Equal(OutboundMessageStatus.DeadLetter, msg.Status);
            Assert.Equal(WebhookFailureClassification.Permanent, msg.FailureClassification);
            Assert.Null(msg.NextRetryAt);
            Assert.NotNull(msg.DeadLetteredAt);

            // Visible in DLQ
            var dlq = await service.GetDeadLetterMessagesAsync(new OutboundDeadLetterQuery());
            Assert.Contains(dlq.Items, d => d.Id == msg.Id);
        }
    }

    [Fact]
    public async Task ProcessDelivery_RetryExhaustion_TransitionsToDeadLetterWithExhausted()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-exhaust");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-exh");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000010",
                IdempotencyKey: "exhaust_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Testing [SIMULATE_TRANSIENT] retry exhaustion.",
                MaxAttempts: 2));

            // Attempt 1 -> Failed
            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);
            var msg1 = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.Failed, msg1!.Status);
            Assert.Equal(1, msg1.AttemptCount);

            // Attempt 2 -> DeadLetter (Exhausted)
            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);
            var msg2 = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.DeadLetter, msg2!.Status);
            Assert.Equal(WebhookFailureClassification.Exhausted, msg2.FailureClassification);
            Assert.Equal(2, msg2.AttemptCount);
            Assert.NotNull(msg2.DeadLetteredAt);
            Assert.Null(msg2.NextRetryAt);
        }
    }

    [Fact]
    public async Task CancelMessage_QueuedMessage_TransitionsToCancelled()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-cancel");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-cancel");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000007",
                IdempotencyKey: "cancel_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Message to be cancelled."));

            var cancelResult = await service.CancelMessageAsync(queueResult.OutboundMessageId!);
            Assert.True(cancelResult.Succeeded);
            Assert.Equal(OutboundMessageStatus.Cancelled, cancelResult.Status);

            var msg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.NotNull(msg);
            Assert.Equal(OutboundMessageStatus.Cancelled, msg.Status);
            Assert.NotNull(msg.CancelledAt);
        }
    }

    [Fact]
    public async Task CancelMessage_SentMessage_Fails()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-cancel-sent");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-cs");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000008",
                IdempotencyKey: "cancel_sent_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Already sent message."));

            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);

            var cancelResult = await service.CancelMessageAsync(queueResult.OutboundMessageId!);
            Assert.False(cancelResult.Succeeded);
            Assert.Contains("Cannot cancel message in status 'Sent'", cancelResult.ErrorMessage);
        }
    }

    [Fact]
    public async Task ReplayMessage_DeadLetteredMessage_ResetsToQueued()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-replay");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-replay");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000009",
                IdempotencyKey: "replay_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Test [SIMULATE_PERMANENT] DLQ."));

            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);

            var dlqMsg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.DeadLetter, dlqMsg!.Status);

            // Replay with idempotency key
            var replayResult = await service.ReplayMessageAsync(queueResult.OutboundMessageId!, "replay_idemp_1");
            Assert.True(replayResult.Succeeded);
            Assert.Equal(OutboundMessageStatus.Queued, replayResult.Status);

            var replayedMsg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.Queued, replayedMsg!.Status);
            Assert.Equal(0, replayedMsg.AttemptCount);
            Assert.Null(replayedMsg.DeadLetteredAt);
            Assert.Null(replayedMsg.LastErrorMessage);
        }
    }

    [Fact]
    public async Task ProcessStatusReceipt_UpdatesOutboundMessageStatusMonotonically()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "out-receipt");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "conn-out-rcpt");
        var service = CreateOutboundService(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var queueResult = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connection.Id,
                RecipientChannelId: "+9779800000100",
                IdempotencyKey: "receipt_test_key",
                MessageType: OutboundMessageType.Text,
                TextContent: "Track my status."));

            await service.ProcessDeliveryAsync(queueResult.OutboundMessageId!);

            var sentMsg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.Sent, sentMsg!.Status);
            var provMsgId = sentMsg.ProviderMessageId!;

            // Receive Delivered receipt
            var deliveredTime = DateTimeOffset.UtcNow.AddSeconds(5);
            await service.ProcessStatusReceiptAsync(connection.Id, provMsgId, MessageDeliveryStatus.Delivered, deliveredTime);

            var delMsg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.Delivered, delMsg!.Status);
            Assert.Equal(deliveredTime, delMsg.DeliveredAt);

            // Receive Read receipt
            var readTime = deliveredTime.AddSeconds(10);
            await service.ProcessStatusReceiptAsync(connection.Id, provMsgId, MessageDeliveryStatus.Read, readTime);

            var readMsg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.Read, readMsg!.Status);
            Assert.Equal(readTime, readMsg.ReadAt);

            // Attempt regression: stale Delivered receipt arrives after Read
            await service.ProcessStatusReceiptAsync(connection.Id, provMsgId, MessageDeliveryStatus.Delivered, deliveredTime);
            var stillReadMsg = await service.GetMessageAsync(queueResult.OutboundMessageId!);
            Assert.Equal(OutboundMessageStatus.Read, stillReadMsg!.Status); // Monotonic!
        }
    }

    [Fact]
    public async Task CrossTenantIsolation_TenantACannotAccessTenantBMessages()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenantA = await CreateTenantAsync(db, "out-iso-a");
        var tenantB = await CreateTenantAsync(db, "out-iso-b");

        var connA = await CreateConnectionAsync(db, accessor, tenantA.Id, "conn-a");
        var service = CreateOutboundService(db, accessor);

        string messageId;
        using (accessor.BeginScope(new TenantContext(tenantA.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var res = await service.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connA.Id,
                RecipientChannelId: "+9779800000200",
                IdempotencyKey: "iso_key_1",
                MessageType: OutboundMessageType.Text,
                TextContent: "Secret order info for Tenant A"));

            messageId = res.OutboundMessageId!;
        }

        // Now attempt to read and list as Tenant B
        using (accessor.BeginScope(new TenantContext(tenantB.Id, "01J00000000000000000000002", null, TenantRole.Owner)))
        {
            var readMsg = await service.GetMessageAsync(messageId);
            Assert.Null(readMsg);

            var list = await service.GetMessagesAsync(new OutboundMessageQuery());
            Assert.DoesNotContain(list.Items, m => m.Id == messageId);

            var cancelResult = await service.CancelMessageAsync(messageId);
            Assert.False(cancelResult.Succeeded);
            Assert.Contains("not found", cancelResult.ErrorMessage);
        }
    }

    // Helper methods

    private static OutboundMessageService CreateOutboundService(AppDbContext db, ITenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("test-corr"), authorizer);
        var registry = new ChannelProviderRegistry([new SimulatorChannelProvider()]);
        var gate = new AlwaysAllowConversationGate();
        var time = new SystemTimeProvider();
        var logger = new NullLogger<OutboundMessageService>();

        return new OutboundMessageService(
            db,
            accessor,
            authorizer,
            audit,
            registry,
            gate,
            time,
            logger);
    }

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string name)
    {
        var tenant = Tenant.Create(name, $"{name}-{Guid.NewGuid():N}"[..Math.Min(40, name.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<ChannelConnection> CreateConnectionAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        string externalAccountId)
    {
        using (accessor.BeginScope(new TenantContext(tenantId, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var conn = ChannelConnection.Create(
                tenantId: tenantId,
                storeId: null,
                channel: ChannelType.Simulator,
                externalAccountId: externalAccountId,
                displayName: "Simulator Connection",
                encryptedCredentials: null,
                capabilities: ChannelCapabilities.ForChannel(ChannelType.Simulator));

            db.ChannelConnections.Add(conn);
            await db.SaveChangesAsync();
            return conn;
        }
    }

    private static async Task<ChannelConnection> CreateConnectionWithCapabilitiesAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        string externalAccountId,
        ChannelCapabilities capabilities)
    {
        using (accessor.BeginScope(new TenantContext(tenantId, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var conn = ChannelConnection.Create(
                tenantId: tenantId,
                storeId: null,
                channel: ChannelType.Simulator,
                externalAccountId: externalAccountId,
                displayName: "Simulator Connection Custom Caps",
                encryptedCredentials: null,
                capabilities: capabilities);

            db.ChannelConnections.Add(conn);
            await db.SaveChangesAsync();
            return conn;
        }
    }

    private sealed class SystemTimeProvider : ITimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
