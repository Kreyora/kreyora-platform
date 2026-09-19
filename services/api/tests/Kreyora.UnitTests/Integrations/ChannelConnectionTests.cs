using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class ChannelConnectionTests
{
    [Fact]
    public void Create_WithValidParameters_InitializesCorrectly()
    {
        var secret = new EncryptedSecret("ciphertext_1", "iv_1", "tag_1", "v1");
        var connection = ChannelConnection.Create(
            tenantId: "tenant_123",
            channel: ChannelType.WhatsApp,
            externalAccountId: "+9779800000000",
            displayName: "Support WhatsApp",
            storeId: "store_abc",
            encryptedCredentials: secret,
            webhookVerificationToken: "wh_tok_xyz",
            tokenExpiresAt: DateTimeOffset.UtcNow.AddDays(30));

        Assert.Equal("tenant_123", connection.TenantId);
        Assert.Equal(ChannelType.WhatsApp, connection.Channel);
        Assert.Equal("+9779800000000", connection.ExternalAccountId);
        Assert.Equal("Support WhatsApp", connection.DisplayName);
        Assert.Equal("store_abc", connection.StoreId);
        Assert.Equal(ChannelConnectionStatus.Active, connection.Status);
        Assert.Same(secret, connection.EncryptedCredentials);
        Assert.Equal("wh_tok_xyz", connection.WebhookVerificationToken);
        Assert.NotNull(connection.TokenExpiresAt);
        Assert.NotNull(connection.LastValidatedAt);
    }

    [Theory]
    [InlineData("", "+9779800000000", "Support Line")]
    [InlineData("tenant_1", "", "Support Line")]
    [InlineData("tenant_1", "+9779800000000", "")]
    public void Create_WithMissingRequiredFields_ThrowsArgumentException(
        string tenantId,
        string externalAccountId,
        string displayName)
    {
        Assert.ThrowsAny<ArgumentException>(() => ChannelConnection.Create(
            tenantId: tenantId,
            channel: ChannelType.WhatsApp,
            externalAccountId: externalAccountId,
            displayName: displayName));
    }

    [Fact]
    public void UpdateMetadata_UpdatesDisplayNameAndStoreId()
    {
        var connection = ChannelConnection.Create("tenant_1", ChannelType.Simulator, "sim_1", "Old Name");

        connection.UpdateMetadata("New Name", "store_99");

        Assert.Equal("New Name", connection.DisplayName);
        Assert.Equal("store_99", connection.StoreId);
    }

    [Fact]
    public void UpdateCredentials_UpdatesSecretAndResetsStatusIfDegradedOrExpired()
    {
        var connection = ChannelConnection.Create("tenant_1", ChannelType.WhatsApp, "+9779800000000", "Line 1");
        connection.Disable();
        connection.Revoke();
        Assert.Equal(ChannelConnectionStatus.Revoked, connection.Status);

        var newSecret = new EncryptedSecret("c2", "i2", "t2", "v2");
        connection.UpdateCredentials(newSecret);

        Assert.Same(newSecret, connection.EncryptedCredentials);
        Assert.Equal(ChannelConnectionStatus.Active, connection.Status);
        Assert.NotNull(connection.LastRefreshedAt);
    }

    [Fact]
    public void RotateSecret_AppliesReEncryptionFuncAndUpdatesModifiedAt()
    {
        var originalSecret = new EncryptedSecret("old_cipher", "old_iv", "old_tag", "v1");
        var connection = ChannelConnection.Create(
            "tenant_1",
            ChannelType.WhatsApp,
            "+9779800000000",
            "Line 1",
            encryptedCredentials: originalSecret);

        connection.RotateSecret((old, targetVersion) =>
        {
            Assert.Equal("v1", old.KeyVersion);
            Assert.Equal("v2", targetVersion);
            return new EncryptedSecret("new_cipher", "new_iv", "new_tag", targetVersion);
        }, "v2");

        Assert.NotNull(connection.EncryptedCredentials);
        Assert.Equal("new_cipher", connection.EncryptedCredentials.CiphertextBase64);
        Assert.Equal("v2", connection.EncryptedCredentials.KeyVersion);
    }

    [Fact]
    public void RotateSecret_WithoutExistingCredentials_ThrowsInvalidOperationException()
    {
        var connection = ChannelConnection.Create("tenant_1", ChannelType.Simulator, "sim_1", "Sim 1");

        Assert.Throws<InvalidOperationException>(() =>
            connection.RotateSecret((old, v) => old, "v2"));
    }

    [Fact]
    public void Disable_And_Enable_TransitionsStatusCorrectly()
    {
        var connection = ChannelConnection.Create("tenant_1", ChannelType.Simulator, "sim_1", "Sim 1");

        connection.Disable("Maintenance window");
        Assert.Equal(ChannelConnectionStatus.Disabled, connection.Status);
        Assert.Equal("Maintenance window", connection.HealthSummary);

        connection.Enable();
        Assert.Equal(ChannelConnectionStatus.Active, connection.Status);
    }

    [Fact]
    public void Enable_OnRevokedConnection_ThrowsInvalidOperationException()
    {
        var connection = ChannelConnection.Create("tenant_1", ChannelType.Simulator, "sim_1", "Sim 1");
        connection.Revoke("Security incident");

        Assert.Throws<InvalidOperationException>(() => connection.Enable());
    }

    [Fact]
    public void Enable_OnExpiredConnection_ThrowsInvalidOperationException()
    {
        var connection = ChannelConnection.Create(
            "tenant_1",
            ChannelType.Simulator,
            "sim_1",
            "Sim 1",
            tokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Throws<InvalidOperationException>(() => connection.Enable());
        Assert.Equal(ChannelConnectionStatus.Expired, connection.Status);
    }

    [Fact]
    public void UpdateHealth_UpdatesStatusAndSummary()
    {
        var connection = ChannelConnection.Create("tenant_1", ChannelType.Simulator, "sim_1", "Sim 1");
        var now = DateTimeOffset.UtcNow;

        connection.UpdateHealth(
            isHealthy: false,
            status: ChannelConnectionStatus.Degraded,
            summary: "Rate limited by upstream",
            details: "HTTP 429 Too Many Requests",
            checkedAt: now);

        Assert.Equal(ChannelConnectionStatus.Degraded, connection.Status);
        Assert.Equal("Rate limited by upstream", connection.HealthSummary);
        Assert.Equal("HTTP 429 Too Many Requests", connection.HealthDetails);
        Assert.Equal(now, connection.LastHealthCheckAt);
    }
}

