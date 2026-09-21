namespace Kreyora.Domain.Integrations;

public enum OutboundMessageStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,
    DeadLetter = 6,
    Cancelled = 7
}

