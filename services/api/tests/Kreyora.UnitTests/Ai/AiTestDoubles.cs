using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Kreyora.Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kreyora.UnitTests.Ai;

/// <summary>Scripted HTTP responses; records every request so tests can assert what would have been sent.</summary>
internal sealed class StubAiHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responses = new();

    public List<(Uri Uri, string Body, string? Authorization, Dictionary<string, string> Headers)> Requests { get; } = [];

    public StubAiHandler Respond(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (retryAfter is { } delay)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
            }

            return Task.FromResult(response);
        });
        return this;
    }

    public StubAiHandler Hang()
    {
        responses.Enqueue(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public StubAiHandler Fail()
    {
        responses.Enqueue((_, _) => throw new HttpRequestException("connection refused"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!, body, request.Headers.Authorization?.ToString(),
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
        if (!responses.TryDequeue(out var next))
        {
            throw new InvalidOperationException("No scripted AI response left.");
        }

        return await next(request, cancellationToken);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Captures formatted log lines so tests can prove nothing sensitive is logged.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception));
}

internal static class AiTestSetup
{
    public const string OpenRouterKey = "sk-or-test-key-should-never-be-logged";

    public static AiOptions LiveOptions(bool withFallback = true)
    {
        var options = new AiOptions
        {
            Enabled = true,
            Mode = AiMode.Live,
            Providers =
            {
                ["OpenRouter"] = new AiProviderOptions { BaseUrl = "https://openrouter.ai/api/v1", ApiKey = OpenRouterKey },
                ["GoogleAiStudio"] = new AiProviderOptions { BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai", ApiKey = "google-test-key" }
            },
            Profiles = { ["Primary"] = new AiProfileOptions { Provider = "OpenRouter", Model = "google/gemma-4-31b-it:free" } }
        };
        if (withFallback)
        {
            options.Profiles["Fallback"] = new AiProfileOptions { Provider = "GoogleAiStudio", Model = "gemini-flash-test" };
        }

        return options;
    }

    public static (ResilientAiChatClient Client, StubAiHandler Handler, FakeAiChatClient Fake, CapturingLogger<OpenAiCompatibleChatClient> Logger) Create(AiOptions options)
    {
        var handler = new StubAiHandler();
        var logger = new CapturingLogger<OpenAiCompatibleChatClient>();
        var transport = new OpenAiCompatibleChatClient(new StubHttpClientFactory(handler), logger);
        var fake = new FakeAiChatClient();
        return (new ResilientAiChatClient(new StaticOptionsMonitor<AiOptions>(options), transport, fake), handler, fake, logger);
    }

    public static string TextCompletion(string text, string model = "google/gemma-4-31b-it:free") =>
        $$$"""{"id":"gen-1","model":"{{{model}}}","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{{{text}}}"}}],"usage":{"prompt_tokens":120,"completion_tokens":15}}""";

    public static readonly ILogger<OpenAiCompatibleChatClient> NullLogger = NullLogger<OpenAiCompatibleChatClient>.Instance;
}
