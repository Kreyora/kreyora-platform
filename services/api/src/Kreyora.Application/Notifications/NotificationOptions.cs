using System.ComponentModel.DataAnnotations;

namespace Kreyora.Application.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    public int[] RetryBackoffMinutes { get; set; } = [1, 5, 15];

    [Range(1, 500)]
    public int ProcessorBatchSize { get; set; } = 50;

    [Range(1, 500)]
    public int DeliveryBatchSize { get; set; } = 50;
}

