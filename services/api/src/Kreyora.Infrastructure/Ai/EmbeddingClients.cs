using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Kreyora.Domain.Assistant;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Ai;

/// <summary>The <see cref="IAiEmbeddingClient"/> callers get: kill switch, then fake or live (ADR-019).</summary>
public sealed class AiEmbeddingClient(IOptionsMonitor<AiOptions> options, OpenAiCompatibleEmbeddingClient transport) : IAiEmbeddingClient
{
    public async Task<AiEmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, AiEmbeddingPurpose purpose, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return AiEmbeddingResult.Failed(AiFailureKind.Disabled, "AI is disabled.");
        }

        if (texts.Count == 0)
        {
            return AiEmbeddingResult.Success([], current.Mode == AiMode.Fake ? FakeEmbeddingClient.ModelName : current.Embeddings.Model, current.Embeddings.Dimensions);
        }

        if (current.Mode == AiMode.Fake)
        {
            return await FakeEmbeddingClient.EmbedAsync(texts, purpose, cancellationToken);
        }

        var embeddings = current.Embeddings;
        if (!current.Providers.TryGetValue(embeddings.Provider, out var provider) || string.IsNullOrWhiteSpace(provider.ApiKey) || string.IsNullOrWhiteSpace(embeddings.Model))
        {
            return AiEmbeddingResult.Failed(AiFailureKind.NotConfigured, "The embedding provider is not configured.");
        }

        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(embeddings.BatchSize))
        {
            var result = await transport.EmbedAsync(embeddings.Provider, provider, embeddings.Model, embeddings.Dimensions, batch,
                TimeSpan.FromSeconds(current.Limits.TimeoutSeconds), cancellationToken);
            if (!result.IsSuccess)
            {
                return result;
            }

            vectors.AddRange(result.Vectors);
        }

        return AiEmbeddingResult.Success(vectors, embeddings.Model, embeddings.Dimensions);
    }
}

/// <summary>
/// OpenAI-compatible <c>POST {BaseUrl}/embeddings</c> (Gemini's compatibility endpoint today; others by configuration).
/// Logs provider, model, count, outcome and latency only, never input text.
/// </summary>
public sealed partial class OpenAiCompatibleEmbeddingClient(IHttpClientFactory httpClientFactory, ILogger<OpenAiCompatibleEmbeddingClient> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Embedding call {Provider}/{Model} for {Count} texts → {Outcome} in {LatencyMs} ms")]
    private static partial void LogCall(ILogger logger, string provider, string model, int count, string outcome, long latencyMs);

    public async Task<AiEmbeddingResult> EmbedAsync(string providerName, AiProviderOptions provider, string model, int dimensions,
        IReadOnlyList<string> texts, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        AiEmbeddingResult result;
        try
        {
            var body = new JsonObject { ["model"] = model, ["dimensions"] = dimensions, ["input"] = new JsonArray(texts.Select(t => (JsonNode?)t).ToArray()) };
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.BaseUrl.TrimEnd('/') + "/embeddings"))
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            using var response = await httpClientFactory.CreateClient(OpenAiCompatibleChatClient.HttpClientName).SendAsync(request, deadline.Token);
            result = Map(response.StatusCode, await response.Content.ReadAsStringAsync(deadline.Token), texts.Count, model, dimensions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = AiEmbeddingResult.Failed(AiFailureKind.Timeout, "The embedding provider did not answer in time.");
        }
        catch (HttpRequestException)
        {
            result = AiEmbeddingResult.Failed(AiFailureKind.ProviderUnavailable, "The embedding provider could not be reached.");
        }

        LogCall(logger, providerName, model, texts.Count, result.IsSuccess ? "ok" : result.Failure!.Value.ToString(), started.ElapsedMilliseconds);
        return result;
    }

    public static AiEmbeddingResult Map(HttpStatusCode status, string body, int expectedCount, string model, int dimensions)
    {
        if (status == HttpStatusCode.TooManyRequests) return AiEmbeddingResult.Failed(AiFailureKind.RateLimited, "The embedding provider rate limit was reached.");
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired) return AiEmbeddingResult.Failed(AiFailureKind.NotConfigured, "The embedding provider rejected the credentials or account.");
        if ((int)status >= 500) return AiEmbeddingResult.Failed(AiFailureKind.ProviderUnavailable, "The embedding provider is temporarily unavailable.");
        if ((int)status >= 400) return AiEmbeddingResult.Failed(AiFailureKind.InvalidRequest, "The embedding provider rejected the request.");

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != expectedCount)
            {
                return AiEmbeddingResult.Failed(AiFailureKind.InvalidResponse, "The embedding provider returned an unexpected number of vectors.");
            }

            // Order by "index" when present; providers may return items out of order.
            var vectors = data.EnumerateArray()
                .Select((item, position) => (Index: item.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : position,
                    Vector: item.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray()))
                .OrderBy(x => x.Index)
                .Select(x => x.Vector)
                .ToList();
            if (vectors.Any(v => v.Length != dimensions))
            {
                return AiEmbeddingResult.Failed(AiFailureKind.InvalidResponse, "The embedding provider returned vectors of an unexpected size.");
            }

            return AiEmbeddingResult.Success(vectors, model, dimensions);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return AiEmbeddingResult.Failed(AiFailureKind.InvalidResponse, "The embedding provider returned an unreadable response.");
        }
    }
}

/// <summary>
/// Offline deterministic embedder for tests and <c>Ai:Mode=Fake</c> (ADR-019): words and character trigrams are hashed
/// into a fixed vector, so texts sharing words are similar. No network, same input → same vector.
/// </summary>
public static class FakeEmbeddingClient
{
    public const string ModelName = "fake-hashing-v1";
    public const int Dimensions = 256;

    public static Task<AiEmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, AiEmbeddingPurpose purpose, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AiEmbeddingResult.Success([.. texts.Select(Embed)], ModelName, Dimensions));
    }

    public static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var token in KnowledgeTokenizer.Tokens(text))
        {
            Add(vector, token, 1.0f);
            var padded = $"#{token}#";
            for (var i = 0; i + 3 <= padded.Length; i++)
            {
                Add(vector, padded.Substring(i, 3), 0.25f);
            }
        }

        return vector;
    }

    private static void Add(float[] vector, string feature, float weight)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(feature));
        var slot = BitConverter.ToUInt32(hash, 0) % Dimensions;
        vector[slot] += (hash[4] & 1) == 0 ? weight : -weight;
    }
}
