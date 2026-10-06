using System.Text.Json;
using Kreyora.Application.Ai;
using Kreyora.Infrastructure.Ai;

namespace Kreyora.UnitTests.Ai;

public sealed class OpenAiCompatibleRequestTests
{
    private static readonly AiProviderOptions Provider = new() { BaseUrl = "https://openrouter.ai/api/v1" };

    private static JsonElement Build(AiChatRequest request, AiProviderOptions? provider = null) =>
        JsonDocument.Parse(OpenAiCompatibleChatClient.BuildRequestBody(provider ?? Provider, "test-model", request, 800)).RootElement;

    [Fact]
    public void Tools_AreSentAsFunctions_WithTheirJsonSchema_AndToolChoice()
    {
        var request = new AiChatRequest(
            [AiChatMessage.User("price?")],
            [new AiToolDefinition("GetPrice", "Current price of a product variant",
                """{"type":"object","properties":{"productId":{"type":"string"}},"required":["productId"]}""")],
            AiToolChoice.Required);

        var body = Build(request);

        var tool = body.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("GetPrice", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("productId", tool.GetProperty("function").GetProperty("parameters").GetProperty("required")[0].GetString());
        Assert.Equal("required", body.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public void WithoutTools_NoToolFieldsAreSent_AndTemperatureIsOmittedUnlessSet()
    {
        var body = Build(new AiChatRequest([AiChatMessage.User("hi")]));

        Assert.False(body.TryGetProperty("tools", out _));
        Assert.False(body.TryGetProperty("tool_choice", out _));
        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.Equal(800, body.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public void ToolRoundTrip_SerializesAssistantToolCalls_AndToolResults()
    {
        var request = new AiChatRequest(
        [
            AiChatMessage.System("rules"),
            AiChatMessage.User("price?"),
            AiChatMessage.Assistant(null, [new AiToolCall("call_1", "GetPrice", """{"productId":"p1"}""")]),
            AiChatMessage.ToolResult("call_1", """{"priceNpr":4500}"""),
        ], Temperature: 0.2, MaxOutputTokens: 300);

        var body = Build(request);
        var messages = body.GetProperty("messages");

        Assert.Equal(["system", "user", "assistant", "tool"], messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()!).ToArray());
        var call = messages[2].GetProperty("tool_calls")[0];
        Assert.Equal("call_1", call.GetProperty("id").GetString());
        Assert.Equal("""{"productId":"p1"}""", call.GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("call_1", messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal(0.2, body.GetProperty("temperature").GetDouble());
        Assert.Equal(300, body.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public void OutputLimitFieldName_FollowsTheProviderConfiguration()
    {
        var body = Build(new AiChatRequest([AiChatMessage.User("hi")]), new AiProviderOptions { MaxTokensParameter = "max_completion_tokens" });

        Assert.True(body.TryGetProperty("max_completion_tokens", out _));
        Assert.False(body.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public void ReasoningEffort_IsSentOnlyWhenConfigured_UnderTheProvidersFieldName()
    {
        var request = new AiChatRequest([AiChatMessage.User("hi")]);

        var without = JsonDocument.Parse(OpenAiCompatibleChatClient.BuildRequestBody(Provider, "m", request, 800)).RootElement;
        var with = JsonDocument.Parse(OpenAiCompatibleChatClient.BuildRequestBody(Provider, "m", request, 800, "none")).RootElement;
        var renamed = JsonDocument.Parse(OpenAiCompatibleChatClient.BuildRequestBody(
            new AiProviderOptions { ReasoningEffortParameter = "reasoning_level" }, "m", request, 800, "low")).RootElement;

        Assert.False(without.TryGetProperty("reasoning_effort", out _));
        Assert.Equal("none", with.GetProperty("reasoning_effort").GetString());
        Assert.Equal("low", renamed.GetProperty("reasoning_level").GetString());
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai/", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")]
    public void CompletionsUri_IsBaseUrlPlusChatCompletions(string baseUrl, string expected) =>
        Assert.Equal(expected, OpenAiCompatibleChatClient.CompletionsUri(baseUrl).ToString());
}
