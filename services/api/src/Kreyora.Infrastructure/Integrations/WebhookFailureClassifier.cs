using System.Text.Json;
using Kreyora.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Integrations;

public static class WebhookFailureClassifier
{
    public static WebhookFailureClassification Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Unwrap aggregate exceptions
        var ex = exception is AggregateException agg && agg.InnerExceptions.Count > 0
            ? agg.InnerExceptions[0]
            : exception;

        // Permanent failures: malformed syntax, schema incompatibility, invalid state
        if (ex is JsonException or
            FormatException or
            ArgumentException or
            NotSupportedException or
            KeyNotFoundException)
        {
            return WebhookFailureClassification.Permanent;
        }

        if (ex is InvalidOperationException inv &&
            (inv.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) ||
             inv.Message.Contains("poison", StringComparison.OrdinalIgnoreCase) ||
             inv.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
             inv.Message.Contains("revoked", StringComparison.OrdinalIgnoreCase) ||
             inv.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase)))
        {
            return WebhookFailureClassification.Permanent;
        }

        // Transient failures: timeouts, connection drops, rate limiting, concurrency conflicts
        if (ex is TimeoutException or
            HttpRequestException or
            TaskCanceledException or
            OperationCanceledException or
            DbUpdateConcurrencyException)
        {
            return WebhookFailureClassification.Transient;
        }

        if (ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("transient", StringComparison.OrdinalIgnoreCase))
        {
            return WebhookFailureClassification.Transient;
        }

        // Default to Transient for unclassified runtime exceptions so they get retried
        return WebhookFailureClassification.Transient;
    }
}

