namespace Kreyora.Domain.Integrations;

public enum ChannelConnectionStatus
{
    Pending = 1,
    Active = 2,
    Connected = Active,
    Degraded = 3,
    Expired = 4,
    Revoked = 5,
    Disabled = 6
}

