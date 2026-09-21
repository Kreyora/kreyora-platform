using System.Security.Cryptography;
using System.Text;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations.Simulator;

namespace Kreyora.UnitTests.Integrations;

public sealed class IntegrationDiagnosticsTests
{
    private readonly SimulatorChannelProvider _provider = new();

    [Fact]
    public void GenerateValidSignature_WithNullSecret_ReturnsDefaultSignature()
    {
        var sig = SimulatorChannelProvider.GenerateValidSignature("test"u8.ToArray(), null);
        Assert.Equal(SimulatorChannelProvider.DefaultValidSignature, sig);
    }

    [Fact]
    public void GenerateValidSignature_WithSecret_ReturnsComputedHmac()
    {
        var body = "{\"hello\":\"world\"}"u8.ToArray();
        var secret = "my_super_secret";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(body));

        var actual = SimulatorChannelProvider.GenerateValidSignature(body, secret);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateSignedWebhookRequest_SetsHeadersCorrectly()
    {
        var rawBody = "{\"test\":123}";
        var secret = "test_sec";
        var eventId = "evt_custom_456";
        var accountId = "act_custom_789";

        var req = SimulatorChannelProvider.CreateSignedWebhookRequest(
            rawBody: rawBody,
            secret: secret,
            eventId: eventId,
            accountId: accountId,
            latencyMs: 50);

        Assert.Equal("POST", req.Method);
        Assert.Equal(secret, req.Secret);
        Assert.Equal(eventId, req.Headers["X-Provider-Event-Id"]);
        Assert.Equal(accountId, req.Headers["X-External-Account-Id"]);
        Assert.Equal("50", req.Headers["X-Simulate-Latency-Ms"]);
        Assert.StartsWith("sha256=", req.Headers["X-Hub-Signature-256"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateOrRefreshConnectionAsync_HealthyConnection_ReturnsHealthy()
    {
        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator,
            status: ChannelConnectionStatus.Active,
            externalAccountId: "normal_account");

        var health = await _provider.ValidateOrRefreshConnectionAsync(snapshot);

        Assert.True(health.IsHealthy);
        Assert.Equal(ChannelConnectionStatus.Active, health.Status);
    }

    [Fact]
    public async Task ValidateOrRefreshConnectionAsync_ExpiredStatus_ReturnsExpired()
    {
        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator,
            status: ChannelConnectionStatus.Expired,
            externalAccountId: "normal_account");

        var health = await _provider.ValidateOrRefreshConnectionAsync(snapshot);

        Assert.False(health.IsHealthy);
        Assert.Equal(ChannelConnectionStatus.Expired, health.Status);
        Assert.Contains("expired", health.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateOrRefreshConnectionAsync_SimulateExpiredAccount_ReturnsExpired()
    {
        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator,
            status: ChannelConnectionStatus.Active,
            externalAccountId: "sim_act_simulate_expired");

        var health = await _provider.ValidateOrRefreshConnectionAsync(snapshot);

        Assert.False(health.IsHealthy);
        Assert.Equal(ChannelConnectionStatus.Expired, health.Status);
    }

    [Fact]
    public async Task ValidateOrRefreshConnectionAsync_SimulateDegradedAccount_ReturnsDegraded()
    {
        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator,
            status: ChannelConnectionStatus.Active,
            externalAccountId: "sim_act_simulate_degraded");

        var health = await _provider.ValidateOrRefreshConnectionAsync(snapshot);

        Assert.False(health.IsHealthy);
        Assert.Equal(ChannelConnectionStatus.Degraded, health.Status);
    }

    [Fact]
    public async Task ValidateOrRefreshConnectionAsync_SimulateRevokedAccount_ReturnsRevoked()
    {
        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator,
            status: ChannelConnectionStatus.Active,
            externalAccountId: "sim_act_simulate_revoked");

        var health = await _provider.ValidateOrRefreshConnectionAsync(snapshot);

        Assert.False(health.IsHealthy);
        Assert.Equal(ChannelConnectionStatus.Revoked, health.Status);
    }

    [Fact]
    public async Task SendMessageAsync_WithRateLimitTrigger_Throws429HttpRequestException()
    {
        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator);

        var msg = OutboundMessageRequest.TextMessage("+9779800000429", "Hello [SIMULATE_RATE_LIMIT]");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _provider.SendMessageAsync(snapshot, msg));

        Assert.Contains("429", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageAsync_WithSimulatedLatency_ExecutesSuccessfully()
    {
        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: "conn_1",
            tenantId: "tenant_1",
            storeId: null,
            channel: ChannelType.Simulator);

        var msg = new OutboundMessageRequest(
            MessageId: "msg_lat_1",
            ConversationId: "conv_1",
            RecipientChannelId: "+9779800000001",
            Text: "Hello with latency",
            Metadata: new Dictionary<string, string> { ["simulate_latency_ms"] = "20" });

        var start = DateTimeOffset.UtcNow;
        var res = await _provider.SendMessageAsync(snapshot, msg);
        var elapsed = DateTimeOffset.UtcNow - start;

        Assert.True(res.Succeeded);
        Assert.True(elapsed.TotalMilliseconds >= 15);
    }
}

