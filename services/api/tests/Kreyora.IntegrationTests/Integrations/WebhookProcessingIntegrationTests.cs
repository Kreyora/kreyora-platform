using System.Text;
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

public sealed class WebhookProcessingIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public WebhookProcessingIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task ProcessWebhookEvent_NormalPayload_CreatesInboundEventsAndMarksProcessed()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-proc-success");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-conn-1");

        var rawBody = "{\"message_id\":\"msg_norm_100\",\"text\":\"Hello Kreyora\",\"sender_id\":\"user_99\"}";
        var webhookEvent = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_p_1", rawBody);

        var service = CreateProcessingService(db, accessor);

        var result = await service.ProcessWebhookEventAsync(webhookEvent.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(WebhookProcessingStatus.Processed, result.Status);
        Assert.Equal(1, result.NormalizedEventsCount);

        // Verify InboundEvent in DB
        var inbound = await db.InboundEvents
            .IgnoreQueryFilters()
            .SingleAsync(i => i.WebhookEventId == webhookEvent.Id);

        Assert.Equal(tenant.Id, inbound.TenantId);
        Assert.Equal(connection.Id, inbound.ConnectionId);
        Assert.Equal("msg_norm_100", inbound.ProviderMessageId);
        Assert.Equal("v1", inbound.SchemaVersion);
        Assert.Equal("text", inbound.EventType);
        Assert.Contains("Hello Kreyora", inbound.PayloadJson);

        // Verify WebhookEvent state
        var updatedEvent = await db.WebhookEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Id == webhookEvent.Id);

        Assert.Equal(WebhookProcessingStatus.Processed, updatedEvent.ProcessingStatus);
        Assert.NotNull(updatedEvent.ProcessedAt);
        Assert.Null(updatedEvent.ErrorMessage);
    }

    [Fact]
    public async Task ProcessWebhookEvent_DuplicateProviderMessageId_SkipsDuplicateInboundEvent()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-proc-dedup");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-conn-dedup");

        var rawBody1 = "{\"message_id\":\"msg_dup_999\",\"text\":\"First delivery\",\"sender_id\":\"user_1\"}";
        var rawBody2 = "{\"message_id\":\"msg_dup_999\",\"text\":\"Second delivery with same message ID\",\"sender_id\":\"user_1\"}";

        var webhookEvent1 = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_dup_1", rawBody1);
        var webhookEvent2 = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_dup_2", rawBody2);

        var service = CreateProcessingService(db, accessor);

        var result1 = await service.ProcessWebhookEventAsync(webhookEvent1.Id);
        Assert.True(result1.Succeeded);
        Assert.Equal(1, result1.NormalizedEventsCount);

        var result2 = await service.ProcessWebhookEventAsync(webhookEvent2.Id);
        Assert.True(result2.Succeeded);
        Assert.Equal(0, result2.NormalizedEventsCount); // Skipped duplicate message!

        // Verify exactly 1 InboundEvent exists for this message ID
        var matchingEvents = await db.InboundEvents
            .IgnoreQueryFilters()
            .Where(i => i.ConnectionId == connection.Id && i.ProviderMessageId == "msg_dup_999")
            .ToListAsync();

        Assert.Single(matchingEvents);
    }

    [Fact]
    public async Task ProcessWebhookEvent_OutOfOrderEvents_PreservesOccurredAtTimestamps()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-proc-order");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-conn-order");

        var earlierTime = DateTimeOffset.UtcNow.AddMinutes(-30);
        var laterTime = DateTimeOffset.UtcNow;

        var rawBodyLater = $"{{\"message_id\":\"msg_order_2\",\"text\":\"Later message\",\"occurred_at\":\"{laterTime:O}\"}}";
        var rawBodyEarlier = $"{{\"message_id\":\"msg_order_1\",\"text\":\"Earlier message\",\"occurred_at\":\"{earlierTime:O}\"}}";

        // Event 2 arrives first
        var event2 = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_ord_2", rawBodyLater, occurredAt: laterTime);
        // Event 1 arrives second (out of order)
        var event1 = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_ord_1", rawBodyEarlier, occurredAt: earlierTime);

        var service = CreateProcessingService(db, accessor);

        await service.ProcessWebhookEventAsync(event2.Id);
        await service.ProcessWebhookEventAsync(event1.Id);

        var inbounds = await db.InboundEvents
            .IgnoreQueryFilters()
            .Where(i => i.ConnectionId == connection.Id)
            .OrderBy(i => i.OccurredAt)
            .ToListAsync();

        Assert.Equal(2, inbounds.Count);
        Assert.Equal("msg_order_1", inbounds[0].ProviderMessageId);
        Assert.Equal("msg_order_2", inbounds[1].ProviderMessageId);
    }

    [Fact]
    public async Task ProcessWebhookEvent_TransientFailure_IncrementsAttemptAndSchedulesNextRetry()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-proc-transient");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-conn-trans");

        var rawBody = "{\"throw_transient\":true,\"message_id\":\"msg_trans_1\"}";
        var webhookEvent = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_trans_1", rawBody, maxAttempts: 3);

        var service = CreateProcessingService(db, accessor);

        var result = await service.ProcessWebhookEventAsync(webhookEvent.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(WebhookProcessingStatus.Failed, result.Status);
        Assert.Equal(WebhookFailureClassification.Transient, result.FailureClassification);

        var updatedEvent = await db.WebhookEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Id == webhookEvent.Id);

        Assert.Equal(WebhookProcessingStatus.Failed, updatedEvent.ProcessingStatus);
        Assert.Equal(1, updatedEvent.AttemptCount);
        Assert.Equal(WebhookFailureClassification.Transient, updatedEvent.FailureClassification);
        Assert.NotNull(updatedEvent.NextRetryAt);
        Assert.Null(updatedEvent.DeadLetteredAt);
    }

    [Fact]
    public async Task ProcessWebhookEvent_RetryExhaustion_TransitionsToDeadLetter()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-proc-exhaust");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-conn-exhaust");

        var rawBody = "{\"throw_transient\":true,\"message_id\":\"msg_ex_1\"}";
        var webhookEvent = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_ex_1", rawBody, maxAttempts: 2);

        var service = CreateProcessingService(db, accessor);

        // Attempt 1: Failed
        var result1 = await service.ProcessWebhookEventAsync(webhookEvent.Id);
        Assert.Equal(WebhookProcessingStatus.Failed, result1.Status);

        // Attempt 2: DeadLetter (exhausted)
        var result2 = await service.ProcessWebhookEventAsync(webhookEvent.Id);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, result2.Status);
        Assert.Equal(WebhookFailureClassification.Exhausted, result2.FailureClassification);

        var updatedEvent = await db.WebhookEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Id == webhookEvent.Id);

        Assert.Equal(WebhookProcessingStatus.DeadLetter, updatedEvent.ProcessingStatus);
        Assert.Equal(2, updatedEvent.AttemptCount);
        Assert.NotNull(updatedEvent.DeadLetteredAt);
        Assert.Null(updatedEvent.NextRetryAt);
    }

    [Fact]
    public async Task ProcessWebhookEvent_PoisonPayload_ImmediatelyQuarantinesToDeadLetter()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-proc-poison");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-conn-poison");

        var rawBody = "{\"is_poison\":true,\"message_id\":\"msg_poison_1\"}";
        var webhookEvent = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_poison_1", rawBody, maxAttempts: 5);

        var service = CreateProcessingService(db, accessor);

        var result = await service.ProcessWebhookEventAsync(webhookEvent.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, result.Status);
        Assert.Equal(WebhookFailureClassification.Permanent, result.FailureClassification);

        var updatedEvent = await db.WebhookEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Id == webhookEvent.Id);

        Assert.Equal(WebhookProcessingStatus.DeadLetter, updatedEvent.ProcessingStatus);
        Assert.Equal(1, updatedEvent.AttemptCount);
        Assert.NotNull(updatedEvent.DeadLetteredAt);
        Assert.Null(updatedEvent.NextRetryAt);
        Assert.Contains("poison", updatedEvent.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplayWebhookEvent_DeadLetterEvent_ResetsToReceivedAndReprocesses()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-proc-replay");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-conn-replay");

        // Start with poison payload that gets dead-lettered
        var rawBody = "{\"is_poison\":true,\"message_id\":\"msg_replay_1\"}";
        var webhookEvent = await CreateWebhookEventAsync(db, accessor, tenant.Id, connection.Id, "evt_replay_1", rawBody);

        var service = CreateProcessingService(db, accessor);
        await service.ProcessWebhookEventAsync(webhookEvent.Id);

        var deadLettered = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == webhookEvent.Id);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, deadLettered.ProcessingStatus);

        // Replay with Idempotency-Key
        using (accessor.BeginScope(new TenantContext(tenant.Id, "user_admin", null, TenantRole.Owner)))
        {
            var replayResult = await service.ReplayWebhookEventAsync(webhookEvent.Id, "idemp_replay_100");
            Assert.True(replayResult.IsSuccess);
            Assert.NotNull(replayResult.Value);
            Assert.Equal(WebhookProcessingStatus.Received, replayResult.Value.Status);
        }

        var replayedEvent = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == webhookEvent.Id);
        Assert.Equal(WebhookProcessingStatus.Received, replayedEvent.ProcessingStatus);
        Assert.Equal(0, replayedEvent.AttemptCount);
        Assert.Null(replayedEvent.DeadLetteredAt);
        Assert.Null(replayedEvent.ErrorMessage);
    }

    [Fact]
    public async Task WebhookProcessingJob_MultiTenant_ExecutesWithStrictTenantIsolation()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenantA = await CreateTenantAsync(db, "wh-job-ten-a");
        var tenantB = await CreateTenantAsync(db, "wh-job-ten-b");

        var connA = await CreateConnectionAsync(db, accessor, tenantA.Id, "sim-conn-a");
        var connB = await CreateConnectionAsync(db, accessor, tenantB.Id, "sim-conn-b");

        var eventA = await CreateWebhookEventAsync(db, accessor, tenantA.Id, connA.Id, "evt_ten_a", "{\"message_id\":\"msg_a\",\"text\":\"Tenant A\"}");
        var eventB = await CreateWebhookEventAsync(db, accessor, tenantB.Id, connB.Id, "evt_ten_b", "{\"message_id\":\"msg_b\",\"text\":\"Tenant B\"}");

        // Create service scope factory and job
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("wh-job-test"), authorizer);

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddSingleton<ITenantPermissionAuthorizer>(authorizer);
        services.AddSingleton<IAuditEventService>(audit);
        services.AddSingleton<IChannelProviderRegistry>(new ChannelProviderRegistry([new SimulatorChannelProvider()]));
        services.AddSingleton<IWebhookProcessingService, WebhookProcessingService>();
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        var job = new WebhookProcessingJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WebhookProcessingJob>.Instance);

        // Run only for Tenant A
        var countA = await job.ProcessTenantWebhooksAsync(serviceProvider, tenantA.Id);

        Assert.Equal(1, countA);

        // Check Tenant A event was processed
        var savedEventA = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == eventA.Id);
        Assert.Equal(WebhookProcessingStatus.Processed, savedEventA.ProcessingStatus);

        // Check Tenant B event was untouched!
        var savedEventB = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == eventB.Id);
        Assert.Equal(WebhookProcessingStatus.Received, savedEventB.ProcessingStatus);
    }

    private static WebhookProcessingService CreateProcessingService(AppDbContext db, ITenantContextAccessor accessor)
    {
        var registry = new ChannelProviderRegistry([new SimulatorChannelProvider()]);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("wh-proc-test"), authorizer);
        return new WebhookProcessingService(
            db,
            accessor,
            authorizer,
            audit,
            registry,
            NullLogger<WebhookProcessingService>.Instance);
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

    private static async Task<WebhookEvent> CreateWebhookEventAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        string connectionId,
        string providerEventId,
        string rawPayload,
        DateTimeOffset? occurredAt = null,
        int maxAttempts = 5)
    {
        using (accessor.BeginScope(new TenantContext(tenantId, null, null, null)))
        {
            var ev = WebhookEvent.Create(
                tenantId: tenantId,
                connectionId: connectionId,
                channel: ChannelType.Simulator,
                providerEventId: providerEventId,
                eventType: "messages",
                occurredAt: occurredAt ?? DateTimeOffset.UtcNow,
                receivedAt: DateTimeOffset.UtcNow,
                correlationId: "corr_" + Guid.NewGuid().ToString("N"),
                headers: "{\"content-type\":\"application/json\"}",
                rawPayload: rawPayload,
                maxAttempts: maxAttempts);

            db.WebhookEvents.Add(ev);
            await db.SaveChangesAsync();
            return ev;
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
