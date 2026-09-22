using System.Security.Cryptography;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Integrations;

public sealed class ChannelConnectionIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public ChannelConnectionIntegrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task CreateConnection_WithEncryptedSecret_StoresCiphertextAndZeroPlaintext()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "conn-create-enc");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var encryptionService = CreateEncryptionService();
        var service = CreateService(db, accessor, encryptionService);

        const string plainSecret = "EAABsbcsd8f76sdfsd87f6sd87fsd68fsd";
        var createResult = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.WhatsApp,
            ExternalAccountId: "+9779800000001",
            DisplayName: "Sales WhatsApp",
            PlainTextSecret: plainSecret));

        Assert.True(createResult.IsSuccess);
        Assert.NotNull(createResult.Value);
        Assert.Equal("+9779800000001", createResult.Value.ExternalAccountId);
        Assert.True(createResult.Value.HasCredentials);
        Assert.Equal("v1", createResult.Value.KeyVersion);

        // Verify directly in DB that credentials are encrypted
        var entity = await db.ChannelConnections
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == createResult.Value.Id);

        Assert.NotNull(entity.EncryptedCredentials);
        Assert.NotEqual(plainSecret, entity.EncryptedCredentials.CiphertextBase64);
        Assert.NotEmpty(entity.EncryptedCredentials.CiphertextBase64);
        Assert.NotEmpty(entity.EncryptedCredentials.IvBase64);
        Assert.NotEmpty(entity.EncryptedCredentials.AuthTagBase64);
        Assert.Equal("v1", entity.EncryptedCredentials.KeyVersion);

        // Verify decrypted value matches original plain text
        var decrypted = encryptionService.Decrypt(entity.EncryptedCredentials);
        Assert.Equal(plainSecret, decrypted);

        // Verify audit event
        var audits = await db.AuditEvents.Where(a => a.TargetId == entity.Id).ToListAsync();
        Assert.Contains(audits, a => a.Action == "integrations.connection.created");
    }

    [Fact]
    public async Task GetConnectionById_ReturnsRedactedDto_ZeroSecretLeakage()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "conn-redact");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var service = CreateService(db, accessor);

        var createResult = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.Instagram,
            ExternalAccountId: "ig_page_12345",
            DisplayName: "Brand Instagram",
            PlainTextSecret: "super_secret_ig_token"));

        Assert.True(createResult.IsSuccess);

        var getResult = await service.GetConnectionByIdAsync(createResult.Value!.Id);
        Assert.True(getResult.IsSuccess);
        Assert.NotNull(getResult.Value);
        Assert.Equal("Brand Instagram", getResult.Value.DisplayName);
        Assert.True(getResult.Value.HasCredentials);
        Assert.Equal("v1", getResult.Value.KeyVersion);
        Assert.Equal(ChannelConnectionStatus.Active, getResult.Value.Status);
    }

    [Fact]
    public async Task RotateConnectionSecrets_UpdatesKeyVersion_AndPreservesDecryptedSecret()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "conn-rotate");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var v1Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var v2Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var encryptionService = CreateEncryptionService(new Dictionary<string, string>
        {
            ["v1"] = v1Key,
            ["v2"] = v2Key
        }, defaultVersion: "v1");

        var service = CreateService(db, accessor, encryptionService);

        const string originalSecret = "webhook_signing_secret_99999";
        var createResult = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.Viber,
            ExternalAccountId: "viber_bot_1",
            DisplayName: "Viber Support",
            PlainTextSecret: originalSecret));

        Assert.True(createResult.IsSuccess);
        Assert.Equal("v1", createResult.Value!.KeyVersion);

        // Rotate to v2
        var rotateResult = await service.RotateConnectionSecretsAsync(createResult.Value.Id, "v2");
        Assert.True(rotateResult.IsSuccess);
        Assert.Equal("v2", rotateResult.Value!.KeyVersion);

        // Verify in DB that credentials can be decrypted with v2
        var entity = await db.ChannelConnections
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == createResult.Value.Id);

        Assert.Equal("v2", entity.EncryptedCredentials!.KeyVersion);
        var decrypted = encryptionService.Decrypt(entity.EncryptedCredentials);
        Assert.Equal(originalSecret, decrypted);

        // Verify audit event
        var audits = await db.AuditEvents.Where(a => a.TargetId == entity.Id).ToListAsync();
        Assert.Contains(audits, a => a.Action == "integrations.connection.secret_rotated");
    }

    [Fact]
    public async Task CrossTenant_CannotAccessOrMutateOtherTenantsConnections()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "conn-tenant-a");
        var tenantB = await CreateTenantAsync(db, "conn-tenant-b");

        ChannelConnectionDto connectionA;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            var serviceA = CreateService(db, accessor);
            var result = await serviceA.CreateConnectionAsync(new CreateChannelConnectionRequest(
                Channel: ChannelType.Telegram,
                ExternalAccountId: "bot_tenant_a",
                DisplayName: "Tenant A Bot",
                PlainTextSecret: "token_a"));
            Assert.True(result.IsSuccess);
            connectionA = result.Value!;
        }

        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var serviceB = CreateService(db, accessor);

            // Get by ID
            var getResult = await serviceB.GetConnectionByIdAsync(connectionA.Id);
            Assert.True(getResult.IsFailure);
            Assert.Equal(404, getResult.Error!.Status);

            // Update
            var updateResult = await serviceB.UpdateConnectionAsync(connectionA.Id, new UpdateChannelConnectionRequest("Hacked"));
            Assert.True(updateResult.IsFailure);
            Assert.Equal(404, updateResult.Error!.Status);

            // Rotate
            var rotateResult = await serviceB.RotateConnectionSecretsAsync(connectionA.Id, "v2");
            Assert.True(rotateResult.IsFailure);
            Assert.Equal(404, rotateResult.Error!.Status);

            // Disable
            var disableResult = await serviceB.DisableConnectionAsync(connectionA.Id);
            Assert.True(disableResult.IsFailure);
            Assert.Equal(404, disableResult.Error!.Status);

            // Delete
            var deleteResult = await serviceB.DeleteConnectionAsync(connectionA.Id);
            Assert.True(deleteResult.IsFailure);
            Assert.Equal(404, deleteResult.Error!.Status);

            // List
            var listResult = await serviceB.GetConnectionsAsync();
            Assert.True(listResult.IsSuccess);
            Assert.DoesNotContain(listResult.Value!, c => c.Id == connectionA.Id);
        }
    }

    [Fact]
    public async Task CreateConnection_WithDuplicateExternalAccount_ReturnsConflict()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "conn-dup");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var service = CreateService(db, accessor);

        var first = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.WhatsApp,
            ExternalAccountId: "+9779811111111",
            DisplayName: "Line 1"));
        Assert.True(first.IsSuccess);

        var duplicate = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.WhatsApp,
            ExternalAccountId: "+9779811111111",
            DisplayName: "Line 2 (Duplicate)"));

        Assert.True(duplicate.IsFailure);
        Assert.Equal(409, duplicate.Error!.Status);
    }

    [Fact]
    public async Task CreateConnection_WithStoreBinding_EnforcesStoreOwnership()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "conn-store-a");
        var tenantB = await CreateTenantAsync(db, "conn-store-b");

        Store storeA;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            storeA = await CreateStoreAsync(db, tenantA.Id, "store-a");
        }

        Store storeB;
        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            storeB = await CreateStoreAsync(db, tenantB.Id, "store-b");
        }

        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            var serviceA = CreateService(db, accessor);

            // Valid store in same tenant
            var valid = await serviceA.CreateConnectionAsync(new CreateChannelConnectionRequest(
                Channel: ChannelType.Messenger,
                ExternalAccountId: "page_1",
                DisplayName: "Store A Messenger",
                StoreId: storeA.Id));
            Assert.True(valid.IsSuccess);
            Assert.Equal(storeA.Id, valid.Value!.StoreId);

            // Cross-tenant store
            var invalid = await serviceA.CreateConnectionAsync(new CreateChannelConnectionRequest(
                Channel: ChannelType.Messenger,
                ExternalAccountId: "page_2",
                DisplayName: "Store B Messenger",
                StoreId: storeB.Id));
            Assert.True(invalid.IsFailure);
            Assert.Equal(400, invalid.Error!.Status);
            Assert.Contains("store was not found", invalid.Error.Detail);
        }
    }

    [Fact]
    public async Task Disable_And_Enable_Lifecycle_TransitionsCorrectly()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "conn-lifecycle");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var service = CreateService(db, accessor);

        var createResult = await service.CreateConnectionAsync(new CreateChannelConnectionRequest(
            Channel: ChannelType.Simulator,
            ExternalAccountId: "sim_test_1",
            DisplayName: "Sim Channel"));
        Assert.True(createResult.IsSuccess);

        // Disable
        var disableResult = await service.DisableConnectionAsync(createResult.Value!.Id, "Testing disable");
        Assert.True(disableResult.IsSuccess);
        Assert.Equal(ChannelConnectionStatus.Disabled, disableResult.Value!.Status);

        // Enable
        var enableResult = await service.EnableConnectionAsync(createResult.Value.Id);
        Assert.True(enableResult.IsSuccess);
        Assert.Equal(ChannelConnectionStatus.Active, enableResult.Value!.Status);

        // Delete
        var deleteResult = await service.DeleteConnectionAsync(createResult.Value.Id);
        Assert.True(deleteResult.IsSuccess);

        var getResult = await service.GetConnectionByIdAsync(createResult.Value.Id);
        Assert.True(getResult.IsFailure);
        Assert.Equal(404, getResult.Error!.Status);
    }

    [Fact]
    public async Task RBAC_Operator_CannotPerformMutations()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "conn-rbac");

        using (accessor.BeginScope(OperatorContext(tenant.Id)))
        {
            var service = CreateService(db, accessor);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.CreateConnectionAsync(new CreateChannelConnectionRequest(
                    Channel: ChannelType.WhatsApp,
                    ExternalAccountId: "+9779800000002",
                    DisplayName: "Operator Line")));
        }
    }

    private static ChannelConnectionService CreateService(
        AppDbContext db,
        TenantContextAccessor accessor,
        ISecretEncryptionService? encryptionService = null)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("conn-test"), authorizer);
        var enc = encryptionService ?? CreateEncryptionService();
        var registry = new ChannelProviderRegistry(Array.Empty<IChannelProvider>());
        return new ChannelConnectionService(db, accessor, authorizer, enc, audit, registry, new RefusingInstagramGraphClient());
    }

    private static AesGcmSecretEncryptionService CreateEncryptionService(
        Dictionary<string, string>? keys = null,
        string defaultVersion = "v1")
    {
        var masterKey = (keys != null && keys.TryGetValue(defaultVersion, out var key) && !string.IsNullOrWhiteSpace(key))
            ? key
            : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = masterKey,
            DefaultKeyVersion = defaultVersion,
            VersionedKeys = keys ?? []
        });

        return new AesGcmSecretEncryptionService(options);
    }

    private static TenantContext OwnerContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private static TenantContext OperatorContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000003", "01J00000000000000000000004", TenantRole.Operator);

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Store> CreateStoreAsync(AppDbContext db, string tenantId, string prefix)
    {
        var store = Store.Create(tenantId, new StoreSettings(
            "Test Store",
            $"{prefix}-{Guid.NewGuid():N}"[..20],
            null,
            StoreThemePreset.Default,
            null,
            "Owner",
            "owner@example.com",
            null, null, null, null, null,
            "Terms", "Privacy", "Returns", "Payment"));
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }
}
