using System.Text.Json;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
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

public sealed class IntegrationDiagnosticsIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public IntegrationDiagnosticsIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task GetOverview_ReturnsLiveMetricsAcrossConnectionsAndQueues()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-overview-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var activeConn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-act-1", ChannelConnectionStatus.Active);
            var degradedConn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-deg-1", ChannelConnectionStatus.Degraded);

            // Create processed event
            var ev1 = await CreateWebhookEventAsync(db, accessor, tenant.Id, activeConn.Id, "evt_ov_1", "{}", WebhookProcessingStatus.Processed);
            ev1.RecordSuccess(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            // Create dead letter inbound event
            var ev2 = await CreateWebhookEventAsync(db, accessor, tenant.Id, activeConn.Id, "evt_ov_2", "{}", WebhookProcessingStatus.DeadLetter);
            ev2.RecordFailure("Poison", WebhookFailureClassification.Permanent, DateTimeOffset.UtcNow, WebhookRetryPolicy.Default);
            await db.SaveChangesAsync();

            // Create dead letter outbound message
            var outMsg = OutboundMessage.Create(
                tenantId: tenant.Id,
                connectionId: activeConn.Id,
                channel: ChannelType.Simulator,
                recipientChannelId: "+9779800000111",
                idempotencyKey: "idemp_ov_1",
                messageType: OutboundMessageType.Text,
                textContent: "Test outbound DLQ");
            outMsg.MarkSending();
            outMsg.RecordDeliveryFailure("Recipient blocked", WebhookFailureClassification.Permanent, DateTimeOffset.UtcNow);
            db.OutboundMessages.Add(outMsg);
            await db.SaveChangesAsync();

            var result = await diagnosticsService.GetOverviewAsync();

            Assert.True(result.IsSuccess);
            var overview = result.Value!;
            Assert.Equal(2, overview.TotalConnections);
            Assert.Equal(1, overview.ActiveConnections);
            Assert.Equal(1, overview.DegradedConnections);
            Assert.Equal(1, overview.InboundDeadLetterCount);
            Assert.Equal(1, overview.OutboundDeadLetterCount);
            Assert.True(overview.EventsProcessed24h >= 1);
        }
    }

    [Fact]
    public async Task GetConnectionDiagnostics_ComputesConnectionMetricsAndWebhookUrl()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-conn-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-act-diag", ChannelConnectionStatus.Active);

            var ev = await CreateWebhookEventAsync(db, accessor, tenant.Id, conn.Id, "evt_conn_diag_1", "{}", WebhookProcessingStatus.Processed);
            ev.RecordSuccess(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            var result = await diagnosticsService.GetConnectionDiagnosticsAsync(conn.Id);

            Assert.True(result.IsSuccess);
            var diag = result.Value!;
            Assert.Equal(conn.Id, diag.ConnectionId);
            Assert.Equal(ChannelType.Simulator, diag.Channel);
            Assert.True(diag.IsHealthy);
            Assert.Equal(ChannelConnectionStatus.Active, diag.Status);
            Assert.Equal($"/v1/webhooks/simulator/{conn.Id}", diag.WebhookUrl);
            Assert.Equal(1, diag.EventsProcessed24h);
            Assert.NotNull(diag.LastEventAt);
        }
    }

    [Fact]
    public async Task GetConnectionWebhooks_PaginatesAndFiltersCorrectly()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-paged-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", null, TenantRole.Owner)))
        {
            var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-paged-conn", ChannelConnectionStatus.Active);

            await CreateWebhookEventAsync(db, accessor, tenant.Id, conn.Id, "evt_pg_1", "{}", WebhookProcessingStatus.Processed);
            await CreateWebhookEventAsync(db, accessor, tenant.Id, conn.Id, "evt_pg_2", "{}", WebhookProcessingStatus.Failed);
            await CreateWebhookEventAsync(db, accessor, tenant.Id, conn.Id, "evt_pg_3", "{}", WebhookProcessingStatus.Processed);

            // Fetch page 1 with pageSize = 2
            var page1 = await diagnosticsService.GetConnectionWebhooksAsync(conn.Id, page: 1, pageSize: 2);
            Assert.True(page1.IsSuccess);
            Assert.Equal(2, page1.Value!.Items.Count);
            Assert.Equal(3, page1.Value.TotalCount);

            // Fetch filtered by Failed status
            var failedOnly = await diagnosticsService.GetConnectionWebhooksAsync(conn.Id, page: 1, pageSize: 10, status: WebhookProcessingStatus.Failed);
            Assert.True(failedOnly.IsSuccess);
            Assert.Single(failedOnly.Value!.Items);
            Assert.Equal("evt_pg_2", failedOnly.Value.Items[0].ProviderEventId);
        }
    }

    [Fact]
    public async Task GetWebhookDetail_RoleRedaction_OwnerSeesRawPayload_OperatorSeesRedacted()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-redact-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        var secretPayload = "{\"customer_phone\":\"+9779812345678\",\"secret_message\":\"private enquiry\"}";
        var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-redact-conn");
        var ev = await CreateWebhookEventAsync(db, accessor, tenant.Id, conn.Id, "evt_sec_1", secretPayload);

        // 1. Check as Owner: can view raw payload
        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_owner_1", "mem_1", TenantRole.Owner)))
        {
            var ownerResult = await diagnosticsService.GetWebhookDetailAsync(ev.Id);
            Assert.True(ownerResult.IsSuccess);
            Assert.Equal(secretPayload, ownerResult.Value!.RawPayload);
            Assert.False(ownerResult.Value.IsPayloadRedacted);
        }

        // 2. Check as Operator: payload must be redacted per ADR-012
        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_op_1", "mem_2", TenantRole.Operator)))
        {
            var opResult = await diagnosticsService.GetWebhookDetailAsync(ev.Id);
            Assert.True(opResult.IsSuccess);
            Assert.Equal("[REDACTED]", opResult.Value!.RawPayload);
            Assert.True(opResult.Value.IsPayloadRedacted);
        }
    }

    [Fact]
    public async Task ExecuteSimulatorScenario_HealthyInbound_IngressesAndNormalizesEvent()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-healthy-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner)))
        {
            var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-healthy-conn");

            var scenarioReq = new SimulatorScenarioRequest(
                ConnectionId: conn.Id,
                Scenario: SimulatorScenarioType.HealthyInbound);

            var result = await diagnosticsService.ExecuteSimulatorScenarioAsync(scenarioReq);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.Succeeded);
            Assert.NotNull(result.Value.GeneratedEventId);

            // Verify event is in DB with Processed status and Normalized InboundEvent created
            var savedEvent = await db.WebhookEvents.SingleOrDefaultAsync(e => e.Id == result.Value.GeneratedEventId);
            Assert.NotNull(savedEvent);
            Assert.Equal(WebhookProcessingStatus.Processed, savedEvent.ProcessingStatus);

            var inbound = await db.InboundEvents.SingleOrDefaultAsync(i => i.WebhookEventId == savedEvent.Id);
            Assert.NotNull(inbound);
            Assert.Equal(conn.Id, inbound.ConnectionId);
        }
    }

    [Fact]
    public async Task ExecuteSimulatorScenario_DuplicateDelivery_DeduplicatesSecondWebhook()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-dup-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner)))
        {
            var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-dup-conn");

            var scenarioReq = new SimulatorScenarioRequest(
                ConnectionId: conn.Id,
                Scenario: SimulatorScenarioType.DuplicateDelivery);

            var result = await diagnosticsService.ExecuteSimulatorScenarioAsync(scenarioReq);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.Succeeded);
            Assert.Contains("deduplicated: IsDuplicate=True", result.Value.Summary);
        }
    }

    [Fact]
    public async Task ExecuteSimulatorScenario_TokenExpiryAndReconnect_TransitionsConnectionStatus()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-expiry-reconnect-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner)))
        {
            var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-exp-conn", ChannelConnectionStatus.Active);

            // 1. Trigger TokenExpiry scenario
            var expResult = await diagnosticsService.ExecuteSimulatorScenarioAsync(
                new SimulatorScenarioRequest(conn.Id, SimulatorScenarioType.TokenExpiry));

            Assert.True(expResult.IsSuccess);

            var expConn = await db.ChannelConnections.SingleAsync(c => c.Id == conn.Id);
            Assert.Equal(ChannelConnectionStatus.Expired, expConn.Status);

            // 2. Trigger Reconnect scenario
            var reconResult = await diagnosticsService.ExecuteSimulatorScenarioAsync(
                new SimulatorScenarioRequest(conn.Id, SimulatorScenarioType.Reconnect));

            Assert.True(reconResult.IsSuccess);

            var reconConn = await db.ChannelConnections.SingleAsync(c => c.Id == conn.Id);
            Assert.Equal(ChannelConnectionStatus.Active, reconConn.Status);
        }
    }

    [Fact]
    public async Task ExecuteSimulatorScenario_PermanentPoison_QuarantinesToDeadLetter()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "diag-poison-tenant");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "usr_1", "mem_1", TenantRole.Owner)))
        {
            var conn = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-poison-conn");

            var scenarioReq = new SimulatorScenarioRequest(
                ConnectionId: conn.Id,
                Scenario: SimulatorScenarioType.PermanentPoison);

            var result = await diagnosticsService.ExecuteSimulatorScenarioAsync(scenarioReq);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.Succeeded);

            var ev = await db.WebhookEvents.SingleAsync(e => e.Id == result.Value.GeneratedEventId);
            Assert.Equal(WebhookProcessingStatus.DeadLetter, ev.ProcessingStatus);
            Assert.Equal(WebhookFailureClassification.Permanent, ev.FailureClassification);
            Assert.NotNull(ev.DeadLetteredAt);
        }
    }

    [Fact]
    public async Task CrossTenantIsolation_TenantBCannotAccessTenantADiagnostics()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenantA = await CreateTenantAsync(db, "iso-tenant-a");
        var tenantB = await CreateTenantAsync(db, "iso-tenant-b");
        var (diagnosticsService, _) = CreateServices(db, accessor);

        var connA = await CreateConnectionAsync(db, accessor, tenantA.Id, "sim-iso-conn-a");
        var evA = await CreateWebhookEventAsync(db, accessor, tenantA.Id, connA.Id, "evt_iso_a", "{}");

        // Authenticate as Tenant B
        using (accessor.BeginScope(new TenantContext(tenantB.Id, "usr_b_1", "mem_b_1", TenantRole.Owner)))
        {
            // Cannot get connection diagnostics for Tenant A's connection
            var diagResult = await diagnosticsService.GetConnectionDiagnosticsAsync(connA.Id);
            Assert.False(diagResult.IsSuccess);

            // Cannot get webhooks for Tenant A's connection
            var webhooksResult = await diagnosticsService.GetConnectionWebhooksAsync(connA.Id);
            Assert.False(webhooksResult.IsSuccess);

            // Cannot get webhook detail for Tenant A's event
            var detailResult = await diagnosticsService.GetWebhookDetailAsync(evA.Id);
            Assert.False(detailResult.IsSuccess);

            // Cannot execute scenario against Tenant A's connection
            var scenarioResult = await diagnosticsService.ExecuteSimulatorScenarioAsync(
                new SimulatorScenarioRequest(connA.Id, SimulatorScenarioType.HealthyInbound));
            Assert.False(scenarioResult.IsSuccess);
        }
    }

    private static (IntegrationDiagnosticsService, WebhookProcessingService) CreateServices(
        AppDbContext db,
        TenantContextAccessor accessor)
    {
        var registry = new ChannelProviderRegistry([new SimulatorChannelProvider()]);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("diag-test"), authorizer);
        var encryption = new FakeSecretEncryptionService();
        var timeProvider = new SystemTimeProvider();

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
            registry);

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

        return (diagnostics, processing);
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

    private static async Task<WebhookEvent> CreateWebhookEventAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        string connectionId,
        string providerEventId,
        string rawPayload,
        WebhookProcessingStatus status = WebhookProcessingStatus.Received,
        DateTimeOffset? occurredAt = null)
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
                maxAttempts: 5);

            if (status != WebhookProcessingStatus.Received)
            {
                if (status == WebhookProcessingStatus.Processed)
                {
                    ev.RecordSuccess(DateTimeOffset.UtcNow);
                }
                else if (status == WebhookProcessingStatus.Failed)
                {
                    ev.RecordFailure("Simulated timeout", WebhookFailureClassification.Transient, DateTimeOffset.UtcNow, WebhookRetryPolicy.Default);
                }
                else if (status == WebhookProcessingStatus.DeadLetter)
                {
                    ev.RecordFailure("Poison", WebhookFailureClassification.Permanent, DateTimeOffset.UtcNow, WebhookRetryPolicy.Default);
                }
            }

            db.WebhookEvents.Add(ev);
            await db.SaveChangesAsync();
            return ev;
        }
    }

    private sealed class FakeSecretEncryptionService : ISecretEncryptionService
    {
        public EncryptedSecret Encrypt(string plainText, string? keyVersion = null) =>
            new("cipher", "iv", "tag", keyVersion ?? "v1");

        public string Decrypt(EncryptedSecret secret) => "decrypted_secret";
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
