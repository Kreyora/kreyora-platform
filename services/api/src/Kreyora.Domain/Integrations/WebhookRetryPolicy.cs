namespace Kreyora.Domain.Integrations;

public sealed class WebhookRetryPolicy
{
    public const int DefaultMaxAttempts = 5;

    public static readonly WebhookRetryPolicy Default = new();

    public static readonly IReadOnlyList<TimeSpan> DefaultBackoffIntervals =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1)
    ];

    public int MaxAttempts { get; }
    public IReadOnlyList<TimeSpan>? BackoffIntervals { get; }

    public WebhookRetryPolicy(int maxAttempts = DefaultMaxAttempts, IReadOnlyList<TimeSpan>? backoffIntervals = null)
    {
        MaxAttempts = maxAttempts;
        BackoffIntervals = backoffIntervals;
    }

    public TimeSpan GetBackoffForAttempt(int attempt, double jitterRatio = 0.2)
    {
        if (attempt <= 0) return TimeSpan.Zero;
        var intervals = BackoffIntervals ?? DefaultBackoffIntervals;
        var index = Math.Min(attempt - 1, intervals.Count - 1);
        var baseTime = intervals[index];

        // Apply bounded jitter: ±jitterRatio (e.g., ±20%)
        var jitterFactor = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterRatio;
        var totalMilliseconds = Math.Max(1000, baseTime.TotalMilliseconds * jitterFactor);
        return TimeSpan.FromMilliseconds(totalMilliseconds);
    }
}

