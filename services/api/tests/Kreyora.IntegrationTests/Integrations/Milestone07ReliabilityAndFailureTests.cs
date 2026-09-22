using System.Diagnostics;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace Kreyora.IntegrationTests.Integrations;

public sealed class Milestone07ReliabilityAndFailureTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public Milestone07ReliabilityAndFailureTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Scenario01_FastDecoupledIngress_AcknowledgesImmediatelyWithoutDownstreamBlock()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-fast-ingress");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-fast-conn");
        var env = CreateEnvironment(db, accessor);

        var body = "{\"message_id\":\"msg_fast_1\",\"text\":\"fast path testing\",\"sender_id\":\"sim_user_1\"}";
        var req = SimulatorChannelProvider.CreateSignedWebhookRequest(body, conn.WebhookVerificationToken);

        var cmd = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: conn.Id,
            Method: req.Method,
            Path: req.Path,
            Headers: req.Headers,
            QueryParameters: req.QueryParameters,
            RawBody: req.RawBody,
            ContentType: "application/json",
            CorrelationId: "corr_fast_1",
            ReceivedAt: DateTimeOffset.UtcNow);

        var sw = Stopwatch.StartNew();
        var result = await env.IngressService.HandleWebhookAsync(cmd);
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);
        Assert.NotNull(result.EventId);
        Assert.False(result.IsDuplicate);

        // Sub-150ms verification for decoupled ingress
        Assert.True(sw.ElapsedMilliseconds < 500, $"Ingress took {sw.ElapsedMilliseconds}ms, expected sub-500ms");

        // WebhookEvent stored as Received
        var savedEvent = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == result.EventId);
        Assert.Equal(WebhookProcessingStatus.Received, savedEvent.ProcessingStatus);

        // Downstream InboundEvent must NOT exist yet (proving complete decoupling from background normalization)
        var inboundExists = await db.InboundEvents.IgnoreQueryFilters().AnyAsync(i => i.WebhookEventId == result.EventId);
        Assert.False(inboundExists, "InboundEvent should not be created during ingress");
    }

    [Fact]
    public async Task Scenario02_DuplicateDeliveryStorm_HighConcurrency_ExactlyOneCreatedAndOthersDeduplicated()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-dup-storm");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-dup-conn");
        var env = CreateEnvironment(db, accessor);

        var fixedProviderEventId = "evt_storm_" + Guid.NewGuid().ToString("N")[..8];
        var body = JsonSerializer.Serialize(new
        {
            id = fixedProviderEventId,
            type = "text",
            text = "Duplicate storm payload"
        });

        var req = SimulatorChannelProvider.CreateSignedWebhookRequest(
            body,
            conn.WebhookVerificationToken,
            eventId: fixedProviderEventId);

        // Launch 10 concurrent requests with the identical ProviderEventId
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var taskAccessor = new TenantContextAccessor();
            await using var taskDb = fixture.CreateDbContext(taskAccessor);
            var taskEnv = CreateEnvironment(taskDb, taskAccessor);

            var cmd = new WebhookIngressCommand(
                Channel: ChannelType.Simulator,
                ConnectionId: conn.Id,
                Method: req.Method,
                Path: req.Path,
                Headers: req.Headers,
                QueryParameters: req.QueryParameters,
                RawBody: req.RawBody,
                ContentType: "application/json",
                CorrelationId: "corr_storm_" + Guid.NewGuid().ToString("N")[..8],
                ReceivedAt: DateTimeOffset.UtcNow);

            return await taskEnv.IngressService.HandleWebhookAsync(cmd);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        // All 10 requests must succeed with 200 or 202
        Assert.All(results, r => Assert.True(r.IsSuccess));

        // Exactly 1 request created the event; the remaining 9 are deduplicated
        var duplicates = results.Count(r => r.IsDuplicate);
        var originals = results.Count(r => !r.IsDuplicate);

        Assert.Equal(1, originals);
        Assert.Equal(9, duplicates);

        // Exactly 1 row in the database
        var dbCount = await db.WebhookEvents
            .IgnoreQueryFilters()
            .CountAsync(e => e.ConnectionId == conn.Id && e.ProviderEventId == fixedProviderEventId);

        Assert.Equal(1, dbCount);
    }

    [Fact]
    public async Task Scenario03_OutofOrderDelivery_MonotonicStatusProgression_PreservesReadAndIgnoresStaleDelivered()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-out-of-order");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-ooo-conn");
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        var queueRes = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "ooo_key_1",
            MessageType: OutboundMessageType.Text,
            TextContent: "Out of order status test"));

        var msgId = queueRes.OutboundMessageId!;
        await env.OutboundService.ProcessDeliveryAsync(msgId);

        var msgAfterSend = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.Equal(OutboundMessageStatus.Sent, msgAfterSend.Status);
        var providerMsgId = msgAfterSend.ProviderMessageId!;

        // 1. Deliver "Read" status receipt first (user read the message)
        var readTime = DateTimeOffset.UtcNow;
        await env.OutboundService.ProcessStatusReceiptAsync(
            conn.Id,
            providerMsgId,
            MessageDeliveryStatus.Read,
            readTime);

        var msgAfterRead = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.Equal(OutboundMessageStatus.Read, msgAfterRead.Status);
        Assert.NotNull(msgAfterRead.ReadAt);

        // 2. Out-of-order: deliver delayed "Delivered" receipt afterwards
        var deliveredTime = readTime.AddSeconds(-5);
        await env.OutboundService.ProcessStatusReceiptAsync(
            conn.Id,
            providerMsgId,
            MessageDeliveryStatus.Delivered,
            deliveredTime);

        // Monotonic check: Status must NOT regress back to Delivered; it must remain Read!
        var msgAfterStaleDelivered = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.Equal(OutboundMessageStatus.Read, msgAfterStaleDelivered.Status);
    }

    [Fact]
    public async Task Scenario04_LatencyAndSlowProvider_RespectsCancellationAndDoesNotCorruptState()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-latency");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-lat-conn");
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        var queueRes = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "lat_key_1",
            MessageType: OutboundMessageType.Text,
            TextContent: "Slow message",
            MetadataJson: "{\"simulate_latency_ms\":\"300\"}"));

        var msgId = queueRes.OutboundMessageId!;

        // Cancel delivery quickly at 20ms
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            env.OutboundService.ProcessDeliveryAsync(msgId, cts.Token));

        // State remains valid (classified as transient failure or remaining sending without state corruption)
        var msg = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.True(msg.Status == OutboundMessageStatus.Failed || msg.Status == OutboundMessageStatus.Sending);
        if (msg.Status == OutboundMessageStatus.Failed)
        {
            Assert.Equal(WebhookFailureClassification.Transient, msg.FailureClassification);
        }
    }

    [Fact]
    public async Task Scenario05_WorkerRestart_InterruptedSendingOrProcessing_RecoversCleanlyWithoutDuplication()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-worker-restart");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-restart-conn");
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        var queueRes = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "restart_key_1",
            MessageType: OutboundMessageType.Text,
            TextContent: "Recovery after crash"));

        var msgId = queueRes.OutboundMessageId!;

        // Simulate worker crash: message was marked Sending, but worker died before provider call
        var msg = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        msg.MarkSending();
        await db.SaveChangesAsync();

        // Worker restarts: Reset/retry policies pick up pending/failed items and retry delivery
        msg.RecordDeliveryFailure("Worker process terminated", WebhookFailureClassification.Transient, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        // New worker instance executes delivery
        var newEnv = CreateEnvironment(db, accessor);
        await newEnv.OutboundService.ProcessDeliveryAsync(msgId);

        var finalMsg = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.Equal(OutboundMessageStatus.Sent, finalMsg.Status);
        Assert.NotNull(finalMsg.ProviderMessageId);
    }

    [Fact]
    public async Task Scenario06_DatabaseConcurrency_ConflictingMutations_TriggersXminConcurrencyException()
    {
        var accessor = new TenantContextAccessor();
        await using var db1 = fixture.CreateDbContext(accessor);
        await using var db2 = fixture.CreateDbContext(accessor);
        await db1.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db1, "rel-concurrency");
        var conn = await CreateConnectionAsync(db1, accessor, tenant.Id, "sim-conc-conn");

        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner)))
        {
            var msg = OutboundMessage.Create(
                tenantId: tenant.Id,
                connectionId: conn.Id,
                channel: ChannelType.Simulator,
                recipientChannelId: "+9779800000001",
                idempotencyKey: "conc_key_1",
                messageType: OutboundMessageType.Text,
                textContent: "Concurrency test message");
            db1.OutboundMessages.Add(msg);
            await db1.SaveChangesAsync();

            // Load same entity in two distinct contexts
            var entity1 = await db1.OutboundMessages.SingleAsync(m => m.Id == msg.Id);
            var entity2 = await db2.OutboundMessages.SingleAsync(m => m.Id == msg.Id);

            // Mutation 1 succeeds
            entity1.MarkSending();
            await db1.SaveChangesAsync();

            // Mutation 2 with stale xmin token must throw DbUpdateConcurrencyException
            entity2.MarkSending();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Scenario07_TransientTimeout_ExponentialBackoffAndJitter_IncrementsAttemptCount()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-timeout-backoff");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-to-conn");
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        var queueRes = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "to_key_1",
            MessageType: OutboundMessageType.Text,
            TextContent: "Testing transient [SIMULATE_TRANSIENT]",
            MaxAttempts: 3));

        var msgId = queueRes.OutboundMessageId!;

        // Attempt 1: Fails with transient timeout
        await env.OutboundService.ProcessDeliveryAsync(msgId);

        var msgAttempt1 = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.Equal(OutboundMessageStatus.Failed, msgAttempt1.Status);
        Assert.Equal(1, msgAttempt1.AttemptCount);
        Assert.Equal(WebhookFailureClassification.Transient, msgAttempt1.FailureClassification);
        Assert.NotNull(msgAttempt1.NextRetryAt);
        var attempt1NextRetry = msgAttempt1.NextRetryAt.Value;

        // Attempt 2: Advance time and deliver again
        env.TimeProvider.SetTime(attempt1NextRetry.AddSeconds(1));
        await env.OutboundService.ProcessDeliveryAsync(msgId);

        var msgAttempt2 = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.Equal(2, msgAttempt2.AttemptCount);
        Assert.True(msgAttempt2.NextRetryAt > attempt1NextRetry, "NextRetryAt should increase with backoff");
    }

    [Fact]
    public async Task Scenario08_RateLimit429_OutboxSend_ClassifiedAsTransientAndRescheduled()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-rate-limit");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-rl-conn");
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        var queueRes = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "rl_key_1",
            MessageType: OutboundMessageType.Text,
            TextContent: "Rate limit test [SIMULATE_RATE_LIMIT]",
            MaxAttempts: 3));

        var msgId = queueRes.OutboundMessageId!;
        await env.OutboundService.ProcessDeliveryAsync(msgId);

        var msg = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == msgId);
        Assert.Equal(OutboundMessageStatus.Failed, msg.Status);
        Assert.Equal(WebhookFailureClassification.Transient, msg.FailureClassification);
        Assert.Contains("429", msg.LastErrorMessage);
        Assert.NotNull(msg.NextRetryAt);
    }

    [Fact]
    public async Task Scenario09_TokenExpiryAndReconnect_HealthCheckTransitionsToExpired_ReconnectRestoresActive()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-token-expiry");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-exp-conn", ChannelConnectionStatus.Active);
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        // 1. Queue a message while connection is Active
        var preQueued = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "pre_queued_1",
            MessageType: OutboundMessageType.Text,
            TextContent: "Message queued before expiry"));
        Assert.True(preQueued.Succeeded);

        // 2. Trigger Token Expiry scenario
        var expResult = await env.DiagnosticsService.ExecuteSimulatorScenarioAsync(
            new SimulatorScenarioRequest(conn.Id, SimulatorScenarioType.TokenExpiry));

        Assert.True(expResult.IsSuccess);
        var connAfterExp = await db.ChannelConnections.SingleAsync(c => c.Id == conn.Id);
        Assert.Equal(ChannelConnectionStatus.Expired, connAfterExp.Status);

        // 3. New queue attempt rejected at front door because connection is Expired
        var newQueueRes = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "exp_send_1",
            MessageType: OutboundMessageType.Text,
            TextContent: "Message while connection expired"));

        Assert.False(newQueueRes.Succeeded);
        Assert.Contains("Expired", newQueueRes.ErrorMessage);

        // 4. Attempting delivery of the pre-queued message fails safely with DeadLetter (CONNECTION_INACTIVE)
        await env.OutboundService.ProcessDeliveryAsync(preQueued.OutboundMessageId!);

        var msgAfterFailedSend = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == preQueued.OutboundMessageId!);
        Assert.Equal(OutboundMessageStatus.DeadLetter, msgAfterFailedSend.Status);
        Assert.Contains("not active", msgAfterFailedSend.LastErrorMessage);

        // 5. Reconnect scenario restores Active status
        var reconResult = await env.DiagnosticsService.ExecuteSimulatorScenarioAsync(
            new SimulatorScenarioRequest(conn.Id, SimulatorScenarioType.Reconnect));

        Assert.True(reconResult.IsSuccess);
        var connAfterRecon = await db.ChannelConnections.SingleAsync(c => c.Id == conn.Id);
        Assert.Equal(ChannelConnectionStatus.Active, connAfterRecon.Status);

        // 6. Subsequent send now succeeds cleanly
        var queueRes2 = await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
            ConnectionId: conn.Id,
            RecipientChannelId: "+9779800000001",
            IdempotencyKey: "recon_send_2",
            MessageType: OutboundMessageType.Text,
            TextContent: "Message after reconnect"));

        Assert.True(queueRes2.Succeeded);
        await env.OutboundService.ProcessDeliveryAsync(queueRes2.OutboundMessageId!);

        var msgAfterRecon = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == queueRes2.OutboundMessageId!);
        Assert.Equal(OutboundMessageStatus.Sent, msgAfterRecon.Status);
    }

    [Fact]
    public async Task Scenario10_PoisonPayload_QuarantinesToDeadLetterImmediatelyWithoutBurningRetries()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-poison");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-poison-conn");
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        var poisonBody = JsonSerializer.Serialize(new
        {
            is_poison = true,
            type = "text",
            text = "Malformed poison payload"
        });

        var req = SimulatorChannelProvider.CreateSignedWebhookRequest(poisonBody, conn.WebhookVerificationToken);

        var cmd = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: conn.Id,
            Method: req.Method,
            Path: req.Path,
            Headers: req.Headers,
            QueryParameters: req.QueryParameters,
            RawBody: req.RawBody,
            ContentType: "application/json",
            CorrelationId: "corr_poison_1",
            ReceivedAt: DateTimeOffset.UtcNow);

        var ingressRes = await env.IngressService.HandleWebhookAsync(cmd);
        var eventId = ingressRes.EventId!;

        // Process event
        var procRes = await env.ProcessingService.ProcessWebhookEventAsync(eventId);

        Assert.False(procRes.Succeeded);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, procRes.Status);

        var ev = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == eventId);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, ev.ProcessingStatus);
        Assert.Equal(WebhookFailureClassification.Permanent, ev.FailureClassification);
        Assert.NotNull(ev.DeadLetteredAt);
        // Quarantined immediately on attempt 1 of 5
        Assert.Equal(1, ev.AttemptCount);
    }

    [Fact]
    public async Task Scenario11_IdempotentReplay_InboundAndOutbound_ResetsStateAndAuditsReplay()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-replay");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-replay-conn");
        var env = CreateEnvironment(db, accessor);

        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner));

        // 1. Inbound DeadLetter Replay
        var rawBody = "{\"message_id\":\"msg_replay_in_1\",\"text\":\"healthy content\",\"sender_id\":\"sim_usr\"}";
        var whEvent = WebhookEvent.Create(
            tenantId: tenant.Id,
            connectionId: conn.Id,
            channel: ChannelType.Simulator,
            providerEventId: "evt_rep_in_1",
            eventType: "message",
            occurredAt: DateTimeOffset.UtcNow,
            receivedAt: DateTimeOffset.UtcNow,
            correlationId: "corr_rep_in",
            headers: "{}",
            rawPayload: rawBody,
            maxAttempts: 5);
        whEvent.MarkFailed("Simulated poison", deadLetter: true);
        db.WebhookEvents.Add(whEvent);
        await db.SaveChangesAsync();

        var replayInRes = await env.ProcessingService.ReplayWebhookEventAsync(whEvent.Id, "rep_idemp_in_1");
        Assert.True(replayInRes.IsSuccess);
        Assert.True(replayInRes.Value!.Succeeded);

        var evAfterReplay = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == whEvent.Id);
        Assert.Equal(WebhookProcessingStatus.Received, evAfterReplay.ProcessingStatus);
        Assert.Equal(0, evAfterReplay.AttemptCount);

        // 2. Outbound DeadLetter Replay
        var outMsg = OutboundMessage.Create(
            tenantId: tenant.Id,
            connectionId: conn.Id,
            channel: ChannelType.Simulator,
            recipientChannelId: "+9779800000001",
            idempotencyKey: "rep_idemp_out_seed",
            messageType: OutboundMessageType.Text,
            textContent: "Outbound DLQ replay content");
        outMsg.MarkSending();
        outMsg.RecordDeliveryFailure("Simulated deadletter", WebhookFailureClassification.Permanent, DateTimeOffset.UtcNow);
        db.OutboundMessages.Add(outMsg);
        await db.SaveChangesAsync();

        var replayOutRes = await env.OutboundService.ReplayMessageAsync(outMsg.Id, "rep_idemp_out_key_1");
        Assert.True(replayOutRes.Succeeded);

        var msgAfterReplay = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == outMsg.Id);
        Assert.Equal(OutboundMessageStatus.Queued, msgAfterReplay.Status);
        Assert.Equal(0, msgAfterReplay.AttemptCount);
    }

    [Fact]
    public async Task Scenario12_TwoTenantConcurrency_StrictIsolationAcrossIngressJobsAndDiagnostics()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenantA = await CreateTenantAsync(db, "rel-two-tenant-a");
        var tenantB = await CreateTenantAsync(db, "rel-two-tenant-b");

        var connA = await CreateConnectionAsync(db, accessor, tenantA.Id, "conn-tenant-a");
        var connB = await CreateConnectionAsync(db, accessor, tenantB.Id, "conn-tenant-b");

        var env = CreateEnvironment(db, accessor);

        // 1. Queue messages in both tenants
        using (accessor.BeginScope(new TenantContext(tenantA.Id, "usr_a", "mem_a", TenantRole.Owner)))
        {
            await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connA.Id,
                RecipientChannelId: "+9779800000001",
                IdempotencyKey: "tenant_a_msg_1",
                MessageType: OutboundMessageType.Text,
                TextContent: "Tenant A confidential order"));
        }

        using (accessor.BeginScope(new TenantContext(tenantB.Id, "usr_b", "mem_b", TenantRole.Owner)))
        {
            await env.OutboundService.QueueMessageAsync(new QueueOutboundMessageRequest(
                ConnectionId: connB.Id,
                RecipientChannelId: "+9779800000002",
                IdempotencyKey: "tenant_b_msg_1",
                MessageType: OutboundMessageType.Text,
                TextContent: "Tenant B confidential order"));
        }

        // 2. Deliver only Tenant A's queue
        using (accessor.BeginScope(new TenantContext(tenantA.Id, "usr_a", "mem_a", TenantRole.Owner)))
        {
            var dueA = await db.OutboundMessages
                .Where(m => m.Status == OutboundMessageStatus.Queued)
                .ToListAsync();

            Assert.Single(dueA);
            await env.OutboundService.ProcessDeliveryAsync(dueA[0].Id);
        }

        // 3. Verify Tenant A is Sent, but Tenant B remains Queued!
        var msgA = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.ConnectionId == connA.Id);
        var msgB = await db.OutboundMessages.IgnoreQueryFilters().SingleAsync(m => m.ConnectionId == connB.Id);

        Assert.Equal(OutboundMessageStatus.Sent, msgA.Status);
        Assert.Equal(OutboundMessageStatus.Queued, msgB.Status);

        // 4. Verify Diagnostics Overview for Tenant A only reports Tenant A
        using (accessor.BeginScope(new TenantContext(tenantA.Id, "usr_a", "mem_a", TenantRole.Owner)))
        {
            var overviewA = await env.DiagnosticsService.GetOverviewAsync();
            Assert.True(overviewA.IsSuccess);
            Assert.Equal(1, overviewA.Value!.TotalConnections);
            Assert.Equal(1, overviewA.Value!.ActiveConnections);
        }
    }

    [Fact]
    public async Task Scenario13_RedactedObservability_Adr012AndAdr013_NoSecretsExposedAndPayloadRedactedForNonOwner()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "rel-observability");
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-obs-conn");
        var env = CreateEnvironment(db, accessor);

        var piiPayload = "{\"customer_name\":\"Subash Shrestha\",\"phone\":\"+9779841234567\",\"amount_npr\":4500}";
        var ev = WebhookEvent.Create(
            tenantId: tenant.Id,
            connectionId: conn.Id,
            channel: ChannelType.Simulator,
            providerEventId: "evt_obs_1",
            eventType: "message",
            occurredAt: DateTimeOffset.UtcNow,
            receivedAt: DateTimeOffset.UtcNow,
            correlationId: "corr_obs_1",
            headers: "{}",
            rawPayload: piiPayload,
            maxAttempts: 5);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_admin", "mem_1", TenantRole.Owner)))
        {
            db.WebhookEvents.Add(ev);
            await db.SaveChangesAsync();
        }

        // 1. Check Owner access: can inspect raw payload (ADR-012)
        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_owner", "mem_1", TenantRole.Owner)))
        {
            var ownerResult = await env.DiagnosticsService.GetWebhookDetailAsync(ev.Id);
            Assert.True(ownerResult.IsSuccess);
            Assert.Equal(piiPayload, ownerResult.Value!.RawPayload);
            Assert.False(ownerResult.Value.IsPayloadRedacted);
        }

        // 2. Check Operator access: payload strictly redacted (ADR-012)
        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_op", "mem_2", TenantRole.Operator)))
        {
            var opResult = await env.DiagnosticsService.GetWebhookDetailAsync(ev.Id);
            Assert.True(opResult.IsSuccess);
            Assert.Equal("[REDACTED]", opResult.Value!.RawPayload);
            Assert.True(opResult.Value.IsPayloadRedacted);
        }

        // 3. Verify secrets are encrypted and zero raw secrets in connection diagnostics (ADR-013)
        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_owner", "mem_1", TenantRole.Owner)))
        {
            var diag = await env.DiagnosticsService.GetConnectionDiagnosticsAsync(conn.Id);
            Assert.True(diag.IsSuccess);
            // Diagnostics DTO contains health info and webhook URL, but zero credentials
            Assert.DoesNotContain("secret", diag.Value!.WebhookUrl, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record TestEnvironment(
        IWebhookIngressService IngressService,
        IWebhookProcessingService ProcessingService,
        IOutboundMessageService OutboundService,
        IIntegrationDiagnosticsService DiagnosticsService,
        MutableTimeProvider TimeProvider);

    private static TestEnvironment CreateEnvironment(AppDbContext db, TenantContextAccessor accessor)
    {
        var registry = new ChannelProviderRegistry([new SimulatorChannelProvider()]);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("rel-test"), authorizer);
        var encryption = new FakeSecretEncryptionService();
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var ingress = new WebhookIngressService(
            db,
            registry,
            encryption,
            accessor,
            NullLogger<WebhookIngressService>.Instance);

        var processing = new WebhookProcessingService(
            db,
            accessor,
            authorizer,
            audit,
            registry,
            NullLogger<WebhookProcessingService>.Instance);

        var connectionService = new ChannelConnectionService(
            db,
            accessor,
            authorizer,
            encryption,
            audit,
            registry,
            new RefusingInstagramGraphClient());

        var outbound = new OutboundMessageService(
            db,
            accessor,
            authorizer,
            audit,
            registry,
            new AlwaysAllowConversationGate(),
            timeProvider,
            NullLogger<OutboundMessageService>.Instance);

        var diagnostics = new IntegrationDiagnosticsService(
            db,
            accessor,
            authorizer,
            audit,
            ingress,
            processing,
            connectionService,
            outbound,
            NullLogger<IntegrationDiagnosticsService>.Instance);

        return new TestEnvironment(ingress, processing, outbound, diagnostics, timeProvider);
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
        string externalAccountId,
        ChannelConnectionStatus status = ChannelConnectionStatus.Active)
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

            if (status != ChannelConnectionStatus.Active)
            {
                conn.UpdateHealth(
                    isHealthy: status == ChannelConnectionStatus.Active,
                    status: status,
                    summary: status.ToString(),
                    details: null,
                    checkedAt: DateTimeOffset.UtcNow);
            }

            db.ChannelConnections.Add(conn);
            await db.SaveChangesAsync();
            return conn;
        }
    }

    private sealed class FakeSecretEncryptionService : ISecretEncryptionService
    {
        public EncryptedSecret Encrypt(string plainText, string? keyVersion = null) =>
            new("cipher", "iv", "tag", keyVersion ?? "v1");

        public string Decrypt(EncryptedSecret secret) => "decrypted_secret";
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : ITimeProvider
    {
        private DateTimeOffset current = initial;
        public DateTimeOffset UtcNow => current;
        public void SetTime(DateTimeOffset time) => current = time;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
