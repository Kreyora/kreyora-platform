namespace Kreyora.Application.Integrations;

public interface IConversationGate
{
    Task<ConversationGateResult> CheckSendPermissionAsync(
        string tenantId,
        string connectionId,
        string? conversationId,
        CancellationToken cancellationToken = default);
}

public sealed record ConversationGateResult(
    bool Allowed,
    string? DenialReason = null)
{
    public static ConversationGateResult Allow() => new(true);
    public static ConversationGateResult Deny(string reason) => new(false, reason);
}

