namespace Kreyora.Domain.Integrations;

public enum WebhookFailureClassification
{
    Transient = 1,
    Permanent = 2,
    Exhausted = 3
}

