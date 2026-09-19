namespace Kreyora.Domain.Integrations;

public enum WebhookProcessingStatus
{
    Received = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3,
    DeadLetter = 4
}

