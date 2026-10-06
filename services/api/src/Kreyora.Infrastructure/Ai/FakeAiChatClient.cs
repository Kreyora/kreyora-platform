using System.Collections.Concurrent;
using Kreyora.Application.Ai;

namespace Kreyora.Infrastructure.Ai;

/// <summary>
/// Safe development fake (the default <c>Ai:Mode</c>): deterministic, no network, no data leaves the process.
/// Tests can queue exact results; otherwise it answers with a fixed, clearly-labelled placeholder that never
/// states prices, stock or other commerce facts.
/// </summary>
public sealed class FakeAiChatClient : IAiChatClient
{
    public const string ProviderName = "fake";
    public const string ModelName = "fake-deterministic-v1";
    public const string PlaceholderReply = "[Demo AI] Thanks for your message. A team member will confirm the details shortly.";

    private readonly ConcurrentQueue<AiChatResult> scripted = new();

    public IReadOnlyCollection<AiChatRequest> Requests => requests;

    private readonly ConcurrentQueue<AiChatRequest> requests = new();

    /// <summary>Queues the next result (tests and the benchmark's offline mode).</summary>
    public void Enqueue(AiChatResult result) => scripted.Enqueue(result);

    public Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        requests.Enqueue(request);
        if (scripted.TryDequeue(out var next))
        {
            return Task.FromResult(next);
        }

        return Task.FromResult(AiChatResult.Success(
            PlaceholderReply, [], AiFinishReason.Stop, new AiUsage(0, 0), ProviderName, ModelName, TimeSpan.Zero));
    }
}
