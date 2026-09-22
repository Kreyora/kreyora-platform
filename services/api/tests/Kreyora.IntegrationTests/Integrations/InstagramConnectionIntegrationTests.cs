using System.Net;
using System.Text;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
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
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Integrations;

public sealed class InstagramConnectionIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public InstagramConnectionIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task CreateInstagramConnection_WithValidLiveValidation_PersistsEncryptedCredentialsAndHealth()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-conn-create");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var stub = new FakeInstagramGraphHandler();
        stub.EnqueuePageLink("page_ig_1", "igsid_1");
        stub.EnqueueAccount("igsid_1", "test.shop");
        var service = CreateService(db, accessor, stub);

        var createResult = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.Instagram,
            ExternalAccountId: "igsid_1",
            DisplayName: "Test Shop IG",
            PlainTextSecret: "dev_pat_stub")
        {
            Instagram = new InstagramConnectOptions("page_ig_1", "igsid_1")
        });

        Assert.True(createResult.IsSuccess);
        Assert.NotNull(createResult.Value);
        Assert.Equal(ChannelConnectionStatus.Active, createResult.Value.Status);
        Assert.True(createResult.Value.HasCredentials);
        Assert.Equal("Validated against Instagram Graph API", createResult.Value.HealthSummary);
        Assert.Contains("test.shop", createResult.Value.HealthDetails);

        var entity = await db.ChannelConnections
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == createResult.Value.Id);
        Assert.NotNull(entity.EncryptedCredentials);
        Assert.DoesNotContain("dev_pat_stub", entity.EncryptedCredentials.CiphertextBase64);

        var audits = await db.AuditEvents.Where(a => a.TargetId == entity.Id).ToListAsync();
        Assert.Contains(audits, a => a.Action == "integrations.connection.created");
    }

    [Fact]
    public async Task CreateInstagramConnection_WithExpiredToken_PersistsNothing()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-conn-denied");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var stub = new FakeInstagramGraphHandler();
        stub.EnqueueError(190, "Invalid OAuth access token.");
        var service = CreateService(db, accessor, stub);

        var createResult = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.Instagram,
            ExternalAccountId: "igsid_9",
            DisplayName: "Expired IG",
            PlainTextSecret: "expired_pat_stub")
        {
            Instagram = new InstagramConnectOptions("page_ig_9", "igsid_9")
        });

        Assert.False(createResult.IsSuccess);
        Assert.Equal(0, await db.ChannelConnections
            .IgnoreQueryFilters()
            .CountAsync(c => c.ExternalAccountId == "igsid_9"));
    }

    [Fact]
    public async Task ReauthorizeInstagramConnection_ReplacesCredentialsAndAudits()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-conn-reauth");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var stub = new FakeInstagramGraphHandler();
        stub.EnqueuePageLink("page_ig_2", "igsid_2");
        stub.EnqueueAccount("igsid_2", "test.shop");
        stub.EnqueueAccount("igsid_2", "test.shop");
        var service = CreateService(db, accessor, stub);

        var created = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.Instagram,
            ExternalAccountId: "igsid_2",
            DisplayName: "Reauth IG",
            PlainTextSecret: "first_pat_stub")
        {
            Instagram = new InstagramConnectOptions("page_ig_2", "igsid_2")
        });
        Assert.True(created.IsSuccess);

        var updated = await service.UpdateConnectionAsync(
            created.Value!.Id,
            new UpdateChannelConnectionRequest(PlainTextSecret: "second_pat_stub")
            {
                Instagram = new InstagramConnectOptions("page_ig_2", "igsid_2")
            });

        Assert.True(updated.IsSuccess);
        var audits = await db.AuditEvents.Where(a => a.TargetId == created.Value.Id).ToListAsync();
        Assert.Contains(audits, a => a.Action == "integrations.connection.reauthorized");
    }

    [Fact]
    public async Task CheckInstagramHealth_MapsExpiredTokenToExpiredStatus()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "ig-conn-health");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var stub = new FakeInstagramGraphHandler();
        stub.EnqueuePageLink("page_ig_3", "igsid_3");
        stub.EnqueueAccount("igsid_3", "test.shop");
        stub.EnqueueError(190, "Invalid OAuth access token.");
        var service = CreateService(db, accessor, stub);

        var created = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.Instagram,
            ExternalAccountId: "igsid_3",
            DisplayName: "Health IG",
            PlainTextSecret: "health_pat_stub")
        {
            Instagram = new InstagramConnectOptions("page_ig_3", "igsid_3")
        });
        Assert.True(created.IsSuccess);

        var health = await service.CheckHealthAsync(created.Value!.Id);

        Assert.True(health.IsSuccess);
        Assert.False(health.Value!.IsHealthy);
        Assert.Equal(ChannelConnectionStatus.Expired, health.Value.NewStatus);

        var entity = await db.ChannelConnections
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == created.Value.Id);
        Assert.Equal(ChannelConnectionStatus.Expired, entity.Status);
    }

    [Fact]
    public async Task CrossTenantConnection_IsInvisibleToOtherTenant()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "ig-conn-tenant-a");
        var tenantB = await CreateTenantAsync(db, "ig-conn-tenant-b");

        var stub = new FakeInstagramGraphHandler();
        stub.EnqueuePageLink("page_ig_4", "igsid_4");
        stub.EnqueueAccount("igsid_4", "tenant.a");

        string connectionId;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            var service = CreateService(db, accessor, stub);
            var created = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
                Channel: ChannelType.Instagram,
                ExternalAccountId: "igsid_4",
                DisplayName: "Tenant A IG",
                PlainTextSecret: "tenant_a_pat_stub")
            {
                Instagram = new InstagramConnectOptions("page_ig_4", "igsid_4")
            });
            Assert.True(created.IsSuccess);
            connectionId = created.Value!.Id;
        }

        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var service = CreateService(db, accessor, stub);
            var seen = await service.GetConnectionByIdAsync(connectionId);
            Assert.False(seen.IsSuccess);
        }
    }

    private static ChannelConnectionService CreateService(
        AppDbContext db,
        TenantContextAccessor accessor,
        FakeInstagramGraphHandler stub)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("ig-conn-test"), authorizer);
        var enc = CreateEncryptionService();
        var registry = new ChannelProviderRegistry(Array.Empty<IChannelProvider>());
        var client = new InstagramGraphClient(
            new HttpClient(stub),
            Options.Create(new InstagramGraphOptions()));
        return new ChannelConnectionService(db, accessor, authorizer, enc, audit, registry, client);
    }

    private static AesGcmSecretEncryptionService CreateEncryptionService()
    {
        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
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

/// <summary>Queued-response HTTP stub for the Instagram Graph API. Test-only; no live calls.</summary>
public sealed class FakeInstagramGraphHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new();
    private readonly List<HttpRequestMessage> sentRequests = new();
    public IReadOnlyList<HttpRequestMessage> Requests => sentRequests;

    public void EnqueuePageLink(string pageId, string linkedAccountId) =>
        responses.Enqueue(JsonResponse(
            $"{{\"id\":\"{pageId}\",\"instagram_business_account\":{{\"id\":\"{linkedAccountId}\"}}}}"));

    public void EnqueueAccount(string accountId, string username) =>
        responses.Enqueue(JsonResponse(
            $"{{\"id\":\"{accountId}\",\"username\":\"{username}\"}}"));

    public void EnqueueError(int code, string message) =>
        responses.Enqueue(JsonResponse(
            $"{{\"error\":{{\"message\":\"{message}\",\"code\":{code}}}}}",
            System.Net.HttpStatusCode.BadRequest));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        sentRequests.Add(request);

        if (request.Headers.Authorization?.Scheme != "Bearer")
        {
            return Task.FromResult(JsonResponse(
                "{\"error\":{\"message\":\"Missing bearer token.\",\"code\":10}}",
                System.Net.HttpStatusCode.Unauthorized));
        }

        if (request.RequestUri?.Query.Contains("access_token=") == true)
        {
            return Task.FromResult(JsonResponse(
                "{\"error\":{\"message\":\"Token leaked into URL.\",\"code\":10}}",
                System.Net.HttpStatusCode.Unauthorized));
        }

        return Task.FromResult(
            responses.Count > 0 ? responses.Dequeue() : JsonResponse("{\"error\":{\"message\":\"No stubbed response.\",\"code\":1}}",
            System.Net.HttpStatusCode.InternalServerError));
    }

    private static HttpResponseMessage JsonResponse(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

/// <summary>Refusing stub for suites that never exercise Instagram validation.</summary>
public sealed class RefusingInstagramGraphClient : IInstagramGraphClient
{
    public Task<InstagramValidationResult> ValidatePageLinkAsync(
        string pageAccessToken,
        string pageId,
        string instagramAccountId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InstagramValidationResult.Failed(
            InstagramValidationKind.Transient, "refused", "Instagram validation is not stubbed in this suite."));

    public Task<InstagramValidationResult> ValidateAccountAsync(
        string pageAccessToken,
        string instagramAccountId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InstagramValidationResult.Failed(
            InstagramValidationKind.Transient, "refused", "Instagram validation is not stubbed in this suite."));
}
