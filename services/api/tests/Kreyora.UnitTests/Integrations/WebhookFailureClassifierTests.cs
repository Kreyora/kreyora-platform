using System.Text.Json;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.UnitTests.Integrations;

public sealed class WebhookFailureClassifierTests
{
    [Fact]
    public void Classify_WithTimeoutException_ReturnsTransient()
    {
        var ex = new TimeoutException("Connection timed out after 30s");
        Assert.Equal(WebhookFailureClassification.Transient, WebhookFailureClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_WithHttpRequestException_ReturnsTransient()
    {
        var ex = new HttpRequestException("Network failure while connecting to provider");
        Assert.Equal(WebhookFailureClassification.Transient, WebhookFailureClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_WithDbUpdateConcurrencyException_ReturnsTransient()
    {
        var ex = new DbUpdateConcurrencyException("Concurrency token xmin conflict detected");
        Assert.Equal(WebhookFailureClassification.Transient, WebhookFailureClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_WithRateLimitOr429Message_ReturnsTransient()
    {
        var ex = new InvalidOperationException("Provider returned 429 Too Many Requests");
        Assert.Equal(WebhookFailureClassification.Transient, WebhookFailureClassifier.Classify(ex));

        var ex2 = new InvalidOperationException("API rate limit exceeded");
        Assert.Equal(WebhookFailureClassification.Transient, WebhookFailureClassifier.Classify(ex2));
    }

    [Fact]
    public void Classify_WithJsonException_ReturnsPermanent()
    {
        var ex = new JsonException("Invalid JSON token at line 1");
        Assert.Equal(WebhookFailureClassification.Permanent, WebhookFailureClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_WithFormatException_ReturnsPermanent()
    {
        var ex = new FormatException("Invalid GUID format");
        Assert.Equal(WebhookFailureClassification.Permanent, WebhookFailureClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_WithUnsupportedSchemaOrPoisonException_ReturnsPermanent()
    {
        var ex = new InvalidOperationException("Unsupported schema version 'v99'.");
        Assert.Equal(WebhookFailureClassification.Permanent, WebhookFailureClassifier.Classify(ex));

        var ex2 = new InvalidOperationException("Poison payload encountered.");
        Assert.Equal(WebhookFailureClassification.Permanent, WebhookFailureClassifier.Classify(ex2));

        var ex3 = new InvalidOperationException("Connection 'conn_1' is in 'Disabled' status and cannot process webhooks.");
        Assert.Equal(WebhookFailureClassification.Permanent, WebhookFailureClassifier.Classify(ex3));
    }

    [Fact]
    public void Classify_WithWrappedAggregateException_UnwrapsAndClassifiesInner()
    {
        var inner = new JsonException("Poison JSON");
        var agg = new AggregateException("Task failed", inner);

        Assert.Equal(WebhookFailureClassification.Permanent, WebhookFailureClassifier.Classify(agg));
    }
}

