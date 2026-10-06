using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kreyora.Application.Ai;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Ai;

/// <summary>
/// Speaks the OpenAI-style <c>POST {BaseUrl}/chat/completions</c> shared by OpenRouter, Gemini's compatibility
/// endpoint, OpenAI itself and many others, so adding a provider is configuration only (ADR-018).
/// Never logs prompts, tool arguments, responses or keys: only provider, model, status, latency and token counts.
/// </summary>
public sealed partial class OpenAiCompatibleChatClient(IHttpClientFactory httpClientFactory, ILogger<OpenAiCompatibleChatClient> logger)
{
    public const string HttpClientName = "kreyora-ai";

    [LoggerMessage(Level = LogLevel.Information, Message = "AI call {Provider}/{Model} → {Outcome} in {LatencyMs} ms (tokens in {InputTokens}, out {OutputTokens})")]
    private static partial void LogCall(ILogger logger, string provider, string model, string outcome, long latencyMs, int inputTokens, int outputTokens);

    public async Task<AiChatResult> CompleteAsync(
        string providerName,
        AiProviderOptions provider,
        string model,
        AiChatRequest request,
        int defaultMaxOutputTokens,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? reasoningEffort = null)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        AiChatResult result;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, CompletionsUri(provider.BaseUrl))
            {
                Content = new StringContent(BuildRequestBody(provider, model, request, defaultMaxOutputTokens, reasoningEffort), Encoding.UTF8, "application/json")
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            foreach (var (name, value) in provider.ExtraHeaders)
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeoutSource.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            result = MapResponse(response.StatusCode, RetryAfter(response), body, providerName, model, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = AiChatResult.Failed(AiFailureKind.Timeout, "The AI provider did not answer in time.", providerName, model, stopwatch.Elapsed);
        }
        catch (HttpRequestException)
        {
            result = AiChatResult.Failed(AiFailureKind.ProviderUnavailable, "The AI provider could not be reached.", providerName, model, stopwatch.Elapsed);
        }

        LogCall(logger, providerName, model, result.IsSuccess ? "ok" : result.Failure!.Value.ToString(),
            (long)stopwatch.Elapsed.TotalMilliseconds, result.Usage?.InputTokens ?? 0, result.Usage?.OutputTokens ?? 0);
        return result;
    }

    public static Uri CompletionsUri(string baseUrl) => new(baseUrl.TrimEnd('/') + "/chat/completions");

    public static string BuildRequestBody(AiProviderOptions provider, string model, AiChatRequest request, int defaultMaxOutputTokens, string? reasoningEffort = null)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(request.Messages.Select(ToJson).ToArray<JsonNode?>()),
            [string.IsNullOrWhiteSpace(provider.MaxTokensParameter) ? "max_tokens" : provider.MaxTokensParameter] =
                request.MaxOutputTokens ?? defaultMaxOutputTokens
        };

        if (request.Temperature is { } temperature)
        {
            body["temperature"] = temperature;
        }

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            body[string.IsNullOrWhiteSpace(provider.ReasoningEffortParameter) ? "reasoning_effort" : provider.ReasoningEffortParameter] = reasoningEffort;
        }

        if (request.Tools is { Count: > 0 } tools)
        {
            body["tools"] = new JsonArray(tools.Select(tool => (JsonNode?)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema)
                }
            }).ToArray());
            body["tool_choice"] = request.ToolChoice switch
            {
                AiToolChoice.None => "none",
                AiToolChoice.Required => "required",
                _ => "auto"
            };
        }

        return body.ToJsonString();
    }

    private static JsonObject ToJson(AiChatMessage message)
    {
        var json = new JsonObject
        {
            ["role"] = message.Role switch
            {
                AiChatRole.System => "system",
                AiChatRole.Assistant => "assistant",
                AiChatRole.Tool => "tool",
                _ => "user"
            },
            ["content"] = message.Content
        };

        if (message.ToolCalls is { Count: > 0 } calls)
        {
            json["tool_calls"] = new JsonArray(calls.Select(call =>
            {
                var node = new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.ArgumentsJson }
                };
                if (!string.IsNullOrEmpty(call.ProviderData))
                {
                    node["extra_content"] = JsonNode.Parse(call.ProviderData); // echoed back unchanged
                }

                return (JsonNode?)node;
            }).ToArray());
        }

        if (message.ToolCallId is not null)
        {
            json["tool_call_id"] = message.ToolCallId;
        }

        return json;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta
        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

    public static AiChatResult MapResponse(HttpStatusCode status, TimeSpan? retryAfter, string body, string provider, string model, TimeSpan latency)
    {
        JsonNode? root = null;
        try
        {
            root = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // handled below: an unreadable body is mapped by status
        }

        // Some providers (e.g. OpenRouter) report errors in the body, sometimes with HTTP 200.
        var errorCode = ErrorCode(root);
        var effective = errorCode is >= 400 and < 600 ? (HttpStatusCode)errorCode.Value : status;

        if (effective == HttpStatusCode.TooManyRequests)
        {
            return AiChatResult.Failed(AiFailureKind.RateLimited, "The AI provider rate limit was reached.", provider, model, latency, retryAfter);
        }

        if (effective is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired)
        {
            return AiChatResult.Failed(AiFailureKind.NotConfigured, "The AI provider rejected the credentials or account.", provider, model, latency);
        }

        if (effective is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
        {
            return AiChatResult.Failed(AiFailureKind.Timeout, "The AI provider did not answer in time.", provider, model, latency);
        }

        if ((int)effective >= 500)
        {
            return AiChatResult.Failed(AiFailureKind.ProviderUnavailable, "The AI provider is temporarily unavailable.", provider, model, latency);
        }

        if ((int)effective >= 400 || errorCode is not null)
        {
            return AiChatResult.Failed(AiFailureKind.InvalidRequest, "The AI provider rejected the request.", provider, model, latency);
        }

        var choice = root is JsonObject && root["choices"] is JsonArray { Count: > 0 } choices ? choices[0] : null;
        var message = choice?["message"];
        if (message is null)
        {
            return AiChatResult.Failed(AiFailureKind.InvalidResponse, "The AI provider returned an unreadable response.", provider, model, latency);
        }

        var finish = (choice?["finish_reason"]?.GetValue<string?>() ?? string.Empty) switch
        {
            "stop" => AiFinishReason.Stop,
            "tool_calls" or "function_call" => AiFinishReason.ToolCalls,
            "length" => AiFinishReason.Length,
            "content_filter" => AiFinishReason.ContentFilter,
            _ => AiFinishReason.Other
        };

        var toolCalls = new List<AiToolCall>();
        if (message["tool_calls"] is JsonArray calls)
        {
            for (var index = 0; index < calls.Count; index++)
            {
                var function = calls[index]?["function"];
                var name = function?["name"]?.GetValue<string?>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return AiChatResult.Failed(AiFailureKind.InvalidResponse, "The AI provider returned a malformed tool call.", provider, model, latency);
                }

                var arguments = function?["arguments"] switch
                {
                    JsonValue value when value.TryGetValue<string>(out var argumentText) => argumentText,
                    JsonNode node => node.ToJsonString(),
                    _ => "{}"
                };
                var id = calls[index]?["id"]?.GetValue<string?>();
                var providerData = calls[index]?["extra_content"] is JsonObject extra ? extra.ToJsonString() : null;
                toolCalls.Add(new AiToolCall(string.IsNullOrWhiteSpace(id) ? $"call_{index}" : id, name, arguments, providerData));
            }
        }

        var text = message["content"] is JsonValue content && content.TryGetValue<string>(out var contentText) ? contentText : null;
        if (finish == AiFinishReason.ContentFilter && toolCalls.Count == 0 && string.IsNullOrWhiteSpace(text))
        {
            return AiChatResult.Failed(AiFailureKind.ContentRefused, "The AI provider refused to answer.", provider, model, latency);
        }

        if (toolCalls.Count == 0 && string.IsNullOrWhiteSpace(text))
        {
            return AiChatResult.Failed(AiFailureKind.InvalidResponse, "The AI provider returned an empty answer.", provider, model, latency);
        }

        if (toolCalls.Count > 0 && finish == AiFinishReason.Other)
        {
            finish = AiFinishReason.ToolCalls;
        }

        var usage = root is JsonObject && root["usage"] is JsonObject usageNode
            ? new AiUsage(usageNode["prompt_tokens"]?.GetValue<int>() ?? 0, usageNode["completion_tokens"]?.GetValue<int>() ?? 0)
            : null;
        var answeredModel = (root is JsonObject ? root["model"]?.GetValue<string?>() : null) ?? model;

        return AiChatResult.Success(text, toolCalls, finish, usage, provider, answeredModel, latency);
    }

    private static int? ErrorCode(JsonNode? root)
    {
        // Gemini's compatibility endpoint can wrap errors in a one-element array.
        if (root is JsonArray { Count: > 0 } wrapped)
        {
            root = wrapped[0];
        }

        if (root is not JsonObject || root["error"] is not JsonObject error)
        {
            return null;
        }

        return error["code"] switch
        {
            JsonValue value when value.TryGetValue<int>(out var number) => number,
            JsonValue value when value.TryGetValue<string>(out var codeText) && int.TryParse(codeText, out var parsed) => parsed,
            _ => 0
        };
    }
}
