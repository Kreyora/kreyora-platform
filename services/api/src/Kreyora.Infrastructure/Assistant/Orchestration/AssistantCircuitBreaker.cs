using System.Collections.Concurrent;
using Kreyora.Application.Ai;
using Kreyora.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant.Orchestration;

/// <summary>
/// Per model profile (M09-S06 Q8): after <c>CircuitFailureThreshold</c> provider failures (timeout, rate limit,
/// unavailable) within <c>CircuitWindowSeconds</c>, the profile is skipped for <c>CircuitOpenSeconds</c>. In-process,
/// so each API instance protects itself; settings are read per call.
/// </summary>
public sealed class AssistantCircuitBreaker(IOptionsMonitor<AiOptions> options)
{
    private readonly ConcurrentDictionary<AiModelProfile, State> states = new();

    public static bool CountsAsFailure(AiFailureKind? failure) =>
        failure is AiFailureKind.Timeout or AiFailureKind.RateLimited or AiFailureKind.ProviderUnavailable;

    public bool IsOpen(AiModelProfile profile, DateTimeOffset now) =>
        states.TryGetValue(profile, out var state) && state.IsOpen(now);

    public void RecordFailure(AiModelProfile profile, DateTimeOffset now)
    {
        var o = options.CurrentValue.Orchestration;
        states.GetOrAdd(profile, _ => new State()).Failure(now, o.CircuitFailureThreshold, TimeSpan.FromSeconds(o.CircuitWindowSeconds), TimeSpan.FromSeconds(o.CircuitOpenSeconds));
    }

    public void RecordSuccess(AiModelProfile profile)
    {
        if (states.TryGetValue(profile, out var state)) state.Reset();
    }

    private sealed class State
    {
        private readonly Queue<DateTimeOffset> failures = new();
        private DateTimeOffset? openUntil;

        public bool IsOpen(DateTimeOffset now)
        {
            lock (failures) return openUntil is { } until && until > now;
        }

        public void Failure(DateTimeOffset now, int threshold, TimeSpan window, TimeSpan openFor)
        {
            lock (failures)
            {
                failures.Enqueue(now);
                while (failures.Count > 0 && now - failures.Peek() > window) failures.Dequeue();
                if (failures.Count >= threshold)
                {
                    openUntil = now + openFor;
                    failures.Clear();
                }
            }
        }

        public void Reset()
        {
            lock (failures)
            {
                failures.Clear();
                openUntil = null;
            }
        }
    }
}
