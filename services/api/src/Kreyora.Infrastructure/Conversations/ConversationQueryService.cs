using Kreyora.Application.Authorization;
using Kreyora.Application.Conversations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// Read-only inbox queries. Every query is tenant-filtered explicitly (EF query filters are defense in depth);
/// another tenant's IDs resolve to not-found. Responses never include raw provider payloads or reactor IDs.
/// </summary>
public sealed class ConversationQueryService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer) : IConversationQueryService
{
    public const int PreviewLength = 140;
    private const int MaxMessagePageSize = 100;

    public async Task<Result<PagedResult<ConversationSummaryItem>>> ListConversationsAsync(
        ConversationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        authorizer.Demand(TenantPermissions.ConversationsRead);
        var tenantId = tenantContext.RequireCurrent().TenantId;

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var conversations = dbContext.Conversations.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (query.Status.HasValue)
        {
            conversations = conversations.Where(c => c.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ConnectionId))
        {
            conversations = conversations.Where(c => c.ConnectionId == query.ConnectionId);
        }

        if (query.UnreadOnly)
        {
            conversations = conversations.Where(c => c.UnreadCount > 0);
        }

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            conversations = conversations.Where(c => c.AssignedUserId == query.AssignedTo);
        }

        // M09-S07 Q8: how long the customer has waited for a person. Since the assistant's escalation while no person
        // has answered since; otherwise since the first of the customer's trailing messages (none when the shop spoke
        // last). Assistant replies and hand-off notices are not a person answering; staff and Instagram-app replies are.
        var shaped = conversations.Select(c => new
        {
            Conversation = c,
            WaitingSince = c.EscalatedAt != null && !dbContext.Messages.Any(m => m.TenantId == tenantId && m.ConversationId == c.Id &&
                    (m.Origin == MessageOrigin.Staff || m.Origin == MessageOrigin.ProviderNative) && m.OccurredAt >= c.EscalatedAt)
                ? c.EscalatedAt
                : dbContext.Messages
                    .Where(m => m.TenantId == tenantId && m.ConversationId == c.Id && m.Origin == MessageOrigin.Customer &&
                        !dbContext.Messages.Any(o => o.TenantId == tenantId && o.ConversationId == c.Id && o.Origin != MessageOrigin.Customer && o.OccurredAt >= m.OccurredAt))
                    .Min(m => (DateTimeOffset?)m.OccurredAt)
        });

        // The staff queue: a person owns the chat, it is open, and the customer is waiting; oldest wait first.
        if (query.NeedsPerson)
        {
            shaped = shaped.Where(r => r.Conversation.AutomationMode == AutomationMode.HumanTakeover
                && r.Conversation.Status != ConversationStatus.Spam && r.Conversation.Status != ConversationStatus.Resolved && r.Conversation.Status != ConversationStatus.Closed
                && r.WaitingSince != null);
        }

        var total = await shaped.CountAsync(cancellationToken);

        var ordered = query.NeedsPerson
            ? shaped.OrderBy(r => r.WaitingSince).ThenBy(r => r.Conversation.Id)
            : shaped
                .OrderByDescending(r => r.Conversation.LastMessageAt.HasValue)
                .ThenByDescending(r => r.Conversation.LastMessageAt)
                .ThenByDescending(r => r.Conversation.CreatedAt)
                .ThenBy(r => r.Conversation.Id);
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(
                dbContext.CustomerChannelIdentities.AsNoTracking().Where(i => i.TenantId == tenantId),
                r => r.Conversation.CustomerChannelIdentityId,
                i => i.Id,
                (r, i) => new
                {
                    r.Conversation,
                    r.WaitingSince,
                    i.DisplayName,
                    i.Username,
                    i.ExternalUserId,
                    LastText = dbContext.Messages
                        .Where(m => m.TenantId == tenantId && m.ConversationId == r.Conversation.Id)
                        .OrderByDescending(m => m.OccurredAt)
                        .ThenByDescending(m => m.CreatedAt)
                        .Select(m => m.RedactedAt != null ? null : (m.Kind == MessageKind.Media ? m.Text ?? "[media]" : m.Text))
                        .FirstOrDefault()
                })
            .ToListAsync(cancellationToken);

        var labels = await LabelsForAsync(tenantId, rows.Select(r => r.Conversation.Id).ToList(), cancellationToken);

        var items = rows.Select(r => new ConversationSummaryItem(
                r.Conversation.Id,
                r.Conversation.ConnectionId,
                r.Conversation.Channel,
                r.Conversation.Status,
                r.DisplayName ?? CustomerChannelIdentity.MaskedLabel(r.Conversation.Channel, r.ExternalUserId),
                Preview(r.LastText),
                r.Conversation.LastMessageAt,
                r.Conversation.UnreadCount,
                labels.GetValueOrDefault(r.Conversation.Id) ?? [],
                r.Conversation.AssignedUserId,
                r.Conversation.AssignedAt,
                r.Conversation.IsAutomationActive,
                r.Conversation.CreatedAt,
                r.Conversation.ModifiedAt,
                r.Conversation.EscalationCategory,
                r.WaitingSince,
                r.Username))
            .ToList();

        return Result<PagedResult<ConversationSummaryItem>>.Success(new PagedResult<ConversationSummaryItem>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<Result<ConversationDetailItem>> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.ConversationsRead);
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var detail = await BuildDetailAsync(tenantId, conversationId, cancellationToken);
        return detail == null
            ? Result<ConversationDetailItem>.NotFound("Conversation not found.")
            : Result<ConversationDetailItem>.Success(detail);
    }

    public async Task<Result<MessagePage>> GetMessagesAsync(
        string conversationId,
        string? beforeMessageId,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.ConversationsRead);
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var size = Math.Clamp(pageSize, 1, MaxMessagePageSize);

        var conversation = await dbContext.Conversations.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == conversationId)
            .Select(c => new { c.Id, c.ConnectionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation == null)
        {
            return Result<MessagePage>.NotFound("Conversation not found.");
        }

        var messages = dbContext.Messages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversation.Id);

        if (!string.IsNullOrWhiteSpace(beforeMessageId))
        {
            var cursor = await messages
                .Where(m => m.Id == beforeMessageId)
                .Select(m => new { m.OccurredAt, m.CreatedAt, m.Id })
                .SingleOrDefaultAsync(cancellationToken);
            if (cursor == null)
            {
                return Result<MessagePage>.ValidationError("The message cursor does not belong to this conversation.");
            }

            messages = messages.Where(m =>
                m.OccurredAt < cursor.OccurredAt
                || (m.OccurredAt == cursor.OccurredAt && m.CreatedAt < cursor.CreatedAt)
                || (m.OccurredAt == cursor.OccurredAt && m.CreatedAt == cursor.CreatedAt && m.Id.CompareTo(cursor.Id) < 0));
        }

        // Newest page first from the database, returned in chronological order.
        var newestFirst = await messages
            .OrderByDescending(m => m.OccurredAt)
            .ThenByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);

        var hasMore = newestFirst.Count > size;
        var pageItems = newestFirst.Take(size).Reverse().ToList();

        var providerIds = pageItems.Where(m => m.ProviderMessageId != null).Select(m => m.ProviderMessageId!).ToList();
        var reactions = await dbContext.MessageReactions.AsNoTracking()
            .Where(r => r.TenantId == tenantId
                        && r.ConnectionId == conversation.ConnectionId
                        && !r.IsRemoved
                        && providerIds.Contains(r.ProviderMessageId))
            .GroupBy(r => new { r.ProviderMessageId, r.Emoji })
            .Select(g => new { g.Key.ProviderMessageId, g.Key.Emoji, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Failure reason for failed outbound rows: the latest delivery attempt's code (M08-S06).
        var failedOutboundIds = pageItems
            .Where(m => m.DeliveryStatus == MessageDeliveryStatus.Failed && m.OutboundMessageId != null)
            .Select(m => m.OutboundMessageId!)
            .ToList();
        var failureCodes = failedOutboundIds.Count == 0
            ? new Dictionary<string, string?>()
            : (await dbContext.OutboundDeliveryAttempts.AsNoTracking()
                    .Where(a => a.TenantId == tenantId && failedOutboundIds.Contains(a.OutboundMessageId))
                    .Select(a => new { a.OutboundMessageId, a.AttemptNumber, a.StartedAt, a.ProviderErrorCode })
                    .ToListAsync(cancellationToken))
                .GroupBy(a => a.OutboundMessageId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(a => a.AttemptNumber).ThenByDescending(a => a.StartedAt).First().ProviderErrorCode);

        var items = pageItems.Select(m => ConversationMapping.ToItem(m,
                reactions
                    .Where(r => r.ProviderMessageId == m.ProviderMessageId)
                    .OrderBy(r => r.Emoji, StringComparer.Ordinal)
                    .Select(r => new MessageReactionSummary(r.Emoji, r.Count))
                    .ToList(),
                m.OutboundMessageId is not null ? failureCodes.GetValueOrDefault(m.OutboundMessageId) : null))
            .ToList();

        return Result<MessagePage>.Success(new MessagePage(items, hasMore && items.Count > 0 ? items[0].Id : null));
    }

    public async Task<Result<IReadOnlyList<ConversationAssigneeItem>>> ListAssigneesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.ConversationsRead);
        var tenantId = tenantContext.RequireCurrent().TenantId;

        var members = await dbContext.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active)
            .Join(dbContext.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m.UserId, u.DisplayName, m.Role })
            .OrderBy(m => m.DisplayName)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ConversationAssigneeItem>>.Success(
            members.Select(m => new ConversationAssigneeItem(m.UserId, m.DisplayName, m.Role)).ToList());
    }

    private async Task<ConversationDetailItem?> BuildDetailAsync(string tenantId, string conversationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Conversations.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == conversationId)
            .Join(
                dbContext.CustomerChannelIdentities.AsNoTracking().Where(i => i.TenantId == tenantId),
                c => c.CustomerChannelIdentityId,
                i => i.Id,
                (c, i) => new { Conversation = c, Identity = i })
            .SingleOrDefaultAsync(cancellationToken);
        if (row == null)
        {
            return null;
        }

        var labels = await LabelsForAsync(tenantId, [row.Conversation.Id], cancellationToken);
        var c = row.Conversation;
        var i = row.Identity;
        return new ConversationDetailItem(
            c.Id,
            c.ConnectionId,
            c.StoreId,
            c.Channel,
            c.Status,
            c.AutomationMode,
            c.IsAutomationActive,
            new CustomerIdentitySummary(
                i.Id,
                i.Channel,
                i.DisplayName ?? CustomerChannelIdentity.MaskedLabel(i.Channel, i.ExternalUserId),
                i.FirstSeenAt,
                i.LastSeenAt,
                i.CustomerId,
                i.ErasedAt.HasValue,
                i.Username),
            c.UnreadCount,
            labels.GetValueOrDefault(c.Id) ?? [],
            c.AssignedUserId,
            c.AssignedAt,
            c.LastMessageAt,
            c.LastCustomerMessageAt,
            c.CustomerLastReadAt,
            c.CreatedAt,
            c.ModifiedAt,
            c.EscalationCategory,
            c.EscalatedAt);
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LabelsForAsync(
        string tenantId,
        List<string> conversationIds,
        CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
        {
            return [];
        }

        var labels = await dbContext.ConversationLabels.AsNoTracking()
            .Where(l => l.TenantId == tenantId && conversationIds.Contains(l.ConversationId))
            .Select(l => new { l.ConversationId, l.Label })
            .ToListAsync(cancellationToken);

        return labels
            .GroupBy(l => l.ConversationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(l => l.Label).OrderBy(l => l, StringComparer.Ordinal).ToList());
    }

    private static string? Preview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= PreviewLength ? singleLine : singleLine[..PreviewLength] + "…";
    }
}
