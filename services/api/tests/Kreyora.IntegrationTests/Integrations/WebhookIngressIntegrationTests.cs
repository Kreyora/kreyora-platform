using System.Diagnostics;
using System.Text;
using Kreyora.Application.Integrations;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Simulator;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Integrations;

public sealed class WebhookIngressIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public WebhookIngressIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task HandleWebhook_ValidSignedPayload_StoresEventAndReturns202WithFastPath()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-fast-path");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-fast-1", ChannelConnectionStatus.Active);

        var service = CreateIngressService(db, accessor);

        var rawBody = "{\"id\":\"evt_fast_100\",\"text\":\"fast path message\"}";
        var command = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: connection.Id,
            Method: "POST",
            Path: $"/v1/webhooks/simulator/{connection.Id}",
            Headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature,
                ["X-Provider-Event-Id"] = "evt_fast_100"
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes(rawBody),
            ContentType: "application/json",
            CorrelationId: "corr_fast_1",
            ReceivedAt: DateTimeOffset.UtcNow);

        var sw = Stopwatch.StartNew();
        var result = await service.HandleWebhookAsync(command);
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);
        Assert.False(result.IsDuplicate);
        Assert.NotNull(result.EventId);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Expected fast path < 500ms, but took {sw.ElapsedMilliseconds}ms");

        // Verify in DB
        var savedEvent = await db.WebhookEvents
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Id == result.EventId);

        Assert.Equal(tenant.Id, savedEvent.TenantId);
        Assert.Equal(connection.Id, savedEvent.ConnectionId);
        Assert.Equal("evt_fast_100", savedEvent.ProviderEventId);
        Assert.Equal(WebhookProcessingStatus.Received, savedEvent.ProcessingStatus);
        Assert.Equal("corr_fast_1", savedEvent.CorrelationId);
        Assert.Equal(rawBody, savedEvent.RawPayload);
        Assert.False(savedEvent.IsPurged);
    }

    [Fact]
    public async Task HandleWebhook_InvalidSignature_Returns401AndStoresZeroEvents()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-bad-sig");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-badsig-1", ChannelConnectionStatus.Active);

        var service = CreateIngressService(db, accessor);

        var command = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: connection.Id,
            Method: "POST",
            Path: $"/v1/webhooks/simulator/{connection.Id}",
            Headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = "sha256=invalid_tampered_signature"
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes("{\"text\":\"untrusted\"}"),
            ContentType: "application/json",
            CorrelationId: "corr_badsig",
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await service.HandleWebhookAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("Invalid signature header", result.ErrorReason);

        // Verify zero events stored
        var count = await db.WebhookEvents
            .IgnoreQueryFilters()
            .CountAsync(e => e.ConnectionId == connection.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task HandleWebhook_DuplicateDelivery_Returns200WithDuplicateFlagAndStoresSingleRow()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-dedup");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-dedup-1", ChannelConnectionStatus.Active);

        var service = CreateIngressService(db, accessor);

        var command = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: connection.Id,
            Method: "POST",
            Path: $"/v1/webhooks/simulator/{connection.Id}",
            Headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature,
                ["X-Provider-Event-Id"] = "evt_dedup_unique_1"
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes("{\"id\":\"evt_dedup_unique_1\",\"text\":\"first delivery\"}"),
            ContentType: "application/json",
            CorrelationId: "corr_dedup_1",
            ReceivedAt: DateTimeOffset.UtcNow);

        // First delivery
        var firstResult = await service.HandleWebhookAsync(command);
        Assert.True(firstResult.IsSuccess);
        Assert.Equal(202, firstResult.StatusCode);
        Assert.False(firstResult.IsDuplicate);

        // Second duplicate delivery
        var secondResult = await service.HandleWebhookAsync(command);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(200, secondResult.StatusCode);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(firstResult.EventId, secondResult.EventId);

        // Verify exactly one row in DB
        var events = await db.WebhookEvents
            .IgnoreQueryFilters()
            .Where(e => e.ConnectionId == connection.Id && e.ProviderEventId == "evt_dedup_unique_1")
            .ToListAsync();

        Assert.Single(events);
    }

    [Fact]
    public async Task HandleWebhook_OversizedPayload_Returns413AndStoresZeroEvents()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-oversize");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-over-1", ChannelConnectionStatus.Active);

        var service = CreateIngressService(db, accessor);

        // 300 KB payload (exceeds 256 KB limit)
        var oversizedBody = new byte[300 * 1024];
        Array.Fill(oversizedBody, (byte)'A');

        var command = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: connection.Id,
            Method: "POST",
            Path: $"/v1/webhooks/simulator/{connection.Id}",
            Headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: oversizedBody,
            ContentType: "application/json",
            CorrelationId: "corr_over",
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await service.HandleWebhookAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(413, result.StatusCode);

        var count = await db.WebhookEvents
            .IgnoreQueryFilters()
            .CountAsync(e => e.ConnectionId == connection.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task HandleWebhook_UnknownConnection_Returns404AndStoresZeroEvents()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var service = CreateIngressService(db, accessor);

        var command = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: "non_existent_conn_id_999",
            Method: "POST",
            Path: "/v1/webhooks/simulator/non_existent_conn_id_999",
            Headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes("{\"text\":\"hello\"}"),
            ContentType: "application/json",
            CorrelationId: "corr_notfound",
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await service.HandleWebhookAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task HandleWebhook_ReplayWindowExpired_Returns400AndStoresZeroEvents()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-replay");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-replay-1", ChannelConnectionStatus.Active);

        var service = CreateIngressService(db, accessor);

        var expiredEpoch = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var command = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: connection.Id,
            Method: "POST",
            Path: $"/v1/webhooks/simulator/{connection.Id}",
            Headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature,
                ["X-Hub-Timestamp"] = expiredEpoch
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes("{\"text\":\"replayed\"}"),
            ContentType: "application/json",
            CorrelationId: "corr_replay",
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await service.HandleWebhookAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("replay window", result.ErrorReason);

        var count = await db.WebhookEvents
            .IgnoreQueryFilters()
            .CountAsync(e => e.ConnectionId == connection.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task HandleChallenge_ValidVerifyToken_ReturnsChallengeString()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenant = await CreateTenantAsync(db, "wh-challenge");
        var connection = await CreateConnectionAsync(db, accessor, tenant.Id, "sim-challenge-1", ChannelConnectionStatus.Active);

        var service = CreateIngressService(db, accessor);

        var command = new WebhookChallengeCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: connection.Id,
            QueryParameters: new Dictionary<string, string>
            {
                ["hub.mode"] = "subscribe",
                ["hub.verify_token"] = SimulatorChannelProvider.DefaultVerifyToken,
                ["hub.challenge"] = "challenge_verified_456"
            },
            Headers: new Dictionary<string, string>());

        var result = await service.HandleChallengeAsync(command);

        Assert.True(result.IsValid);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("challenge_verified_456", result.ChallengeResponse);
    }

    [Fact]
    public async Task HandleWebhook_MultiTenantIsolation_EventScopedToConnectionTenant()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();

        var tenantA = await CreateTenantAsync(db, "wh-tenant-a");
        var tenantB = await CreateTenantAsync(db, "wh-tenant-b");

        var connectionA = await CreateConnectionAsync(db, accessor, tenantA.Id, "sim-ten-a", ChannelConnectionStatus.Active);

        var service = CreateIngressService(db, accessor);

        var command = new WebhookIngressCommand(
            Channel: ChannelType.Simulator,
            ConnectionId: connectionA.Id,
            Method: "POST",
            Path: $"/v1/webhooks/simulator/{connectionA.Id}",
            Headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature,
                ["X-Provider-Event-Id"] = "evt_tenant_iso_1"
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes("{\"text\":\"tenant a message\"}"),
            ContentType: "application/json",
            CorrelationId: "corr_ten_iso",
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await service.HandleWebhookAsync(command);
        Assert.True(result.IsSuccess);

        // When queried as Tenant B, global query filter must yield 0 results
        using var scopeB = accessor.BeginScope(new TenantContext(tenantB.Id, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner));
        var tenantBEvents = await db.WebhookEvents
            .Where(e => e.Id == result.EventId)
            .ToListAsync();

        Assert.Empty(tenantBEvents);

        // When queried as Tenant A, global query filter finds exactly 1 event
        using var scopeA = accessor.BeginScope(new TenantContext(tenantA.Id, "01J00000000000000000000003", "01J00000000000000000000004", TenantRole.Owner));
        var tenantAEvents = await db.WebhookEvents
            .Where(e => e.Id == result.EventId)
            .ToListAsync();

        Assert.Single(tenantAEvents);
        Assert.Equal(tenantA.Id, tenantAEvents[0].TenantId);
    }

    private static WebhookIngressService CreateIngressService(AppDbContext db, TenantContextAccessor accessor)
    {
        var provider = new SimulatorChannelProvider();
        var registry = new ChannelProviderRegistry(new[] { provider });
        var encryptionService = CreateEncryptionService();
        return new WebhookIngressService(db, registry, encryptionService, accessor, NullLogger<WebhookIngressService>.Instance);
    }

    private static AesGcmSecretEncryptionService CreateEncryptionService()
    {
        var masterKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = masterKey,
            DefaultKeyVersion = "v1",
            VersionedKeys = []
        });

        return new AesGcmSecretEncryptionService(options);
    }

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string name)
    {
        var tenant = Tenant.Create($"{name} tenant", $"{name}-{Guid.NewGuid():N}"[..Math.Min(40, name.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<ChannelConnection> CreateConnectionAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        string externalAccountId,
        ChannelConnectionStatus status)
    {
        using var scope = accessor.BeginScope(new TenantContext(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner));
        var connection = ChannelConnection.Create(
            tenantId: tenantId,
            channel: ChannelType.Simulator,
            externalAccountId: externalAccountId,
            displayName: "Simulator Connection",
            webhookVerificationToken: SimulatorChannelProvider.DefaultVerifyToken);

        if (status != ChannelConnectionStatus.Active)
        {
            connection.Disable();
        }

        db.ChannelConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }
}
