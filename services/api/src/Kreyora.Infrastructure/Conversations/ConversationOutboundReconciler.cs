using Kreyora.Application.Integrations;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// Keeps the timeline in step with outbox delivery (ADR-017). Staff replies already exist as pending rows;
/// automation messages are added only once accepted. If the provider's echo of this send arrived first, the
/// echo row is removed so the message appears exactly once, with its Kreyora actor and origin.
/// </summary>
public sealed class ConversationOutboundReconciler(AppDbContext dbContext) : IConversationOutboundReconciler
{
    public async Task OnDeliverySucceededAsync(
        OutboundMessage message,
        string providerMessageId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        if (message.Origin == OutboundMessageOrigin.System || string.IsNullOrWhiteSpace(message.ConversationId))
        {
            return;
        }

        var conversation = await dbContext.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == message.TenantId && c.Id == message.ConversationId, cancellationToken);
        if (conversation is null)
        {
            return;
        }

        var echo = await dbContext.Messages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == message.TenantId
                                      && m.ConnectionId == message.ConnectionId
                                      && m.ProviderMessageId == providerMessageId
                                      && m.OutboundMessageId == null, cancellationToken);
        if (echo is not null)
        {
            dbContext.Messages.Remove(echo);
        }

        if (message.Origin == OutboundMessageOrigin.Staff)
        {
            var pending = await dbContext.Messages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == message.TenantId && m.OutboundMessageId == message.Id, cancellationToken);
            if (pending is not null)
            {
                pending.RecordProviderAcceptance(providerMessageId, sentAt);
            }
            else
            {
                dbContext.Messages.Add(Message.CreateOutboundText(message.TenantId, conversation.Id, message.ConnectionId,
                    MessageOrigin.Staff, providerMessageId, message.TextContent ?? string.Empty, sentAt, sentAt, message.Id, message.ActorUserId));
            }
        }
        else
        {
            dbContext.Messages.Add(Message.CreateOutboundText(message.TenantId, conversation.Id, message.ConnectionId,
                MessageOrigin.Automation, providerMessageId, message.TextContent ?? string.Empty, sentAt, sentAt, message.Id));
        }

        conversation.RecordOutboundMessage(sentAt);
    }

    public async Task OnDeliveryFailedPermanentlyAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Origin != OutboundMessageOrigin.Staff)
        {
            return;
        }

        var pending = await dbContext.Messages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == message.TenantId && m.OutboundMessageId == message.Id, cancellationToken);
        pending?.MarkDeliveryFailed();
    }
}
