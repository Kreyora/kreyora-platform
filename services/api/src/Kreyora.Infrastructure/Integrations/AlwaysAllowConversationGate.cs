using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;

namespace Kreyora.Infrastructure.Integrations;

/// <summary>
/// Permissive gate kept for provider-neutral M07 tests. Production uses <c>ConversationGate</c> (ADR-017).
/// </summary>
public sealed class AlwaysAllowConversationGate : IConversationGate
{
    public Task<ConversationGateResult> CheckSendPermissionAsync(
        string tenantId,
        string connectionId,
        string? conversationId,
        OutboundMessageOrigin origin,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ConversationGateResult.Allow());
    }
}
