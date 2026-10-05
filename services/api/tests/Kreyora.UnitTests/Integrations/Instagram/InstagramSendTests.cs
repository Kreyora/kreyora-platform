using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Microsoft.Extensions.Options;

namespace Kreyora.UnitTests.Integrations.Instagram;

/// <summary>M08-S05 Send API client and provider send mapping (ADR-017), with stubbed HTTP only.</summary>
public sealed class InstagramSendTests
{
    [Fact]
    public async Task SendText_PostsToMeMessages_WithBearerToken_AndResponseMessagingType()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var client = Client(async (request, ct) =>
        {
            seen = request;
            body = await request.Content!.ReadAsStringAsync(ct);
            return Json("{\"recipient_id\":\"igsid_c\",\"message_id\":\"mid_sent_1\"}");
        });

        var result = await client.SendTextAsync("secret_page_token", "igsid_c", "Hello there");

        Assert.True(result.IsSent);
        Assert.Equal("mid_sent_1", result.MessageId);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("https://graph.facebook.com/v21.0/me/messages", seen.RequestUri!.ToString());
        Assert.Equal("Bearer", seen.Headers.Authorization!.Scheme);
        Assert.Equal("secret_page_token", seen.Headers.Authorization.Parameter);
        Assert.DoesNotContain("secret_page_token", seen.RequestUri.ToString());
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("igsid_c", json.RootElement.GetProperty("recipient").GetProperty("id").GetString());
        Assert.Equal("RESPONSE", json.RootElement.GetProperty("messaging_type").GetString());
        Assert.Equal("Hello there", json.RootElement.GetProperty("message").GetProperty("text").GetString());
        Assert.False(json.RootElement.TryGetProperty("tag", out _));
    }

    [Fact]
    public async Task SendText_WithTag_UsesMessageTagType()
    {
        string? body = null;
        var client = Client(async (request, ct) =>
        {
            body = await request.Content!.ReadAsStringAsync(ct);
            return Json("{\"message_id\":\"mid_tag\"}");
        });

        await client.SendTextAsync("token", "igsid_c", "Late reply", "HUMAN_AGENT");

        using var json = JsonDocument.Parse(body!);
        Assert.Equal("MESSAGE_TAG", json.RootElement.GetProperty("messaging_type").GetString());
        Assert.Equal("HUMAN_AGENT", json.RootElement.GetProperty("tag").GetString());
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":190}}", HttpStatusCode.BadRequest, InstagramSendOutcome.TokenExpired, "190")]
    [InlineData("{\"error\":{\"code\":613}}", HttpStatusCode.BadRequest, InstagramSendOutcome.Transient, "613")]
    [InlineData("{\"error\":{\"code\":2,\"is_transient\":true}}", HttpStatusCode.InternalServerError, InstagramSendOutcome.Transient, "2")]
    [InlineData("{}", HttpStatusCode.TooManyRequests, InstagramSendOutcome.Transient, "429")]
    [InlineData("{\"error\":{\"code\":10,\"error_subcode\":2018278}}", HttpStatusCode.BadRequest, InstagramSendOutcome.Rejected, "10/2018278")]
    [InlineData("{\"error\":{\"code\":551}}", HttpStatusCode.BadRequest, InstagramSendOutcome.Rejected, "551")]
    [InlineData("{\"error\":{\"code\":10,\"error_subcode\":1545041}}", HttpStatusCode.BadRequest, InstagramSendOutcome.Rejected, "10/1545041")]
    [InlineData("not json", HttpStatusCode.BadGateway, InstagramSendOutcome.Unconfirmed, "502")]
    public async Task SendText_MapsProviderErrors(string payload, HttpStatusCode status, InstagramSendOutcome expected, string code)
    {
        var client = Client((_, _) => Task.FromResult(Json(payload, status)));

        var result = await client.SendTextAsync("token", "igsid_c", "hi");

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(code, result.ProviderErrorCode);
    }

    [Fact]
    public async Task SendText_Timeout_IsUnconfirmed_NotRetryable()
    {
        var client = Client((_, _) => throw new TaskCanceledException("timed out"));

        var result = await client.SendTextAsync("token", "igsid_c", "hi");

        Assert.Equal(InstagramSendOutcome.Unconfirmed, result.Outcome);
    }

    [Fact]
    public async Task SendText_ConnectionRefused_IsTransient_BecauseNothingWasSent()
    {
        var client = Client((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var result = await client.SendTextAsync("token", "igsid_c", "hi");

        Assert.Equal(InstagramSendOutcome.Transient, result.Outcome);
    }

    [Fact]
    public async Task SendText_ResetAfterSending_IsUnconfirmed()
    {
        var client = Client((_, _) => throw new HttpRequestException(HttpRequestError.ResponseEnded, "reset"));

        var result = await client.SendTextAsync("token", "igsid_c", "hi");

        Assert.Equal(InstagramSendOutcome.Unconfirmed, result.Outcome);
    }

    [Fact]
    public async Task SendText_SuccessWithoutMessageId_IsUnconfirmed()
    {
        var client = Client((_, _) => Task.FromResult(Json("{\"recipient_id\":\"igsid_c\"}")));

        var result = await client.SendTextAsync("token", "igsid_c", "hi");

        Assert.Equal(InstagramSendOutcome.Unconfirmed, result.Outcome);
    }

    [Fact]
    public async Task SendText_BlankToken_FailsWithoutHttpCall()
    {
        var calls = 0;
        var client = Client((_, _) =>
        {
            calls++;
            return Task.FromResult(Json("{}"));
        });

        var result = await client.SendTextAsync(" ", "igsid_c", "hi");

        Assert.Equal(InstagramSendOutcome.TokenExpired, result.Outcome);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(InstagramSendOutcome.Transient, null, true)]
    [InlineData(InstagramSendOutcome.TokenExpired, "190", null)]
    [InlineData(InstagramSendOutcome.Unconfirmed, "delivery_unconfirmed", null)]
    [InlineData(InstagramSendOutcome.Rejected, "551", null)]
    public async Task Provider_MapsSendOutcomes_ToDeliveryResults(InstagramSendOutcome outcome, string? expectedCode, bool? transient)
    {
        var encryption = Encryption();
        var graph = new StubGraph(InstagramSendResult.Failed(outcome, outcome == InstagramSendOutcome.Rejected ? "551" : "x", "failed"));
        var provider = new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions()), null, graph, encryption);

        var result = await provider.SendMessageAsync(Connection(encryption), OutboundMessageRequest.TextMessage("igsid_c", "hi"));

        Assert.False(result.Succeeded);
        Assert.Equal(transient, result.IsTransient);
        if (expectedCode is not null)
        {
            Assert.Equal(expectedCode, result.ProviderErrorCode);
        }
    }

    [Fact]
    public async Task Provider_Success_DecryptsTokenAndPassesGateTag()
    {
        var encryption = Encryption();
        var graph = new StubGraph(InstagramSendResult.Sent("mid_ok"));
        var provider = new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions()), null, graph, encryption);
        var request = OutboundMessageRequest.TextMessage("igsid_c", "hi") with
        {
            Metadata = new Dictionary<string, string> { [ConversationGateResult.MessagingTagMetadataKey] = "HUMAN_AGENT" }
        };

        var result = await provider.SendMessageAsync(Connection(encryption), request);

        Assert.True(result.Succeeded);
        Assert.Equal("mid_ok", result.ProviderMessageId);
        Assert.Equal("page_token_plain", graph.LastToken);
        Assert.Equal("HUMAN_AGENT", graph.LastTag);
    }

    [Fact]
    public async Task Provider_MediaOrUnconfigured_FailsWithoutCallingGraph()
    {
        var encryption = Encryption();
        var graph = new StubGraph(InstagramSendResult.Sent("never"));
        var provider = new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions()), null, graph, encryption);
        var unconfigured = new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions()));

        var media = await provider.SendMessageAsync(Connection(encryption),
            OutboundMessageRequest.TextMessage("igsid_c", "caption") with { MediaUrl = "https://cdn.example/a.jpg" });
        var noHost = await unconfigured.SendMessageAsync(Connection(encryption), OutboundMessageRequest.TextMessage("igsid_c", "hi"));

        Assert.Equal(ConversationDenialReasons.CapabilityUnsupported, media.ProviderErrorCode);
        Assert.Equal("provider_unconfigured", noHost.ProviderErrorCode);
        Assert.Equal(0, graph.Calls);
    }

    private static ChannelConnectionSnapshot Connection(AesGcmSecretEncryptionService encryption) =>
        ChannelConnectionSnapshot.Create("conn_1", "tenant_1", null, ChannelType.Instagram,
            externalAccountId: "igid_1", encryptedCredentials: encryption.Encrypt("page_token_plain"));

    private static AesGcmSecretEncryptionService Encryption() =>
        new(Options.Create(new SecretEncryptionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            DefaultKeyVersion = "v1",
            VersionedKeys = []
        }));

    private static InstagramGraphClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        new(new HttpClient(new FuncHandler(responder)), Options.Create(new InstagramGraphOptions()));

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FuncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class StubGraph(InstagramSendResult result) : IInstagramGraphClient
    {
        public int Calls { get; private set; }
        public string? LastToken { get; private set; }
        public string? LastTag { get; private set; }

        public Task<InstagramValidationResult> ValidatePageLinkAsync(string pageAccessToken, string pageId, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstagramValidationResult> ValidateAccountAsync(string pageAccessToken, string instagramAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstagramSendResult> SendTextAsync(string pageAccessToken, string recipientId, string text, string? messagingTag = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = pageAccessToken;
            LastTag = messagingTag;
            return Task.FromResult(result);
        }
    }
}
