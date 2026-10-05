using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// ADR-017 send gate, evaluated against committed state (no-tracking reads) at enqueue and again right
/// before delivery. <see cref="OutboundMessageOrigin.System"/> messages (provider-neutral M07 outbox API)
/// are not conversation-bound and keep their M07 behaviour.
/// </summary>
public sealed class ConversationGate(
    AppDbContext dbContext,
    ITimeProvider timeProvider,
    IOptions<InstagramMessagingOptions> messagingOptions) : IConversationGate
{
    public const string HumanAgentTag = "HUMAN_AGENT";

    public async Task<ConversationGateResult> CheckSendPermissionAsync(
        string tenantId,
        string connectionId,
        string? conversationId,
        OutboundMessageOrigin origin,
        CancellationToken cancellationToken = default)
    {
        if (origin == OutboundMessageOrigin.System)
        {
            return ConversationGateResult.Allow();
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return ConversationGateResult.Deny("Conversation replies must reference a conversation.", ConversationDenialReasons.ConversationNotFound);
        }

        var conversation = await dbContext.Conversations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == conversationId && c.ConnectionId == connectionId)
            .Select(c => new { c.Status, c.AutomationMode, c.LastCustomerMessageAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (conversation is null)
        {
            return ConversationGateResult.Deny("The conversation was not found for this connection.", ConversationDenialReasons.ConversationNotFound);
        }

        if (conversation.Status == ConversationStatus.Spam)
        {
            return ConversationGateResult.Deny("Replies to conversations marked as spam are blocked.", ConversationDenialReasons.ConversationIsSpam);
        }

        if (origin == OutboundMessageOrigin.Automation && conversation.AutomationMode == AutomationMode.HumanTakeover)
        {
            return ConversationGateResult.Deny("A human has taken over this conversation; automation may not send.", ConversationDenialReasons.AutomationPausedByTakeover);
        }

        var connection = await dbContext.ChannelConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == connectionId)
            .Select(c => new { c.Channel, c.Capabilities })
            .SingleOrDefaultAsync(cancellationToken);

        if (connection is null)
        {
            return ConversationGateResult.Deny("The channel connection was not found.", ConversationDenialReasons.ConnectionInactive);
        }

        if (!connection.Capabilities.Enforces24HourWindow)
        {
            return ConversationGateResult.Allow();
        }

        return InstagramWindowEvaluator.Evaluate(conversation.LastCustomerMessageAt, timeProvider.UtcNow) switch
        {
            InstagramWindowState.WindowOpen => ConversationGateResult.Allow(),
            InstagramWindowState.WindowExpiredHumanAgentEligible
                when origin == OutboundMessageOrigin.Staff
                     && connection.Channel == ChannelType.Instagram
                     && messagingOptions.Value.HumanAgentTagApproved => ConversationGateResult.Allow(HumanAgentTag),
            InstagramWindowState.WindowExpiredHumanAgentEligible => ConversationGateResult.Deny(
                "The 24-hour messaging window has closed. Late human replies need Meta's Human Agent approval, which is not enabled.",
                ConversationDenialReasons.WindowClosedHumanAgentUnavailable),
            _ => ConversationGateResult.Deny(
                "The customer has not messaged within the provider's reply window.",
                ConversationDenialReasons.WindowClosed)
        };
    }
}
