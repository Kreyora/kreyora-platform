using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Conversations;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// Staff inbox operations (ADR-016/017). Every change is tenant-checked explicitly, saved with its audit
/// event in one unit of work, and protected by the conversation row version: a concurrent edit returns
/// 409 <c>conversation_changed</c> instead of silently overwriting.
/// </summary>
public sealed class ConversationInboxService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer,
    IConversationQueryService queryService,
    IAuditEventService auditEvents,
    ITimeProvider timeProvider) : IConversationInboxService
{
    public const int MaxLabels = 20;

    public async Task<Result<ConversationDetailItem>> MarkReadAsync(
        string conversationId,
        CancellationToken cancellationToken = default) =>
        await MutateAsync(conversationId, async (conversation, _) =>
        {
            if (conversation.UnreadCount == 0)
            {
                return Outcome.NoChange;
            }

            conversation.MarkReadByStaff();
            await dbContext.SaveChangesAsync(cancellationToken);
            return Outcome.Saved;
        }, cancellationToken);

    public async Task<Result<ConversationDetailItem>> TakeOverAsync(string conversationId, CancellationToken cancellationToken = default) =>
        await MutateAsync(conversationId, async (conversation, _) =>
        {
            if (!conversation.TakeOver())
            {
                return Outcome.NoChange;
            }

            var suppression = await ConversationTakeover.SuppressAutomationAsync(dbContext, conversation, timeProvider.UtcNow, cancellationToken);
            await auditEvents.AppendAsync(ConversationReplyService.TakeoverAudit(conversation.Id, "manual", suppression), cancellationToken);
            return Outcome.Saved;
        }, cancellationToken);

    public async Task<Result<ConversationDetailItem>> ReleaseAsync(string conversationId, CancellationToken cancellationToken = default) =>
        await MutateAsync(conversationId, async (conversation, _) =>
        {
            if (!conversation.Release())
            {
                return Outcome.NoChange;
            }

            await auditEvents.AppendAsync(Audit("conversations.released", conversation.Id, new { }), cancellationToken);
            return Outcome.Saved;
        }, cancellationToken);

    public async Task<Result<ConversationDetailItem>> AssignAsync(string conversationId, string userId, CancellationToken cancellationToken = default) =>
        await MutateAsync(conversationId, async (conversation, tenantId) =>
        {
            var isActiveMember = !string.IsNullOrWhiteSpace(userId) && await dbContext.Memberships
                .AnyAsync(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active, cancellationToken);
            if (!isActiveMember)
            {
                return Outcome.Fail(ConversationDenialReasons.AssigneeNotMember, "The assignee must be an active member of this workspace.");
            }

            if (conversation.AssignedUserId == userId)
            {
                return Outcome.NoChange;
            }

            var previous = conversation.AssignedUserId;
            conversation.Assign(userId, timeProvider.UtcNow);
            await auditEvents.AppendAsync(Audit("conversations.assigned", conversation.Id, new { previousAssigneeId = previous, assigneeId = userId }), cancellationToken);
            return Outcome.Saved;
        }, cancellationToken);

    public async Task<Result<ConversationDetailItem>> UnassignAsync(string conversationId, CancellationToken cancellationToken = default) =>
        await MutateAsync(conversationId, async (conversation, _) =>
        {
            if (conversation.AssignedUserId is null)
            {
                return Outcome.NoChange;
            }

            var previous = conversation.AssignedUserId;
            conversation.Unassign();
            await auditEvents.AppendAsync(Audit("conversations.unassigned", conversation.Id, new { previousAssigneeId = previous }), cancellationToken);
            return Outcome.Saved;
        }, cancellationToken);

    public async Task<Result<ConversationDetailItem>> SetLabelsAsync(
        string conversationId,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken = default) =>
        await MutateAsync(conversationId, async (conversation, tenantId) =>
        {
            var normalized = (labels ?? [])
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .DistinctBy(l => l.ToUpperInvariant())
                .ToList();
            if (normalized.Count > MaxLabels || normalized.Any(l => l.Length > ConversationLabel.LabelMaxLength))
            {
                return Outcome.Fail("invalid_labels", $"Use at most {MaxLabels} labels of up to {ConversationLabel.LabelMaxLength} characters.");
            }

            var current = await dbContext.ConversationLabels
                .Where(l => l.TenantId == tenantId && l.ConversationId == conversation.Id)
                .ToListAsync(cancellationToken);
            var keep = new HashSet<string>(normalized.Select(l => l.ToUpperInvariant()), StringComparer.Ordinal);
            var existing = new HashSet<string>(current.Select(l => l.Label.ToUpperInvariant()), StringComparer.Ordinal);

            dbContext.ConversationLabels.RemoveRange(current.Where(l => !keep.Contains(l.Label.ToUpperInvariant())));
            dbContext.ConversationLabels.AddRange(normalized
                .Where(l => !existing.Contains(l.ToUpperInvariant()))
                .Select(l => ConversationLabel.Create(tenantId, conversation.Id, l)));

            // Touch the conversation so concurrent label edits conflict on its row version.
            conversation.ModifiedAt = timeProvider.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Outcome.Saved;
        }, cancellationToken);

    public async Task<Result<ConversationDetailItem>> ChangeStatusAsync(
        string conversationId,
        ConversationStatusAction action,
        CancellationToken cancellationToken = default) =>
        await MutateAsync(conversationId, async (conversation, _) =>
        {
            var previous = conversation.Status;
            bool changed;
            try
            {
                changed = conversation.ApplyStatusAction(action);
            }
            catch (InvalidOperationException ex)
            {
                return Outcome.Fail(ConversationDenialReasons.InvalidTransition, ex.Message, 409);
            }

            if (!changed)
            {
                return Outcome.NoChange;
            }

            await auditEvents.AppendAsync(Audit("conversations.status_changed", conversation.Id,
                new { action = action.ToString(), from = previous.ToString(), to = conversation.Status.ToString() }), cancellationToken);
            return Outcome.Saved;
        }, cancellationToken);

    private async Task<Result<ConversationDetailItem>> MutateAsync(
        string conversationId,
        Func<Conversation, string, Task<Outcome>> mutation,
        CancellationToken cancellationToken)
    {
        authorizer.Demand(TenantPermissions.ConversationsWrite);
        var context = tenantContext.RequireCurrent();
        if (context.IsReadOnlySupport)
        {
            return Result<ConversationDetailItem>.Forbidden("Read-only support access cannot change conversations.");
        }

        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(c => c.TenantId == context.TenantId && c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return Result<ConversationDetailItem>.NotFound("Conversation not found.");
        }

        Outcome outcome;
        try
        {
            outcome = await mutation(conversation, context.TenantId);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return ConversationMapping.Denied<ConversationDetailItem>(ConversationDenialReasons.ConversationChanged,
                "The conversation was changed by someone else. Refresh and try again.", 409);
        }

        if (outcome.FailureCode is not null)
        {
            dbContext.ChangeTracker.Clear();
            return ConversationMapping.Denied<ConversationDetailItem>(outcome.FailureCode, outcome.FailureDetail!, outcome.FailureStatus);
        }

        return await queryService.GetConversationAsync(conversationId, cancellationToken);
    }

    private static AuditEventWrite Audit(string action, string conversationId, object metadata) => new(
        Action: action,
        TargetType: "conversation",
        TargetId: conversationId,
        Metadata: JsonSerializer.Serialize(metadata));

    private sealed record Outcome(string? FailureCode = null, string? FailureDetail = null, int FailureStatus = 422)
    {
        public static readonly Outcome Saved = new();
        public static readonly Outcome NoChange = new();

        public static Outcome Fail(string code, string detail, int status = 422) => new(code, detail, status);
    }
}
