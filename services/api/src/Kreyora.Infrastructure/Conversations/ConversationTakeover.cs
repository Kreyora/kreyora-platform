using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// ADR-017 takeover side effects, applied in the caller's unit of work: queued or retrying automation
/// messages are cancelled; messages already Sending (an in-flight provider call that cannot be recalled)
/// are counted and reported, never hidden.
/// </summary>
internal static class ConversationTakeover
{
    public static async Task<(int Cancelled, int InFlight)> SuppressAutomationAsync(
        AppDbContext dbContext,
        Conversation conversation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var automation = await dbContext.OutboundMessages
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == conversation.TenantId
                        && m.ConversationId == conversation.Id
                        && m.Origin == OutboundMessageOrigin.Automation
                        && (m.Status == OutboundMessageStatus.Queued
                            || m.Status == OutboundMessageStatus.Failed
                            || m.Status == OutboundMessageStatus.Sending))
            .ToListAsync(cancellationToken);

        var cancelled = 0;
        foreach (var message in automation.Where(m => m.Status != OutboundMessageStatus.Sending))
        {
            message.Cancel(now);
            cancelled++;
        }

        return (cancelled, automation.Count - cancelled);
    }
}
