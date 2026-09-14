namespace Kreyora.Domain.Notifications;

public enum NotificationStatus
{
    Pending = 1,
    Delivering = 2,
    Delivered = 3,
    Failed = 4,
    DeadLettered = 5
}

