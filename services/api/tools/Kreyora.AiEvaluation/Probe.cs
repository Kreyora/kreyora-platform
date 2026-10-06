using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Kreyora.Infrastructure.Ai;

namespace Kreyora.AiEvaluation;

public sealed record ProbeResult(
    string Provider,
    string Model,
    long TextLatencyMs,
    int TextOutputTokens,
    double TokensPerSecond,
    string? TextFailure,
    long ToolLatencyMs,
    bool ToolCallValid,
    string? ToolFailure,
    double? PaidInputPerMillionUsd,
    double? PaidOutputPerMillionUsd,
    DateTimeOffset ProbedAt);

/// <summary>
/// Speed and tool-calling pre-screen: two synthetic requests per model (a short text reply and one tool call).
/// Speed = output tokens / total time, so it includes time to first token.
/// </summary>
public static class Probe
{
    private static readonly string[] GeminiExcluded = ["image", "tts", "audio", "live", "embedding", "vision", "learnlm"];

    public static async Task RunAsync(AiOptions aiOptions, OpenAiCompatibleChatClient transport, HttpClient http, Pacer pacer, string outDir, string? models, string? reasoningEffort)
    {
        var targets = models is null
            ? (await DiscoverAsync(aiOptions, http)).ToList()
            : models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ModelRef.Parse).ToList();
        Console.WriteLine($"Probing {targets.Count} models (2 requests each)…");

        var results = new List<ProbeResult>();
        foreach (var (providerName, model) in targets)
        {
            var provider = ModelRef.Provider(aiOptions, providerName);
            await pacer.WaitAsync();
            var text = await transport.CompleteAsync(providerName, provider, model, new AiChatRequest(
            [
                AiChatMessage.System("You are a friendly shop assistant. Reply in about 80 words."),
                AiChatMessage.User("Write a short welcome message for a clothing shop in Kathmandu, first in English, then in Nepali (Devanagari).")
            ], Temperature: 0.3, MaxOutputTokens: 250), 250, TimeSpan.FromSeconds(60), CancellationToken.None, reasoningEffort);

            await pacer.WaitAsync();
            var tool = await transport.CompleteAsync(providerName, provider, model, new AiChatRequest(
            [
                AiChatMessage.System("You are a shop assistant. Always use the tools for prices."),
                AiChatMessage.User("How much is the Red Cotton Kurta (product id P-KURTA-RED) in size L?")
            ], [FakeTools.Definitions.Single(d => d.Name == "GetPrice")], Temperature: 0, MaxOutputTokens: 200), 200, TimeSpan.FromSeconds(60), CancellationToken.None, reasoningEffort);

            var outputTokens = text.Usage?.OutputTokens ?? 0;
            var seconds = text.Latency.TotalSeconds;
            var result = new ProbeResult(providerName, model,
                (long)text.Latency.TotalMilliseconds, outputTokens, text.IsSuccess && seconds > 0 ? Math.Round(outputTokens / seconds, 1) : 0,
                text.IsSuccess ? null : text.Failure.ToString(),
                (long)tool.Latency.TotalMilliseconds,
                tool.IsSuccess && tool.ToolCalls.Any(c => c.Name == "GetPrice" && Scoring.ArgumentsValid("GetPrice", c.ArgumentsJson)),
                tool.IsSuccess ? null : tool.Failure.ToString(),
                null, null, DateTimeOffset.UtcNow);
            results.Add(result);
            Console.WriteLine($"  {providerName}:{model}  {result.TokensPerSecond} tok/s  text {result.TextLatencyMs} ms  tool {(result.ToolCallValid ? "ok" : "NO")} {result.ToolLatencyMs} ms {result.TextFailure} {result.ToolFailure}");
        }

        Directory.CreateDirectory(outDir);
        var probeFile = Path.Combine(outDir, models is null ? "probe.json" : $"probe-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(probeFile, JsonSerializer.Serialize(new { reasoningEffort, results }, EvalDataset.Json));
        Console.WriteLine();
        Console.WriteLine("| Model | tok/s | Text reply (ms) | Tool call | Tool latency (ms) | Paid $/M in/out |");
        Console.WriteLine("|---|---:|---:|---|---:|---|");
        foreach (var r in results.OrderByDescending(r => r.ToolCallValid).ThenByDescending(r => r.TokensPerSecond))
        {
            var paid = r.PaidInputPerMillionUsd is null ? "n/a" : $"{r.PaidInputPerMillionUsd:0.###}/{r.PaidOutputPerMillionUsd:0.###}";
            Console.WriteLine($"| {r.Provider}:{r.Model} | {r.TokensPerSecond} | {r.TextLatencyMs}{(r.TextFailure is null ? "" : $" ({r.TextFailure})")} | {(r.ToolCallValid ? "valid" : "no")}{(r.ToolFailure is null ? "" : $" ({r.ToolFailure})")} | {r.ToolLatencyMs} | {paid} |");
        }
    }

    /// <summary>The Gemini Flash / Flash-Lite models available to the owner's Google AI Studio key.</summary>
    private static async Task<IEnumerable<(string, string)>> DiscoverAsync(AiOptions aiOptions, HttpClient http)
    {
        var targets = new List<(string, string)>();
        if (aiOptions.Providers.TryGetValue("GoogleAiStudio", out var google) && !string.IsNullOrWhiteSpace(google.ApiKey))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(google.BaseUrl.TrimEnd('/') + "/models"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", google.ApiKey);
            using var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var node = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                foreach (var item in node?["data"]?.AsArray() ?? [])
                {
                    var id = item?["id"]?.GetValue<string>() ?? string.Empty;
                    if (id.Contains("gemini", StringComparison.OrdinalIgnoreCase) && id.Contains("flash", StringComparison.OrdinalIgnoreCase)
                        && !GeminiExcluded.Any(x => id.Contains(x, StringComparison.OrdinalIgnoreCase)))
                    {
                        targets.Add(("GoogleAiStudio", id.StartsWith("models/", StringComparison.Ordinal) ? id["models/".Length..] : id));
                    }
                }
            }
            else
            {
                Console.WriteLine($"  Google AI Studio model list failed: HTTP {(int)response.StatusCode}");
            }
        }

        return targets;
    }
}
