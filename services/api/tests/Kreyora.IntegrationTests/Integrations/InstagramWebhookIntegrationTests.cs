using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Integrations;

public sealed class InstagramWebhookIntegrationTests : IClassFixture<PostgresFixture>
{
    private const string TestSecret = "integration_app_secret_ig";
    private readonly PostgresFixture fixture;

    public InstagramWebhookIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task SignedPost_RoutesByIgId_PersistsRawAndAcknowledgesFast()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-wh-ack");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var connectionId = await CreateInstagramConnectionAsync(db, accessor, "igsid_ack_1");
        var ingress = CreateIngress(db, accessor);

        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_ack_1\",\"time\":1729500000000,\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_customer_1\"},\"timestamp\":1729500000123," +
            "\"message\":{\"mid\":\"mid_ack_1\",\"text\":\"Do you have size M?\"}}]}]}";

        var sw = Stopwatch.StartNew();
        var result = await ingress.HandleWebhookAsync(Command(body), CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Ack took {sw.ElapsedMilliseconds}ms.");

        var stored = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.ConnectionId == connectionId);
        Assert.Equal(connectionId, stored.ConnectionId);
        Assert.Equal(tenant.Id, stored.TenantId);
        Assert.Equal(body, stored.RawPayload);
    }

    [Fact]
    public async Task InvalidSignature_CreatesNoTrustedEvent()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-wh-badsig");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var connectionId = await CreateInstagramConnectionAsync(db, accessor, "igsid_sig_1");
        var ingress = CreateIngress(db, accessor);

        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_sig_1\",\"messaging\":[]}]}";
        var command = Command(body, signature: "sha256=deadbeef");

        var result = await ingress.HandleWebhookAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
        // Scoped to this test's connection: the class fixture shares one database across tests.
        Assert.Equal(0, await db.WebhookEvents.IgnoreQueryFilters().CountAsync(e => e.ConnectionId == connectionId));
    }

    [Fact]
    public async Task UnknownAccount_IsAcknowledgedWith200AndNotStored()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-wh-unknown");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var ingress = CreateIngress(db, accessor);

        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_nobody\",\"messaging\":[]}]}";
        var result = await ingress.HandleWebhookAsync(Command(body), CancellationToken.None);

        // Meta retries non-200 responses for up to 36 hours; a verified delivery for an account no
        // Kreyora connection owns is acknowledged and dropped (ADR-015).
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.EventId);
        Assert.Equal("{\"status\":\"ignored\"}", result.ResponseBody);
        Assert.Equal(0, await db.WebhookEvents.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenant.Id));
    }

    [Fact]
    public async Task ExactRedelivery_IsDeduplicated()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-wh-dupe");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var connectionId = await CreateInstagramConnectionAsync(db, accessor, "igsid_dupe_1");
        var ingress = CreateIngress(db, accessor);

        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_dupe_1\",\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_c9\"},\"timestamp\":1729500000123," +
            "\"message\":{\"mid\":\"mid_dupe_1\",\"text\":\"hi again\"}}]}]}";

        var first = await ingress.HandleWebhookAsync(Command(body), CancellationToken.None);
        var second = await ingress.HandleWebhookAsync(Command(body), CancellationToken.None);

        Assert.True(first.IsSuccess && !first.IsDuplicate);
        Assert.True(second.IsSuccess && second.IsDuplicate);
        Assert.Equal(first.EventId, second.EventId);
        Assert.Equal(1, await db.WebhookEvents.IgnoreQueryFilters()
            .CountAsync(e => e.ConnectionId == connectionId));
    }

    [Fact]
    public async Task Processing_NormalizesTextAndReadIntoInboundEvents()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-wh-proc");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var connectionId = await CreateInstagramConnectionAsync(db, accessor, "igsid_proc_1");
        var ingress = CreateIngress(db, accessor);

        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_proc_1\",\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_c2\"},\"timestamp\":1729500000123," +
            "\"message\":{\"mid\":\"mid_proc_1\",\"text\":\"order please\"}}," +
            "{\"sender\":{\"id\":\"igsid_c2\"},\"timestamp\":1729500000456," +
            "\"read\":{\"mid\":\"mid_out_99\"}}]}]}";

        var accepted = await ingress.HandleWebhookAsync(Command(body), CancellationToken.None);
        Assert.True(accepted.IsSuccess);

        var processing = CreateProcessing(db, accessor);
        var eventId = await db.WebhookEvents.IgnoreQueryFilters().Where(e => e.ConnectionId == connectionId).Select(e => e.Id).SingleAsync();
        var outcome = await processing.ProcessWebhookEventAsync(eventId, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var inbound = await db.InboundEvents.IgnoreQueryFilters()
            .Where(i => i.ConnectionId == connectionId)
            .ToListAsync(CancellationToken.None);
        Assert.Equal(2, inbound.Count);
        Assert.Contains(inbound, i => i.EventType == "text");
        Assert.Contains(inbound, i => i.EventType == "status");
    }

    [Fact]
    public async Task Processing_PoisonPayload_QuarantinesToDeadLetter()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-wh-poison");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var poisonConnectionId = await CreateInstagramConnectionAsync(db, accessor, "igsid_poison_1");
        var ingress = CreateIngress(db, accessor);

        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_poison_1\",BROKEN";
        var accepted = await ingress.HandleWebhookAsync(
            Command(body, connectionId: poisonConnectionId), CancellationToken.None);
        Assert.True(accepted.IsSuccess);

        var processing = CreateProcessing(db, accessor);
        var eventId = await db.WebhookEvents.IgnoreQueryFilters().Where(e => e.ConnectionId == poisonConnectionId).Select(e => e.Id).SingleAsync();
        await processing.ProcessWebhookEventAsync(eventId, CancellationToken.None);

        var stored = await db.WebhookEvents.IgnoreQueryFilters().SingleAsync(e => e.ConnectionId == poisonConnectionId);
        Assert.Equal(WebhookProcessingStatus.DeadLetter, stored.ProcessingStatus);
        Assert.Equal(WebhookFailureClassification.Permanent, stored.FailureClassification);
    }

    /// <summary>
    /// Inserts the connection entity directly: the service requires live Graph validation for Instagram
    /// (covered by InstagramConnectionIntegrationTests); these tests exercise the webhook path only.
    /// </summary>
    private static async Task<string> CreateInstagramConnectionAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        string externalAccountId)
    {
        var tenantId = accessor.Current!.TenantId;
        var connection = ChannelConnection.Create(tenantId, ChannelType.Instagram, externalAccountId, "IG Webhook Test");
        db.ChannelConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }

    private static WebhookIngressService CreateIngress(AppDbContext db, TenantContextAccessor accessor)
    {
        var provider = new InstagramChannelProvider(
            Options.Create(new InstagramWebhookOptions { AppSecret = TestSecret }));
        var registry = new ChannelProviderRegistry(new IChannelProvider[] { provider });
        return new WebhookIngressService(
            db, registry, CreateEncryptionService(), accessor, NullLogger<WebhookIngressService>.Instance);
    }

    private static WebhookProcessingService CreateProcessing(AppDbContext db, TenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("ig-wh-test"), authorizer);
        var provider = new InstagramChannelProvider(
            Options.Create(new InstagramWebhookOptions { AppSecret = TestSecret }));
        var registry = new ChannelProviderRegistry(new IChannelProvider[] { provider });
        return new WebhookProcessingService(
            db, accessor, authorizer, audit, registry, NullLogger<WebhookProcessingService>.Instance);
    }

    private static WebhookIngressCommand Command(string body, string? signature = null, string? connectionId = null) =>
        new(
            Channel: ChannelType.Instagram,
            ConnectionId: connectionId,
            Method: "POST",
            Path: "/v1/webhooks/instagram",
            Headers: new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["X-Hub-Signature-256"] = signature ?? Sign(body)
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes(body),
            ContentType: "application/json",
            CorrelationId: Guid.NewGuid().ToString("N"),
            ReceivedAt: DateTimeOffset.UtcNow);

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static AesGcmSecretEncryptionService CreateEncryptionService()
    {
        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            DefaultKeyVersion = "v1",
            VersionedKeys = []
        });

        return new AesGcmSecretEncryptionService(options);
    }

    private static TenantContext OwnerContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
