using System.Net;
using Kreyora.Application.Ai;
using Kreyora.Infrastructure.Ai;

namespace Kreyora.ContractTests.Ai;

/// <summary>
/// The provider-neutral contract (ADR-018): whatever OpenAI-compatible provider answers, callers see the same
/// <see cref="AiChatResult"/> semantics. Shapes follow the documented OpenAI-compatible format used by OpenRouter
/// and Gemini's compatibility endpoint; sanitized live captures from the M09-S01 speed probe are added as they are
/// observed. No network.
/// </summary>
public sealed class AiChatClientContractTests
{
    private static AiChatResult Map(HttpStatusCode status, string body, TimeSpan? retryAfter = null) =>
        OpenAiCompatibleChatClient.MapResponse(status, retryAfter, body, "provider", "model", TimeSpan.FromMilliseconds(5));

    public static TheoryData<string, HttpStatusCode, string, AiFailureKind?> Shapes => new()
    {
        { "text answer", HttpStatusCode.OK,
            """{"model":"m","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"Ho, cha."}}],"usage":{"prompt_tokens":10,"completion_tokens":3}}""", null },
        { "tool call, arguments as string", HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"tool_calls","message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_a","type":"function","function":{"name":"GetPrice","arguments":"{\"productId\":\"p1\"}"}}]}}]}""", null },
        { "tool call, arguments as object, no id, odd finish reason", HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"STOP","message":{"role":"assistant","tool_calls":[{"type":"function","function":{"name":"GetPrice","arguments":{"productId":"p1"}}}]}}]}""", null },
        { "rate limited", HttpStatusCode.TooManyRequests, """{"error":{"code":429,"message":"Rate limit exceeded: free-models-per-day"}}""", AiFailureKind.RateLimited },
        { "error in a 200 body (OpenRouter style)", HttpStatusCode.OK, """{"error":{"code":502,"message":"Provider returned error"}}""", AiFailureKind.ProviderUnavailable },
        { "error wrapped in an array (Gemini style)", HttpStatusCode.BadRequest, """[{"error":{"code":400,"message":"Invalid argument","status":"INVALID_ARGUMENT"}}]""", AiFailureKind.InvalidRequest },
        { "bad key", HttpStatusCode.Unauthorized, """{"error":{"code":401,"message":"No auth credentials found"}}""", AiFailureKind.NotConfigured },
        { "no credits for paid model", HttpStatusCode.PaymentRequired, """{"error":{"code":402,"message":"Insufficient credits"}}""", AiFailureKind.NotConfigured },
        { "server error", HttpStatusCode.InternalServerError, "oops", AiFailureKind.ProviderUnavailable },
        { "malformed JSON", HttpStatusCode.OK, "{not json", AiFailureKind.InvalidResponse },
        { "no choices", HttpStatusCode.OK, """{"choices":[]}""", AiFailureKind.InvalidResponse },
        { "empty answer", HttpStatusCode.OK, """{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":""}}]}""", AiFailureKind.InvalidResponse },
        { "content filtered", HttpStatusCode.OK, """{"choices":[{"finish_reason":"content_filter","message":{"role":"assistant","content":null}}]}""", AiFailureKind.ContentRefused },
        // Observed live 2026-10-06 (M09-S01 probe; content replaced, structure kept):
        { "OpenRouter extended usage block (cost, reasoning token details)", HttpStatusCode.OK,
            """{"model":"nvidia/nemotron-3-super-120b-a12b:free","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"Welcome to our shop!"}}],"usage":{"prompt_tokens":45,"completion_tokens":83,"total_tokens":128,"cost":0,"is_byok":false,"prompt_tokens_details":{"cached_tokens":0},"completion_tokens_details":{"reasoning_tokens":0}}}""", null },
        { "Gemini thinking budget exhausted: truncated visible text, finish=length", HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"length","message":{"role":"assistant","content":"Namaste and a warm welcome to our"}}],"usage":{"completion_tokens":8,"prompt_tokens":29,"total_tokens":275}}""", null },
        { "Gemini retired model (404 in an array)", HttpStatusCode.NotFound,
            """[{"error":{"code":404,"message":"This model is no longer available to new users.","status":"NOT_FOUND"}}]""", AiFailureKind.InvalidRequest },
        { "tool call without a name", HttpStatusCode.OK, """{"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"id":"x","function":{"arguments":"{}"}}]}}]}""", AiFailureKind.InvalidResponse },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ProviderShapes_MapToTheContract(string scenario, HttpStatusCode status, string body, AiFailureKind? expectedFailure)
    {
        var result = Map(status, body);

        Assert.True(expectedFailure is null == result.IsSuccess, scenario);
        Assert.Equal(expectedFailure, result.Failure);
        if (result.IsSuccess)
        {
            Assert.True(result.ToolCalls.Count > 0 || !string.IsNullOrWhiteSpace(result.Text), scenario);
            Assert.All(result.ToolCalls, call => Assert.False(string.IsNullOrWhiteSpace(call.Id)));
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.FailureMessage));
            Assert.DoesNotContain("free-models-per-day", result.FailureMessage); // fixed messages, never provider text
        }
    }

    [Fact]
    public void ToolCalls_AreNormalized_ToStringArguments_AndToolCallsFinish()
    {
        var result = Map(HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"STOP","message":{"tool_calls":[{"type":"function","function":{"name":"GetPrice","arguments":{"productId":"p1"}}}]}}]}""");

        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("call_0", call.Id);
        Assert.Equal("""{"productId":"p1"}""", call.ArgumentsJson);
        Assert.Equal(AiFinishReason.ToolCalls, result.FinishReason);
    }

    [Fact]
    public void TruncatedAnswers_AreReportedAsLength_SoCallersCanTreatThemAsIncomplete()
    {
        var result = Map(HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"length","message":{"role":"assistant","content":"Namaste and a warm welcome to our"}}]}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(AiFinishReason.Length, result.FinishReason);
    }

    [Fact]
    public void ProviderData_OnToolCalls_IsCaptured_AndSentBackUnchanged()
    {
        // Observed live 2026-10-06: Gemini 3 returns tool_calls[].extra_content.google.thought_signature and rejects
        // the next turn with INVALID_ARGUMENT unless it is echoed back.
        var result = Map(HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"tool_calls","message":{"role":"assistant","tool_calls":[{"extra_content":{"google":{"thought_signature":"opaque-sig-123"}},"function":{"arguments":"{\"productId\":\"P-KURTA-RED\"}","name":"GetPrice"},"id":"call_20038","type":"function"}]}}]}""");
        var call = Assert.Single(result.ToolCalls);

        var body = OpenAiCompatibleChatClient.BuildRequestBody(new AiProviderOptions(), "m",
            new AiChatRequest([AiChatMessage.User("price?"), AiChatMessage.Assistant(null, [call]), AiChatMessage.ToolResult(call.Id, "{}")]), 100);

        using var document = System.Text.Json.JsonDocument.Parse(body);
        var echoed = document.RootElement.GetProperty("messages")[1].GetProperty("tool_calls")[0];
        Assert.Equal("opaque-sig-123", echoed.GetProperty("extra_content").GetProperty("google").GetProperty("thought_signature").GetString());
    }

    [Fact]
    public void ToolCallsWithoutProviderData_SendNoExtraContent()
    {
        var body = OpenAiCompatibleChatClient.BuildRequestBody(new AiProviderOptions(), "m",
            new AiChatRequest([AiChatMessage.Assistant(null, [new AiToolCall("c1", "GetPrice", "{}")])]), 100);

        Assert.DoesNotContain("extra_content", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryAfter_IsCarried_OnRateLimit()
    {
        var result = Map(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), result.RetryAfter);
    }

    [Fact]
    public async Task Fake_SatisfiesTheContract_WithoutNetwork_AndReplaysScriptedResults()
    {
        var fake = new FakeAiChatClient();
        var request = new AiChatRequest([AiChatMessage.User("price?")]);

        var placeholder = await fake.CompleteAsync(request);
        fake.Enqueue(AiChatResult.Failed(AiFailureKind.RateLimited, "scripted"));
        var scripted = await fake.CompleteAsync(request);

        Assert.True(placeholder.IsSuccess);
        Assert.DoesNotMatch(@"\d", placeholder.Text!); // never states prices, stock or other numbers
        Assert.Equal(AiFailureKind.RateLimited, scripted.Failure);
        Assert.Equal(2, fake.Requests.Count);
    }
}
