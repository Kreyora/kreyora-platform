using System.Security.Cryptography;
using System.Text;
using Kreyora.Application.Integrations;
using Kreyora.Infrastructure.Integrations.Simulator;

namespace Kreyora.UnitTests.Integrations;

public sealed class SimulatorChannelProviderTests
{
    // Development/Testing behavior; the production default is covered by the FixedTestSignature tests below.
    private readonly SimulatorChannelProvider _provider = new(acceptsFixedTestSignature: true);

    [Fact]
    public async Task ValidateWebhookAsync_WithDefaultValidSignature_ReturnsSuccess()
    {
        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_100\",\"text\":\"hello\"}",
            headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature
            });

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorReason);
        Assert.Equal("evt_100", result.ProviderEventId);
    }

    [Fact]
    public async Task FixedTestSignature_IsRejectedByDefault()
    {
        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_forged\",\"text\":\"forged\"}",
            headers: new Dictionary<string, string> { ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature },
            secret: "connection_secret_1234567890");

        var result = await new SimulatorChannelProvider().ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task FixedTestSignature_IsRejectedByDefault_EvenWithoutAConnectionSecret()
    {
        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_forged\",\"text\":\"forged\"}",
            headers: new Dictionary<string, string> { ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature });

        var result = await new SimulatorChannelProvider().ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HmacSignature_IsStillAcceptedByDefault()
    {
        const string secret = "connection_secret_1234567890";
        const string rawBody = "{\"id\":\"evt_real\",\"text\":\"real\"}";
        var request = WebhookValidationRequest.Create(
            rawBody: rawBody,
            headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.GenerateValidSignature(System.Text.Encoding.UTF8.GetBytes(rawBody), secret)
            },
            secret: secret);

        var result = await new SimulatorChannelProvider().ValidateWebhookAsync(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void FixedTestSignature_IsOnlyEnabledInDevelopmentAndTesting(string environmentName, bool expected)
    {
        var environment = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = environmentName };

        Assert.Equal(expected, SimulatorChannelProvider.AcceptsFixedTestSignatureIn(environment));
    }

    [Fact]
    public async Task ValidateWebhookAsync_WithHmacSignature_ReturnsSuccess()
    {
        var secret = "super_secret_signing_key_123456789";
        var rawBody = "{\"id\":\"evt_hmac_1\",\"text\":\"hmac message\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(bodyBytes));

        var request = WebhookValidationRequest.Create(
            rawBody: rawBody,
            headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = signature
            },
            secret: secret);

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.True(result.IsValid);
        Assert.Equal("evt_hmac_1", result.ProviderEventId);
    }

    [Fact]
    public async Task ValidateWebhookAsync_WithMismatchedHmacSignature_ReturnsFailed()
    {
        var secret = "secret_key";
        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_1\"}",
            headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = "sha256=invalid_hash"
            },
            secret: secret);

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("Invalid signature header", result.ErrorReason);
    }

    [Fact]
    public async Task ValidateWebhookAsync_WithMissingSignature_ReturnsFailed()
    {
        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_1\"}",
            headers: new Dictionary<string, string>());

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("Missing signature header", result.ErrorReason);
    }

    [Fact]
    public async Task ValidateWebhookAsync_WithExpiredTimestamp_ReturnsFailed()
    {
        var expiredEpoch = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_1\"}",
            headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature,
                ["X-Hub-Timestamp"] = expiredEpoch
            });

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("Timestamp outside replay window", result.ErrorReason);
    }

    [Fact]
    public async Task ValidateWebhookAsync_WithValidTimestamp_ReturnsSuccess()
    {
        var freshEpoch = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_fresh\"}",
            headers: new Dictionary<string, string>
            {
                ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature,
                ["X-Hub-Timestamp"] = freshEpoch
            });

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.True(result.IsValid);
        Assert.Equal("evt_fresh", result.ProviderEventId);
    }

    [Fact]
    public async Task ValidateWebhookAsync_WithValidChallenge_ReturnsChallengeResponse()
    {
        var request = new WebhookValidationRequest(
            Method: "GET",
            Path: "/webhook",
            Headers: new Dictionary<string, string>(),
            QueryParameters: new Dictionary<string, string>
            {
                ["hub.mode"] = "subscribe",
                ["hub.verify_token"] = "custom_token_123",
                ["hub.challenge"] = "challenge_code_98765"
            },
            RawBody: Array.Empty<byte>(),
            Secret: "custom_token_123");

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.True(result.IsValid);
        Assert.Equal("challenge_code_98765", result.ChallengeResponse);
    }

    [Fact]
    public async Task ValidateWebhookAsync_WithMismatchedChallengeToken_ReturnsFailed()
    {
        var request = new WebhookValidationRequest(
            Method: "GET",
            Path: "/webhook",
            Headers: new Dictionary<string, string>(),
            QueryParameters: new Dictionary<string, string>
            {
                ["hub.mode"] = "subscribe",
                ["hub.verify_token"] = "wrong_token",
                ["hub.challenge"] = "challenge_code_98765"
            },
            RawBody: Array.Empty<byte>(),
            Secret: "expected_token");

        var result = await _provider.ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal("Verification token mismatch", result.ErrorReason);
    }
}
