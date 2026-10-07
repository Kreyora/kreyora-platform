using Kreyora.Application.Conversations;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// Applies ADR-016 rules to a newly normalized inbound event. Only adds/updates tracked entities; webhook
/// processing saves them together with the inbound event (one transaction). Lookups check pending (unsaved)
/// entities first because one delivery can carry several events for the same new customer.
/// Concurrent first contact is resolved by unique indexes plus the processing retry.
/// </summary>
public sealed partial class ConversationIngestionService(
    AppDbContext dbContext,
    ILogger<ConversationIngestionService> logger,
    Kreyora.Application.Assistant.IAssistantInboundHook? assistantHook = null) : IConversationIngestionService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Inbound message for connection {ConnectionId} already exists in the timeline; skipped (inbound event {InboundEventId})")]
    private static partial void LogMessageExists(ILogger logger, string connectionId, string inboundEventId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Read receipt for connection {ConnectionId} references no known customer; skipped (inbound event {InboundEventId})")]
    private static partial void LogReceiptWithoutIdentity(ILogger logger, string connectionId, string inboundEventId);

    public async Task IngestAsync(
        InboundEvent inboundEvent,
        NormalizedInboundPayload payload,
        ChannelConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inboundEvent);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(connection);

        if (!string.Equals(inboundEvent.TenantId, connection.TenantId, StringComparison.Ordinal)
            || !string.Equals(inboundEvent.ConnectionId, connection.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Inbound event does not belong to the supplied connection (permanent).");
        }

        switch (payload)
        {
            case TextMessageReceivedPayload { IsEcho: true } echoText:
                await IngestEchoAsync(inboundEvent, connection, echoText.RecipientChannelId, echoText.MessageId, echoText.Text, null, null, echoText.Timestamp, cancellationToken);
                break;

            case MediaMessageReceivedPayload { IsEcho: true } echoMedia:
                await IngestEchoAsync(inboundEvent, connection, echoMedia.RecipientChannelId, echoMedia.MessageId, echoMedia.Caption,
                    echoMedia.MediaUrl, echoMedia.ContentType, echoMedia.Timestamp, cancellationToken);
                break;

            case TextMessageReceivedPayload text:
                await IngestMessageAsync(inboundEvent, connection, text.SenderChannelId, text.SenderName, text.MessageId, text.Timestamp,
                    (conversationId, receivedAt) => Message.CreateInboundText(
                        connection.TenantId, conversationId, connection.Id, inboundEvent.Id, text.MessageId, text.Text, text.Timestamp, receivedAt),
                    cancellationToken);
                break;

            case MediaMessageReceivedPayload media:
                await IngestMessageAsync(inboundEvent, connection, media.SenderChannelId, media.SenderName, media.MessageId, media.Timestamp,
                    (conversationId, receivedAt) => Message.CreateInboundMedia(
                        connection.TenantId, conversationId, connection.Id, inboundEvent.Id, media.MessageId, media.MediaUrl,
                        media.ContentType, media.Caption, media.Timestamp, receivedAt, media.SharedPostId),
                    cancellationToken);
                break;

            case MessageStatusUpdatedPayload status:
                await ApplyStatusAsync(inboundEvent, connection, status, cancellationToken);
                break;

            case ReactionReceivedPayload reaction:
                await ApplyReactionAsync(connection, reaction, cancellationToken);
                break;

            case CustomerProfileUpdatedPayload profile:
                var identity = await FindIdentityAsync(connection, profile.SenderChannelId, cancellationToken);
                identity?.UpdateDisplayName(profile.DisplayName);
                break;
        }
    }

    private async Task IngestMessageAsync(
        InboundEvent inboundEvent,
        ChannelConnection connection,
        string senderChannelId,
        string? senderName,
        string providerMessageId,
        DateTimeOffset occurredAt,
        Func<string, DateTimeOffset, Message> createMessage,
        CancellationToken cancellationToken)
    {
        if (await MessageExistsAsync(connection.Id, providerMessageId, cancellationToken))
        {
            LogMessageExists(logger, connection.Id, inboundEvent.Id);
            return;
        }

        var identity = await FindIdentityAsync(connection, senderChannelId, cancellationToken);
        if (identity == null)
        {
            identity = CustomerChannelIdentity.Create(connection.TenantId, connection.Id, connection.Channel, senderChannelId, occurredAt);
            dbContext.CustomerChannelIdentities.Add(identity);
        }
        else
        {
            identity.RecordActivity(occurredAt);
        }

        identity.UpdateDisplayName(senderName);

        var conversation = await FindConversationAsync(connection, identity.Id, cancellationToken);
        if (conversation == null)
        {
            conversation = Conversation.Start(connection.TenantId, connection.Id, connection.StoreId, identity.Id, connection.Channel);
            dbContext.Conversations.Add(conversation);
        }

        conversation.RecordInboundMessage(occurredAt);
        var message = createMessage(conversation.Id, DateTimeOffset.UtcNow);
        dbContext.Messages.Add(message);
        assistantHook?.CustomerMessageReceived(connection.TenantId, conversation.Id, message.Id); // scheduled only after commit (M09-S07)
    }

    /// <summary>
    /// A business message reported back by the provider (ADR-017). Kreyora's own sends already have a timeline
    /// row with this provider ID and are skipped; messages typed in the provider's app become ProviderNative
    /// rows. Echoes never count as customer activity or unread. Since M09-S07 (ADR-022) an echo still unmatched after a
    /// short delay takes the conversation over: the seller answered from the app.
    /// </summary>
    private async Task IngestEchoAsync(
        InboundEvent inboundEvent,
        ChannelConnection connection,
        string? customerChannelId,
        string providerMessageId,
        string? text,
        string? mediaUrl,
        string? mediaContentType,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerChannelId))
        {
            return;
        }

        if (await MessageExistsAsync(connection.Id, providerMessageId, cancellationToken))
        {
            LogMessageExists(logger, connection.Id, inboundEvent.Id);
            return;
        }

        var identity = await FindIdentityAsync(connection, customerChannelId, cancellationToken);
        if (identity == null)
        {
            // Seller-initiated thread from the provider app.
            identity = CustomerChannelIdentity.Create(connection.TenantId, connection.Id, connection.Channel, customerChannelId, occurredAt);
            dbContext.CustomerChannelIdentities.Add(identity);
        }

        var conversation = await FindConversationAsync(connection, identity.Id, cancellationToken);
        if (conversation == null)
        {
            conversation = Conversation.Start(connection.TenantId, connection.Id, connection.StoreId, identity.Id, connection.Channel);
            dbContext.Conversations.Add(conversation);
        }

        conversation.RecordOutboundMessage(occurredAt);
        var echo = Message.CreateProviderNativeEcho(
            connection.TenantId, conversation.Id, connection.Id, inboundEvent.Id, providerMessageId,
            text, mediaUrl, mediaContentType, occurredAt, DateTimeOffset.UtcNow);
        dbContext.Messages.Add(echo);
        // M09-S07 Q6 (ADR-022): if it stays unmatched to our own sends, the seller replied from the app → takeover.
        assistantHook?.NativeReplyReceived(connection.TenantId, echo.Id);
    }

    private async Task ApplyStatusAsync(
        InboundEvent inboundEvent,
        ChannelConnection connection,
        MessageStatusUpdatedPayload status,
        CancellationToken cancellationToken)
    {
        // Outbound timeline entries (from S05 onward) advance monotonically; unknown IDs are fine.
        var message = await FindMessageAsync(connection.Id, status.MessageId, cancellationToken);
        message?.AdvanceDeliveryStatus(status.Status);

        if (status.Status != MessageDeliveryStatus.Read)
        {
            return;
        }

        // For read receipts the "recipient" of the status is the customer who read the business message.
        var identity = await FindIdentityAsync(connection, status.RecipientChannelId, cancellationToken);
        if (identity == null)
        {
            LogReceiptWithoutIdentity(logger, connection.Id, inboundEvent.Id);
            return;
        }

        identity.RecordActivity(status.Timestamp);
        var conversation = await FindConversationAsync(connection, identity.Id, cancellationToken);
        conversation?.RecordCustomerRead(status.Timestamp);
    }

    private async Task ApplyReactionAsync(
        ChannelConnection connection,
        ReactionReceivedPayload reaction,
        CancellationToken cancellationToken)
    {
        var current = dbContext.MessageReactions.Local.FirstOrDefault(r =>
                          r.ConnectionId == connection.Id
                          && r.ProviderMessageId == reaction.MessageId
                          && r.ReactorChannelId == reaction.SenderChannelId)
                      ?? await dbContext.MessageReactions.FirstOrDefaultAsync(r =>
                          r.TenantId == connection.TenantId
                          && r.ConnectionId == connection.Id
                          && r.ProviderMessageId == reaction.MessageId
                          && r.ReactorChannelId == reaction.SenderChannelId, cancellationToken);

        if (current == null)
        {
            dbContext.MessageReactions.Add(MessageReaction.Create(
                connection.TenantId, connection.Id, reaction.MessageId, reaction.SenderChannelId,
                reaction.Emoji, reaction.IsRemoved, reaction.Timestamp));
        }
        else
        {
            current.Apply(reaction.Emoji, reaction.IsRemoved, reaction.Timestamp);
        }

        var identity = await FindIdentityAsync(connection, reaction.SenderChannelId, cancellationToken);
        identity?.RecordActivity(reaction.Timestamp);
    }

    private async Task<CustomerChannelIdentity?> FindIdentityAsync(
        ChannelConnection connection,
        string externalUserId,
        CancellationToken cancellationToken) =>
        dbContext.CustomerChannelIdentities.Local.FirstOrDefault(i =>
            i.ConnectionId == connection.Id && i.ExternalUserId == externalUserId)
        ?? await dbContext.CustomerChannelIdentities.FirstOrDefaultAsync(i =>
            i.TenantId == connection.TenantId && i.ConnectionId == connection.Id && i.ExternalUserId == externalUserId,
            cancellationToken);

    private async Task<Conversation?> FindConversationAsync(
        ChannelConnection connection,
        string identityId,
        CancellationToken cancellationToken) =>
        dbContext.Conversations.Local.FirstOrDefault(c =>
            c.ConnectionId == connection.Id && c.CustomerChannelIdentityId == identityId)
        ?? await dbContext.Conversations.FirstOrDefaultAsync(c =>
            c.TenantId == connection.TenantId && c.ConnectionId == connection.Id && c.CustomerChannelIdentityId == identityId,
            cancellationToken);

    private async Task<Message?> FindMessageAsync(string connectionId, string providerMessageId, CancellationToken cancellationToken) =>
        dbContext.Messages.Local.FirstOrDefault(m => m.ConnectionId == connectionId && m.ProviderMessageId == providerMessageId)
        ?? await dbContext.Messages.FirstOrDefaultAsync(m => m.ConnectionId == connectionId && m.ProviderMessageId == providerMessageId, cancellationToken);

    private async Task<bool> MessageExistsAsync(string connectionId, string providerMessageId, CancellationToken cancellationToken) =>
        dbContext.Messages.Local.Any(m => m.ConnectionId == connectionId && m.ProviderMessageId == providerMessageId)
        || await dbContext.Messages.AnyAsync(m => m.ConnectionId == connectionId && m.ProviderMessageId == providerMessageId, cancellationToken);
}
