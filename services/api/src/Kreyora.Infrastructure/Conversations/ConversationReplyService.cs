using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Conversations;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// Conversation replies through the durable outbox (ADR-017). A staff reply commits its outbox message,
/// pending timeline entry, any implicit takeover (with automation suppression) and the takeover audit in a
/// single save. Authorization is enforced here (<c>conversations.write</c>), not by the M07 outbox API.
/// </summary>
public sealed class ConversationReplyService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer,
    IOutboundEnqueuer enqueuer,
    IAuditEventService auditEvents,
    ITimeProvider timeProvider,
    IOptions<InstagramMessagingOptions> messagingOptions) : IConversationReplyService
{
    public async Task<Result<MessageItem>> SendStaffReplyAsync(
        string conversationId,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.ConversationsWrite);
        var context = tenantContext.RequireCurrent();
        if (string.IsNullOrWhiteSpace(context.UserId) || context.IsReadOnlySupport)
        {
            return Result<MessageItem>.Forbidden("A signed-in workspace member is required to reply.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<MessageItem>.ValidationError("An Idempotency-Key is required to send a reply.");
        }

        // A concurrent change to the conversation (e.g. an inbound message updating unread/window fields)
        // conflicts on its row version; the idempotency key makes one clean retry safe.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendStaffReplyOnceAsync(context, conversationId, text, idempotencyKey, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                if (attempt >= 2)
                {
                    return ConversationMapping.Denied<MessageItem>(ConversationDenialReasons.ConversationChanged,
                        "The conversation kept changing while sending. Refresh and try again.", 409);
                }
            }
        }
    }

    private async Task<Result<MessageItem>> SendStaffReplyOnceAsync(
        TenantContext context,
        string conversationId,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(c => c.TenantId == context.TenantId && c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return Result<MessageItem>.NotFound("Conversation not found.");
        }

        var existing = await FindReplyByKeyAsync(context.TenantId, conversation.ConnectionId, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Result<MessageItem>.Success(existing);
        }

        var body = text?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            return ConversationMapping.Denied<MessageItem>(ConversationDenialReasons.TextRequired, "Reply text is required.");
        }

        var maxLength = conversation.Channel == ChannelType.Instagram
            ? messagingOptions.Value.MaxTextLength
            : OutboundMessage.TextContentMaxLength;
        if (body.Length > maxLength)
        {
            return ConversationMapping.Denied<MessageItem>(ConversationDenialReasons.TextTooLong,
                $"Reply text exceeds the {maxLength}-character limit for this channel.");
        }

        var recipient = await dbContext.CustomerChannelIdentities
            .Where(i => i.TenantId == context.TenantId && i.Id == conversation.CustomerChannelIdentityId)
            .Select(i => i.ExternalUserId)
            .SingleAsync(cancellationToken);

        var now = timeProvider.UtcNow;

        // Q1 (ADR-017): a staff reply takes over an automated conversation in the same transaction.
        var tookOver = conversation.TakeOver();
        var suppression = tookOver
            ? await ConversationTakeover.SuppressAutomationAsync(dbContext, conversation, now, cancellationToken)
            : (Cancelled: 0, InFlight: 0);

        var enqueue = await enqueuer.EnqueueAsync(new OutboundEnqueueRequest(
            context.TenantId, conversation.ConnectionId, conversation.Id, recipient, idempotencyKey, body,
            OutboundMessageOrigin.Staff, context.UserId), cancellationToken);

        if (!enqueue.Allowed)
        {
            dbContext.ChangeTracker.Clear();
            return ConversationMapping.Denied<MessageItem>(enqueue.ReasonCode ?? "send_blocked", enqueue.DenialReason ?? "The reply was blocked.");
        }

        if (enqueue.Message is null)
        {
            // Idempotency key used concurrently by another request: return that reply.
            dbContext.ChangeTracker.Clear();
            var duplicate = await FindReplyByKeyAsync(context.TenantId, conversation.ConnectionId, idempotencyKey, cancellationToken);
            return duplicate is null
                ? ConversationMapping.Denied<MessageItem>(ConversationDenialReasons.ConversationChanged, "The reply key was used by another request.", 409)
                : Result<MessageItem>.Success(duplicate);
        }

        var pending = Message.CreatePendingStaffReply(
            context.TenantId, conversation.Id, conversation.ConnectionId, enqueue.Message.Id, context.UserId!, body, now);
        dbContext.Messages.Add(pending);
        conversation.RecordOutboundMessage(now);

        try
        {
            if (tookOver)
            {
                // AppendAsync saves the whole unit of work: outbox, timeline, takeover, cancellations, audit.
                await auditEvents.AppendAsync(TakeoverAudit(conversation.Id, "staff_reply", suppression), cancellationToken);
            }
            else
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var duplicate = await FindReplyByKeyAsync(context.TenantId, conversation.ConnectionId, idempotencyKey, cancellationToken);
            return duplicate is null
                ? ConversationMapping.Denied<MessageItem>(ConversationDenialReasons.ConversationChanged,
                    "The conversation changed while sending. Refresh and try again.", 409)
                : Result<MessageItem>.Success(duplicate);
        }

        return Result<MessageItem>.Success(ConversationMapping.ToItem(pending, []));
    }

    public async Task<Result<string>> EnqueueAutomationReplyAsync(
        string conversationId,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var context = tenantContext.RequireCurrent();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(text))
        {
            return Result<string>.ValidationError("Text and an idempotency key are required.");
        }

        var conversation = await dbContext.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == context.TenantId && c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return Result<string>.NotFound("Conversation not found.");
        }

        var recipient = await dbContext.CustomerChannelIdentities
            .Where(i => i.TenantId == context.TenantId && i.Id == conversation.CustomerChannelIdentityId)
            .Select(i => i.ExternalUserId)
            .SingleAsync(cancellationToken);

        var enqueue = await enqueuer.EnqueueAsync(new OutboundEnqueueRequest(
            context.TenantId, conversation.ConnectionId, conversation.Id, recipient, idempotencyKey, text.Trim(),
            OutboundMessageOrigin.Automation, null), cancellationToken);

        if (!enqueue.Allowed)
        {
            dbContext.ChangeTracker.Clear();
            return ConversationMapping.Denied<string>(enqueue.ReasonCode ?? "send_blocked", enqueue.DenialReason ?? "Automation was blocked.");
        }

        if (enqueue.Message is null)
        {
            return Result<string>.Success(enqueue.ExistingOutboundMessageId!);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<string>.Success(enqueue.Message.Id);
    }

    internal static AuditEventWrite TakeoverAudit(string conversationId, string trigger, (int Cancelled, int InFlight) suppression) => new(
        Action: "conversations.takeover",
        TargetType: "conversation",
        TargetId: conversationId,
        Metadata: JsonSerializer.Serialize(new
        {
            trigger,
            automationMessagesCancelled = suppression.Cancelled,
            automationMessagesInFlight = suppression.InFlight
        }));

    private async Task<MessageItem?> FindReplyByKeyAsync(string tenantId, string connectionId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var outboundId = await dbContext.OutboundMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConnectionId == connectionId && m.IdempotencyKey == idempotencyKey)
            .Select(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (outboundId is null)
        {
            return null;
        }

        var message = await dbContext.Messages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.OutboundMessageId == outboundId, cancellationToken);
        return message is null ? null : ConversationMapping.ToItem(message, []);
    }
}
