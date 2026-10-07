using Kreyora.Application.Assistant;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant.Orchestration;

/// <summary>Operator allowlist (M09-S07 Q3), read per call so changes apply on configuration reload.</summary>
public sealed class AssistantEntitlementQuery(IOptionsMonitor<AiOptions> aiOptions) : IAssistantEntitlementQuery
{
    public bool IsEntitled(string tenantId)
    {
        var entitlements = aiOptions.CurrentValue.Entitlements;
        return entitlements.Mode == AiEntitlementMode.AllTenants || entitlements.AllowedTenantIds.Contains(tenantId, StringComparer.Ordinal);
    }
}

/// <summary>Why the guard stopped a turn: an outcome and a stable reason code.</summary>
public sealed record GuardStop(AssistantTurnOutcome Outcome, string Reason);

/// <summary>
/// The checks that must hold both before the assistant is invoked and again right before anything is enqueued
/// (M09-S07 §B, ADR-022): connection can send, customer safety, and — for assistant replies — entitlement, readiness,
/// ownership (automated, message received after the last release, no newer customer message). Reads committed state.
/// The hand-off notice only needs the connection and safety checks: it accompanies the assistant's own takeover.
/// </summary>
public sealed class AssistantSendGuard(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAssistantActivationQuery activation,
    IAssistantEntitlementQuery entitlements)
{
    public async Task<GuardStop?> CheckAsync(string conversationId, Message? trigger, bool handoffNotice, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var conversation = await dbContext.Conversations.AsNoTracking().Where(c => c.Id == conversationId && c.TenantId == tenantId)
            .Select(c => new { c.ConnectionId, c.Status, c.AutomationMode, c.AutomationResumedAt, c.CustomerChannelIdentityId })
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null) return new GuardStop(AssistantTurnOutcome.Blocked, AssistantTurnReasons.ConversationNotFound);

        var connection = await dbContext.ChannelConnections.AsNoTracking().Where(c => c.Id == conversation.ConnectionId && c.TenantId == tenantId)
            .Select(c => new { c.Status, c.Capabilities }).SingleOrDefaultAsync(cancellationToken);
        if (connection is null || connection.Status != ChannelConnectionStatus.Active || !connection.Capabilities.CanSendText)
            return new GuardStop(AssistantTurnOutcome.Blocked, AssistantTurnReasons.ConnectionUnavailable);

        var erased = await dbContext.CustomerChannelIdentities.AsNoTracking()
            .AnyAsync(i => i.Id == conversation.CustomerChannelIdentityId && i.TenantId == tenantId && i.ErasedAt != null, cancellationToken);
        if (conversation.Status == ConversationStatus.Spam || erased) return new GuardStop(AssistantTurnOutcome.Blocked, AssistantTurnReasons.CustomerSafety);

        if (handoffNotice) return null;

        if (!entitlements.IsEntitled(tenantId)) return new GuardStop(AssistantTurnOutcome.Blocked, AssistantTurnReasons.NotEntitled);
        if (conversation.AutomationMode != AutomationMode.Automated) return new GuardStop(AssistantTurnOutcome.Blocked, AssistantTurnReasons.AutomationPaused);
        if (trigger is not null)
        {
            if (conversation.AutomationResumedAt is { } resumed && trigger.ReceivedAt <= resumed)
                return new GuardStop(AssistantTurnOutcome.Blocked, AssistantTurnReasons.ReceivedBeforeRelease);
            var newer = await dbContext.Messages.AsNoTracking().AnyAsync(m => m.ConversationId == conversationId && m.TenantId == tenantId && m.Id != trigger.Id &&
                m.Direction == MessageDirection.Inbound && m.Origin == MessageOrigin.Customer && m.ReceivedAt > trigger.ReceivedAt, cancellationToken);
            if (newer) return new GuardStop(AssistantTurnOutcome.Superseded, AssistantTurnReasons.Superseded);
        }

        return await activation.IsActiveAsync(cancellationToken) ? null : new GuardStop(AssistantTurnOutcome.Blocked, AssistantTurnReasons.AssistantInactive);
    }
}
