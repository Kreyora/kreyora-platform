namespace Kreyora.Domain.Integrations;

/// <summary>
/// Who caused an outbound message (ADR-017). Automation messages are subject to the human-takeover gate;
/// <see cref="System"/> covers the provider-neutral M07 outbox API and messages created before origin existed.
/// </summary>
public enum OutboundMessageOrigin
{
    System = 0,
    Staff = 1,
    Automation = 2
}
