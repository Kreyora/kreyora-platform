using System.Net;
using System.Text.Json;
using Kreyora.Application.Ai;
using Kreyora.Infrastructure.Ai;

namespace Kreyora.UnitTests.Ai;

public sealed class ResilientAiChatClientTests
{
    private static readonly AiChatRequest Simple = new([AiChatMessage.System("You are a shop assistant."), AiChatMessage.User("Namaste, size M cha?")]);

    [Fact]
    public async Task KillSwitchOff_FailsFast_WithoutAnyNetworkCall()
    {
        var options = AiTestSetup.LiveOptions();
        options.Enabled = false;
        var (client, handler, _, _) = AiTestSetup.Create(options);

        var result = await client.CompleteAsync(Simple);

        Assert.Equal(AiFailureKind.Disabled, result.Failure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FakeMode_UsesTheDeterministicFake_AndNeverTheNetwork()
    {
        var options = AiTestSetup.LiveOptions();
        options.Mode = AiMode.Fake;
        var (client, handler, _, _) = AiTestSetup.Create(options);

        var result = await client.CompleteAsync(Simple);

        Assert.True(result.IsSuccess);
        Assert.Equal(FakeAiChatClient.PlaceholderReply, result.Text);
        Assert.Equal(FakeAiChatClient.ProviderName, result.Provider);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PersonalData_IsRefused_ForFreeTierProviders_BeforeAnythingIsSent()
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions());

        var result = await client.CompleteAsync(Simple with { ContainsPersonalData = true });

        Assert.Equal(AiFailureKind.PolicyViolation, result.Failure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PersonalData_IsAllowed_OnlyForAnApprovedNoTrainingProvider()
    {
        var options = AiTestSetup.LiveOptions(withFallback: false);
        options.Providers["OpenRouter"].NoTraining = true;
        options.DataPolicy.AllowPersonalData = true;
        var (client, handler, _, _) = AiTestSetup.Create(options);
        handler.Respond(HttpStatusCode.OK, AiTestSetup.TextCompletion("Ho, M size cha."));

        var result = await client.CompleteAsync(Simple with { ContainsPersonalData = true });

        Assert.True(result.IsSuccess);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Success_ReturnsTextUsageAndModel_AndSendsBearerKeyToTheConfiguredProvider()
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions());
        handler.Respond(HttpStatusCode.OK, AiTestSetup.TextCompletion("Ho, M size cha."));

        var result = await client.CompleteAsync(Simple);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ho, M size cha.", result.Text);
        Assert.Equal(new AiUsage(120, 15), result.Usage);
        Assert.Equal([AiModelProfile.Primary], result.Attempts);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", sent.Uri.ToString());
        Assert.Equal($"Bearer {AiTestSetup.OpenRouterKey}", sent.Authorization);
        Assert.Equal("google/gemma-4-31b-it:free", JsonDocument.Parse(sent.Body).RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task TransientPrimaryFailure_FallsBackOnce_ToTheFallbackProfile(HttpStatusCode primaryStatus)
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions());
        handler.Respond(primaryStatus, """{"error":{"message":"busy"}}""")
            .Respond(HttpStatusCode.OK, AiTestSetup.TextCompletion("Fallback answer", "gemini-flash-test"));

        var result = await client.CompleteAsync(Simple);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fallback answer", result.Text);
        Assert.Equal([AiModelProfile.Primary, AiModelProfile.Fallback], result.Attempts);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://generativelanguage.googleapis.com/v1beta/openai/", handler.Requests[1].Uri.ToString());
        Assert.Equal("gemini-flash-test", JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AiFailureKind.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, AiFailureKind.NotConfigured)]
    public async Task NonTransientFailure_DoesNotFallBack(HttpStatusCode status, AiFailureKind expected)
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions());
        handler.Respond(status, """{"error":{"message":"no"}}""");

        var result = await client.CompleteAsync(Simple);

        Assert.Equal(expected, result.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task WithoutAFallbackProfile_TheTransientFailureIsReturned_WithRetryAfter()
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions(withFallback: false));
        handler.Respond(HttpStatusCode.TooManyRequests, "{}", retryAfter: TimeSpan.FromSeconds(20));

        var result = await client.CompleteAsync(Simple);

        Assert.Equal(AiFailureKind.RateLimited, result.Failure);
        Assert.Equal(TimeSpan.FromSeconds(20), result.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExplicitFallbackRequest_IsNotRetriedAgain()
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions());
        handler.Respond(HttpStatusCode.TooManyRequests, "{}");

        var result = await client.CompleteAsync(Simple with { Profile = AiModelProfile.Fallback });

        Assert.Equal(AiFailureKind.RateLimited, result.Failure);
        Assert.Equal([AiModelProfile.Fallback], result.Attempts);
    }

    [Fact]
    public async Task SlowProvider_TimesOut_WithinTheOverallDeadline()
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions(withFallback: false));
        handler.Hang();

        var started = DateTime.UtcNow;
        var result = await client.CompleteAsync(Simple with { Timeout = TimeSpan.FromMilliseconds(300) });

        Assert.Equal(AiFailureKind.Timeout, result.Failure);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UnreachableProvider_IsProviderUnavailable_AndFallsBack()
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions());
        handler.Fail().Respond(HttpStatusCode.OK, AiTestSetup.TextCompletion("ok"));

        var result = await client.CompleteAsync(Simple);

        Assert.True(result.IsSuccess);
        Assert.Equal([AiModelProfile.Primary, AiModelProfile.Fallback], result.Attempts);
    }

    [Fact]
    public async Task CallerCancellation_IsNotDisguisedAsATimeout()
    {
        var (client, handler, _, _) = AiTestSetup.Create(AiTestSetup.LiveOptions(withFallback: false));
        handler.Hang();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(Simple, cancellation.Token));
    }

    [Fact]
    public async Task Logs_NeverContainPromptContent_ResponseText_OrTheKey()
    {
        var (client, handler, _, logger) = AiTestSetup.Create(AiTestSetup.LiveOptions(withFallback: false));
        handler.Respond(HttpStatusCode.OK, AiTestSetup.TextCompletion("Secret answer text 4500"));

        await client.CompleteAsync(new AiChatRequest([AiChatMessage.User("My phone is 9800000012, address Lakeside")]));

        var joined = string.Join("\n", logger.Lines);
        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain("9800000012", joined);
        Assert.DoesNotContain("Lakeside", joined);
        Assert.DoesNotContain("Secret answer text", joined);
        Assert.DoesNotContain(AiTestSetup.OpenRouterKey, joined);
    }

    [Fact]
    public async Task ProfileReasoningEffort_IsSentWithTheRequest()
    {
        var options = AiTestSetup.LiveOptions(withFallback: false);
        options.Profiles["Primary"].ReasoningEffort = "none";
        var (client, handler, _, _) = AiTestSetup.Create(options);
        handler.Respond(HttpStatusCode.OK, AiTestSetup.TextCompletion("ok"));

        await client.CompleteAsync(Simple);

        Assert.Equal("none", JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task PaidProviderSwitch_IsConfigurationOnly()
    {
        // R-PAID: change the Primary profile to a paid OpenAI-compatible provider; no code change.
        var options = AiTestSetup.LiveOptions(withFallback: false);
        options.Providers["OpenAi"] = new AiProviderOptions
        {
            BaseUrl = "https://api.openai.com/v1", ApiKey = "paid-key", NoTraining = true, MaxTokensParameter = "max_completion_tokens"
        };
        options.Profiles["Primary"] = new AiProfileOptions { Provider = "OpenAi", Model = "paid-model" };
        var (client, handler, _, _) = AiTestSetup.Create(options);
        handler.Respond(HttpStatusCode.OK, AiTestSetup.TextCompletion("Hello", "paid-model"));

        var result = await client.CompleteAsync(Simple);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/chat/completions", sent.Uri.ToString());
        Assert.Equal("Bearer paid-key", sent.Authorization);
        var body = JsonDocument.Parse(sent.Body).RootElement;
        Assert.True(body.TryGetProperty("max_completion_tokens", out _));
        Assert.False(body.TryGetProperty("max_tokens", out _));
    }
}
