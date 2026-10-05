using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Microsoft.Extensions.Options;

namespace Kreyora.UnitTests.Integrations.Instagram;

public sealed class InstagramChannelProviderTests
{
    private const string TestSecret = "test_app_secret_ig";
    private const string TestVerifyToken = "test_app_verify_token";

    [Fact]
    public void Channel_IsInstagram_WithEvidencedCapabilities()
    {
        var provider = CreateProvider();

        Assert.Equal(ChannelType.Instagram, provider.Channel);
        Assert.Equal(ChannelCapabilities.InstagramGraphApi(), provider.Capabilities);
        Assert.False(provider.Capabilities.SupportsDeliveryReceipts);
        Assert.True(provider.Capabilities.SupportsReadReceipts);
    }

    [Theory]
    [InlineData("subscribe", TestVerifyToken, "challenge_123", true)]
    [InlineData("subscribe", "wrong_token", "challenge_123", false)]
    [InlineData("unsubscribe", TestVerifyToken, "challenge_123", false)]
    public async Task ValidateWebhook_Challenge_MatchesAppVerifyToken(
        string mode, string token, string challenge, bool expectedValid)
    {
        var provider = CreateProvider();

        var result = await provider.ValidateWebhookAsync(ChallengeRequest(mode, token, challenge, connectionSecret: null));

        Assert.Equal(expectedValid, result.IsValid);
        Assert.Equal(expectedValid ? challenge : null, result.ChallengeResponse);
    }

    [Fact]
    public async Task ValidateWebhook_Challenge_IgnoresConnectionScopedSecret()
    {
        var provider = CreateProvider();

        var result = await provider.ValidateWebhookAsync(
            ChallengeRequest("subscribe", "connection_token", "c1", connectionSecret: "connection_token"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateWebhook_Challenge_UnconfiguredVerifyToken_FailsClosed()
    {
        var provider = new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions { AppSecret = TestSecret }));

        var result = await provider.ValidateWebhookAsync(ChallengeRequest("subscribe", "anything", "c1", connectionSecret: null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Provider_DeclaresAppLevelSignatureAnd200Acknowledgement()
    {
        var provider = CreateProvider();

        Assert.False(provider.UsesConnectionSecretForSignature);
        Assert.Equal(200, provider.AcknowledgementStatusCode);
    }

    [Fact]
    public async Task ValidateWebhook_SignedPost_ReturnsBodyHashEventIdAndAccountId()
    {
        var provider = CreateProvider();
        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_9\",\"time\":1729500000000,\"messaging\":[]}]}";

        var result = await provider.ValidateWebhookAsync(SignedRequest(body));

        Assert.True(result.IsValid);
        Assert.StartsWith("ig_", result.ProviderEventId);
        Assert.Equal("igsid_9", result.ExternalAccountId);
    }

    [Fact]
    public async Task ValidateWebhook_WrongSecret_FailsWithoutEvent()
    {
        var provider = CreateProvider();
        var request = SignedRequest("{\"object\":\"instagram\",\"entry\":[]}", secret: "other_secret");

        var result = await provider.ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
        Assert.Null(result.ProviderEventId);
    }

    [Fact]
    public async Task ValidateWebhook_MissingSignature_Fails()
    {
        var provider = CreateProvider();
        var request = WebhookValidationRequest.Create(
            rawBody: "{}",
            headers: new Dictionary<string, string> { ["content-type"] = "application/json" },
            secret: "verify_me");

        var result = await provider.ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateWebhook_UnconfiguredSecret_FailsClosed()
    {
        var provider = new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions()));
        var request = SignedRequest("{\"object\":\"instagram\",\"entry\":[]}");

        var result = await provider.ValidateWebhookAsync(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Normalize_TextMessage_MapsToV1Envelope()
    {
        var provider = CreateProvider();
        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igid_1\",\"time\":1729500000000,\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_sender\"},\"recipient\":{\"id\":\"igid_1\"}," +
            "\"timestamp\":1729500000123,\"message\":{\"mid\":\"mid_text_1\",\"text\":\"price?\"}}]}]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        var envelope = Assert.Single(envelopes);
        Assert.Equal("v1", envelope.SchemaVersion);
        Assert.Equal(ChannelType.Instagram, envelope.Channel);
        Assert.Equal("tenant_1", envelope.TenantId);
        var text = Assert.IsType<TextMessageReceivedPayload>(envelope.Payload);
        Assert.Equal("mid_text_1", text.MessageId);
        Assert.Equal("igsid_sender", text.SenderChannelId);
        Assert.Equal("price?", text.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1729500000123), text.Timestamp);
    }

    [Fact]
    public async Task Normalize_QuickReply_MapsPayloadAsText()
    {
        var provider = CreateProvider();
        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igid_1\",\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_q\"},\"timestamp\":1729500000123," +
            "\"message\":{\"mid\":\"mid_q_1\",\"quick_reply\":{\"payload\":\"SIZE_M\"}}}]}]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        var text = Assert.IsType<TextMessageReceivedPayload>(Assert.Single(envelopes).Payload);
        Assert.Equal("SIZE_M", text.Text);
    }

    [Fact]
    public async Task Normalize_MediaAttachment_MapsTypeAndUrl()
    {
        var provider = CreateProvider();
        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igid_1\",\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_m\"},\"timestamp\":1729500000123," +
            "\"message\":{\"mid\":\"mid_media_1\",\"attachments\":[{\"type\":\"image\",\"payload\":{\"url\":\"https://cdn.example/i.jpg\"}}]}}]}]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        var media = Assert.IsType<MediaMessageReceivedPayload>(Assert.Single(envelopes).Payload);
        Assert.Equal("mid_media_1", media.MessageId);
        Assert.Equal("https://cdn.example/i.jpg", media.MediaUrl);
        Assert.Equal("image", media.ContentType);
    }

    [Fact]
    public async Task Normalize_Seen_MapsToReadReceiptWithoutDelivered()
    {
        var provider = CreateProvider();
        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igid_1\",\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_s\"},\"timestamp\":1729500000123," +
            "\"read\":{\"mid\":\"mid_out_1\"}}]}]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        var status = Assert.IsType<MessageStatusUpdatedPayload>(Assert.Single(envelopes).Payload);
        Assert.Equal("mid_out_1", status.MessageId);
        Assert.Equal("igsid_s", status.RecipientChannelId);
        Assert.Equal(MessageDeliveryStatus.Read, status.Status);
    }

    [Fact]
    public async Task Normalize_Reaction_MapsEmojiAndRemoval()
    {
        var provider = CreateProvider();
        const string body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igid_1\",\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_r\"},\"timestamp\":1729500000123," +
            "\"reaction\":{\"mid\":\"mid_t_1\",\"action\":\"react\",\"emoji\":\"❤\"}}]}]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        var reaction = Assert.IsType<ReactionReceivedPayload>(Assert.Single(envelopes).Payload);
        Assert.Equal("mid_t_1", reaction.MessageId);
        Assert.Equal("❤", reaction.Emoji);
        Assert.False(reaction.IsRemoved);
    }

    [Theory]
    [InlineData("\"is_echo\":true")]
    [InlineData("\"is_deleted\":true")]
    [InlineData("\"is_unsupported\":true")]
    public async Task Normalize_SystemFlags_ProduceNoEnvelopes(string flag)
    {
        var provider = CreateProvider();
        var body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igid_1\",\"messaging\":[{" +
            "\"sender\":{\"id\":\"igsid_x\"},\"timestamp\":1729500000123," +
            "\"message\":{\"mid\":\"mid_sys_1\",\"text\":\"hi\"," + flag + "}}]}]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        Assert.Empty(envelopes);
    }

    [Fact]
    public async Task Normalize_NonInstagramObject_ProduceNoEnvelopes()
    {
        var provider = CreateProvider();
        const string body = "{\"object\":\"page\",\"entry\":[]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        Assert.Empty(envelopes);
    }

    [Fact]
    public void ResolveExternalAccountId_ReadsFirstEntryId()
    {
        var provider = CreateProvider();
        using var document = JsonDocument.Parse("{\"object\":\"instagram\",\"entry\":[{\"id\":\"igsid_z\"}]}");

        Assert.Equal("igsid_z", provider.ResolveExternalAccountId(document));
    }

    [Fact]
    public void SplitByAccount_GroupsEntriesPerAccount_PreservingOriginalEntryJson()
    {
        var provider = CreateProvider();
        const string entryA1 = "{\"id\":\"igid_a\",\"time\":1,\"messaging\":[]}";
        const string entryB = "{\"id\":\"igid_b\",\"time\":2,\"messaging\":[]}";
        const string entryA2 = "{\"id\":\"igid_a\",\"time\":3,\"messaging\":[]}";
        using var document = JsonDocument.Parse(
            "{\"object\":\"instagram\",\"entry\":[" + entryA1 + "," + entryB + "," + entryA2 + ",{\"time\":4}]}");

        var slices = provider.SplitByAccount(document)!;

        Assert.Equal(2, slices.Count);
        Assert.Equal("igid_a", slices[0].ExternalAccountId);
        Assert.Equal("{\"object\":\"instagram\",\"entry\":[" + entryA1 + "," + entryA2 + "]}", slices[0].RawBody);
        Assert.Equal("igid_b", slices[1].ExternalAccountId);
        Assert.Equal("{\"object\":\"instagram\",\"entry\":[" + entryB + "]}", slices[1].RawBody);
    }

    [Fact]
    public async Task Normalize_SkipsEntriesForOtherAccounts_WhenConnectionAccountIsKnown()
    {
        var provider = CreateProvider();
        var body = "{\"object\":\"instagram\",\"entry\":[" +
            Entry("igid_mine", MessageItem("mid_mine", "hello")) + "," +
            Entry("igid_other", MessageItem("mid_other", "not yours")) + "]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body) with { ExternalAccountId = "igid_mine" });

        var text = Assert.IsType<TextMessageReceivedPayload>(Assert.Single(envelopes).Payload);
        Assert.Equal("mid_mine", text.MessageId);
    }

    [Fact]
    public async Task Normalize_SeenReactUnreact_OnOneMessage_HaveDistinctDeduplicationKeys()
    {
        var provider = CreateProvider();
        var body = "{\"object\":\"instagram\",\"entry\":[" + Entry("igid_1",
            "{\"sender\":{\"id\":\"igsid_c\"},\"timestamp\":1000,\"read\":{\"mid\":\"mid_biz\"}}," +
            "{\"sender\":{\"id\":\"igsid_c\"},\"timestamp\":2000,\"reaction\":{\"mid\":\"mid_biz\",\"action\":\"react\",\"reaction\":\"love\",\"emoji\":\"❤\"}}," +
            "{\"sender\":{\"id\":\"igsid_c\"},\"timestamp\":3000,\"reaction\":{\"mid\":\"mid_biz\",\"action\":\"unreact\"}}") + "]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        Assert.Equal(3, envelopes.Count);
        Assert.Equal(3, envelopes.Select(e => e.DeduplicationKey).Distinct().Count());
        Assert.All(envelopes, e => Assert.False(string.IsNullOrWhiteSpace(e.DeduplicationKey)));
        var unreact = Assert.IsType<ReactionReceivedPayload>(envelopes[2].Payload);
        Assert.True(unreact.IsRemoved);
        Assert.Equal("mid_biz", unreact.MessageId);
    }

    [Fact]
    public async Task Normalize_RedeliveredReaction_KeepsSameDeduplicationKey()
    {
        var provider = CreateProvider();
        var item = "{\"sender\":{\"id\":\"igsid_c\"},\"timestamp\":2000,\"reaction\":{\"mid\":\"mid_biz\",\"action\":\"react\",\"emoji\":\"❤\"}}";

        var first = await provider.NormalizeInboundAsync(Raw("{\"object\":\"instagram\",\"entry\":[" + Entry("igid_1", item, time: 1) + "]}"));
        var second = await provider.NormalizeInboundAsync(Raw("{\"object\":\"instagram\",\"entry\":[" + Entry("igid_1", item, time: 9) + "]}"));

        Assert.Equal(Assert.Single(first).DeduplicationKey, Assert.Single(second).DeduplicationKey);
    }

    [Fact]
    public async Task Normalize_TextWithAttachment_ProducesDistinctPartIdsAndKeys()
    {
        var provider = CreateProvider();
        var body = "{\"object\":\"instagram\",\"entry\":[" + Entry("igid_1",
            "{\"sender\":{\"id\":\"igsid_c\"},\"timestamp\":1000,\"message\":{\"mid\":\"mid_multi\",\"text\":\"look\"," +
            "\"attachments\":[{\"type\":\"image\",\"payload\":{\"url\":\"https://cdn.example/a.jpg\"}}]}}") + "]}";

        var envelopes = await provider.NormalizeInboundAsync(Raw(body));

        Assert.Equal(2, envelopes.Count);
        Assert.Equal("mid_multi#p0", Assert.IsType<TextMessageReceivedPayload>(envelopes[0].Payload).MessageId);
        Assert.Equal("mid_multi#p1", Assert.IsType<MediaMessageReceivedPayload>(envelopes[1].Payload).MessageId);
        Assert.Equal("mid_multi#p0", envelopes[0].DeduplicationKey);
        Assert.Equal("mid_multi#p1", envelopes[1].DeduplicationKey);
    }

    [Fact]
    public async Task Normalize_SingleTextMessage_UsesMessageIdAsLegacyCompatibleKey()
    {
        var provider = CreateProvider();

        var envelope = Assert.Single(await provider.NormalizeInboundAsync(
            Raw("{\"object\":\"instagram\",\"entry\":[" + Entry("igid_1", MessageItem("mid_plain", "hi")) + "]}")));

        Assert.Equal("mid_plain", envelope.DeduplicationKey);
    }

    [Fact]
    public async Task Normalize_Postback_ProducesNoEnvelopes()
    {
        var provider = CreateProvider();
        var body = "{\"object\":\"instagram\",\"entry\":[" + Entry("igid_1",
            "{\"sender\":{\"id\":\"igsid_c\"},\"timestamp\":1000,\"postback\":{\"mid\":\"mid_pb\",\"title\":\"Start\",\"payload\":\"START\"}}") + "]}";

        Assert.Empty(await provider.NormalizeInboundAsync(Raw(body)));
    }

    [Fact]
    public async Task Normalize_WithoutProviderTimestamps_UsesReceivedAt()
    {
        var provider = CreateProvider();
        var receivedAt = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        var body = "{\"object\":\"instagram\",\"entry\":[{\"id\":\"igid_1\",\"messaging\":[" +
            "{\"sender\":{\"id\":\"igsid_c\"},\"message\":{\"mid\":\"mid_nots\",\"text\":\"hi\"}}]}]}";

        var envelope = Assert.Single(await provider.NormalizeInboundAsync(Raw(body) with { ReceivedAt = receivedAt }));

        Assert.Equal(receivedAt, envelope.OccurredAt);
    }

    [Fact]
    public async Task OutboundAndHealth_ThrowNotSupported()
    {
        var provider = CreateProvider();
        var connection = ChannelConnectionSnapshot.Create("c", "t", null, ChannelType.Instagram);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.SendMessageAsync(connection, OutboundMessageRequest.TextMessage("igsid", "hi")));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.ValidateOrRefreshConnectionAsync(connection));
    }

    private static InstagramChannelProvider CreateProvider() =>
        new(Options.Create(new InstagramWebhookOptions { AppSecret = TestSecret, VerifyToken = TestVerifyToken }));

    private static WebhookValidationRequest ChallengeRequest(string mode, string token, string challenge, string? connectionSecret) =>
        WebhookValidationRequest.Create(
            rawBody: string.Empty,
            headers: new Dictionary<string, string>(),
            secret: connectionSecret,
            method: "GET",
            queryParameters: new Dictionary<string, string>
            {
                ["hub.mode"] = mode,
                ["hub.verify_token"] = token,
                ["hub.challenge"] = challenge
            });

    private static string Entry(string igid, string items, long time = 1729500000000) =>
        "{\"id\":\"" + igid + "\",\"time\":" + time + ",\"messaging\":[" + items + "]}";

    private static string MessageItem(string mid, string text) =>
        "{\"sender\":{\"id\":\"igsid_c\"},\"timestamp\":1729500000123,\"message\":{\"mid\":\"" + mid + "\",\"text\":\"" + text + "\"}}";

    private static WebhookValidationRequest SignedRequest(string body, string? secret = TestSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? TestSecret));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));

        return WebhookValidationRequest.Create(
            rawBody: body,
            headers: new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["X-Hub-Signature-256"] = signature
            },
            secret: "verify_me");
    }

    private static RawWebhookPayload Raw(string body) => new(
        RawBody: body,
        Headers: new Dictionary<string, string>(),
        ContentType: "application/json",
        TenantId: "tenant_1",
        ConnectionId: "conn_1",
        Channel: ChannelType.Instagram);
}
