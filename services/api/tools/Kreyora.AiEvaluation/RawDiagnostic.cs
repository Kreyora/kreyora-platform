using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Kreyora.Infrastructure.Ai;

namespace Kreyora.AiEvaluation;

/// <summary>
/// One synthetic request with optional extra body fields; prints only the response shape (finish reason, content
/// length, usage incl. reasoning tokens, error code). Used to understand provider behavior, e.g. thinking budgets.
/// </summary>
public static class RawDiagnostic
{
    public static async Task RunAsync(AiOptions aiOptions, HttpClient http, string modelRef, int maxTokens, string? extraJson)
    {
        var (providerName, model) = ModelRef.Parse(modelRef);
        var provider = ModelRef.Provider(aiOptions, providerName);
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = "You are a friendly shop assistant. Reply in about 60 words." },
                new JsonObject { ["role"] = "user", ["content"] = "Write a short welcome message for a clothing shop in Kathmandu." }),
            [provider.MaxTokensParameter] = maxTokens
        };
        if (!string.IsNullOrWhiteSpace(extraJson) && JsonNode.Parse(extraJson) is JsonObject extra)
        {
            foreach (var (key, value) in extra)
            {
                body[key] = value?.DeepClone();
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiCompatibleChatClient.CompletionsUri(provider.BaseUrl))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        var stopwatch = Stopwatch.StartNew();
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        stopwatch.Stop();

        JsonNode? root = null;
        try { root = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }
        var root0 = root is JsonArray { Count: > 0 } array ? array[0] : root;
        var choice = root0?["choices"]?[0];
        var content = choice?["message"]?["content"]?.ToString() ?? string.Empty;
        var hasReasoning = choice?["message"]?["reasoning"] is not null || choice?["message"]?["reasoning_content"] is not null;
        Console.WriteLine($"{modelRef} max={maxTokens} extra={extraJson ?? "-"} → HTTP {(int)response.StatusCode} in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"   finish={choice?["finish_reason"]} contentChars={content.Length} reasoningField={hasReasoning}");
        Console.WriteLine($"   usage={root0?["usage"]?.ToJsonString() ?? "-"}");
        if (root0?["error"] is JsonNode error)
        {
            Console.WriteLine($"   error code={error["code"]} status={error["status"]} message={error["message"]?.ToString()[..Math.Min(160, error["message"]?.ToString().Length ?? 0)]}");
        }
        else if (content.Length > 0)
        {
            Console.WriteLine($"   sample: {content[..Math.Min(120, content.Length)].ReplaceLineEndings(" ")}");
        }
    }
}
