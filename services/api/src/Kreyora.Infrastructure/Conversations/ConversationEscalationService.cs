using System.Text.Json;
using Kreyora.Application.Assistant;
using Kreyora.Application.Audit;
using Kreyora.Application.Conversations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Common;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// EscalateToHuman (M09-S05 Q7): the same takeover as staff (ADR-017: automation stops, queued automation messages are
/// cancelled) performed by the system, with the reason category recorded on the conversation and in the audit log.
/// </summary>
public sealed class ConversationEscalationService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAuditEventService auditEvents,
    ITimeProvider timeProvider) : IConversationEscalationService
{
    public async Task<Result<ConversationEscalationResult>> EscalateAsync(string conversationId, string category, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        if (!AssistantEscalationCategories.All.Contains(category))
        {
            return Result<ConversationEscalationResult>.ValidationError("Unknown escalation category.");
        }

        for (var attempt = 1; ; attempt++)
        {
            var conversation = await dbContext.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenantId, cancellationToken);
            if (conversation is null) return Result<ConversationEscalationResult>.NotFound("The conversation was not found.");

            var now = timeProvider.UtcNow;
            if (!conversation.Escalate(category, now))
            {
                return Result<ConversationEscalationResult>.Success(new ConversationEscalationResult(false, conversation.EscalationCategory ?? category, conversation.EscalatedAt));
            }

            try
            {
                var (cancelled, inFlight) = await ConversationTakeover.SuppressAutomationAsync(dbContext, conversation, now, cancellationToken);
                await auditEvents.AppendAsync(new AuditEventWrite("assistant.escalated", "conversation", conversation.Id,
                    Metadata: JsonSerializer.Serialize(new { category, cancelledAutomation = cancelled, inFlightAutomation = inFlight }),
                    ActorKind: CommerceActorKind.CommerceSystem), cancellationToken);
                return Result<ConversationEscalationResult>.Success(new ConversationEscalationResult(true, category, now));
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                dbContext.ChangeTracker.Clear(); // a staff action changed it meanwhile; re-read and decide again
            }
        }
    }
}
