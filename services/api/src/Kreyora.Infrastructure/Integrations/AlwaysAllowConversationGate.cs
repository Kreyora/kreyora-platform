using Kreyora.Application.Integrations;

namespace Kreyora.Infrastructure.Integrations;

public sealed class AlwaysAllowConversationGate : IConversationGate
{
    public Task<ConversationGateResult> CheckSendPermissionAsync(
        string tenantId,
        string connectionId,
        string? conversationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ConversationGateResult.Allow());
    }
}

