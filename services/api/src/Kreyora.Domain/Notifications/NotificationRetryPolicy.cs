namespace Kreyora.Domain.Notifications;

public sealed record NotificationRetryPolicy
{
    public const int DefaultMaxAttempts = 3;

    public static readonly IReadOnlyList<TimeSpan> DefaultBackoffIntervals =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    ];

    public int MaxAttempts { get; }
    public IReadOnlyList<TimeSpan> BackoffIntervals { get; }

    public NotificationRetryPolicy(int maxAttempts = DefaultMaxAttempts, IReadOnlyList<TimeSpan>? backoffIntervals = null)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be at least 1.");
        MaxAttempts = maxAttempts;
        BackoffIntervals = backoffIntervals ?? DefaultBackoffIntervals;
    }

    public TimeSpan GetBackoffForAttempt(int attemptNumber)
    {
        if (attemptNumber <= 0) return TimeSpan.Zero;
        var index = Math.Min(attemptNumber - 1, BackoffIntervals.Count - 1);
        return BackoffIntervals[index];
    }
}

