using Kreyora.Domain.Integrations;

namespace Kreyora.UnitTests.Integrations;

public sealed class WebhookRetryPolicyTests
{
    [Fact]
    public void GetBackoffForAttempt_WithZeroOrNegative_ReturnsZero()
    {
        var policy = new WebhookRetryPolicy();

        Assert.Equal(TimeSpan.Zero, policy.GetBackoffForAttempt(0));
        Assert.Equal(TimeSpan.Zero, policy.GetBackoffForAttempt(-1));
    }

    [Fact]
    public void GetBackoffForAttempt_WithAttempts_ReturnsBoundedIntervalsWithJitter()
    {
        var policy = new WebhookRetryPolicy();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var baseInterval = WebhookRetryPolicy.DefaultBackoffIntervals[attempt - 1];
            var backoff = policy.GetBackoffForAttempt(attempt, jitterRatio: 0.2);

            // Jitter is ±20%, so backoff must be within [base * 0.8, base * 1.2]
            var minExpected = baseInterval.TotalMilliseconds * 0.79;
            var maxExpected = baseInterval.TotalMilliseconds * 1.21;

            Assert.InRange(backoff.TotalMilliseconds, minExpected, maxExpected);
        }
    }

    [Fact]
    public void GetBackoffForAttempt_ExceedingConfiguredIntervals_CapsAtLastInterval()
    {
        var policy = new WebhookRetryPolicy();
        var lastInterval = WebhookRetryPolicy.DefaultBackoffIntervals[^1];

        var backoff = policy.GetBackoffForAttempt(10, jitterRatio: 0.0); // 0 jitter for exact check
        Assert.Equal(lastInterval, backoff);
    }
}

