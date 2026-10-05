using Kreyora.Application.Authorization;
using Kreyora.Application.Conversations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
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

        var total = await conversations.CountAsync(cancellationToken);

        var rows = await conversations
            .OrderByDescending(c => c.LastMessageAt.HasValue)
            .ThenByDescending(c => c.LastMessageAt)
            .ThenByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(
                dbContext.CustomerChannelIdentities.AsNoTracking().Where(i => i.TenantId == tenantId),
                c => c.CustomerChannelIdentityId,
                i => i.Id,
                (c, i) => new
                {
                    Conversation = c,
                    i.DisplayName,
                    i.ExternalUserId,
                    LastText = dbContext.Messages
                        .Where(m => m.TenantId == tenantId && m.ConversationId == c.Id)
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
                r.Conversation.ModifiedAt))
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

        var items = pageItems.Select(m => ConversationMapping.ToItem(m,
                reactions
                    .Where(r => r.ProviderMessageId == m.ProviderMessageId)
                    .OrderBy(r => r.Emoji, StringComparer.Ordinal)
                    .Select(r => new MessageReactionSummary(r.Emoji, r.Count))
                    .ToList()))
            .ToList();

        return Result<MessagePage>.Success(new MessagePage(items, hasMore && items.Count > 0 ? items[0].Id : null));
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
                i.ErasedAt.HasValue),
            c.UnreadCount,
            labels.GetValueOrDefault(c.Id) ?? [],
            c.AssignedUserId,
            c.AssignedAt,
            c.LastMessageAt,
            c.LastCustomerMessageAt,
            c.CustomerLastReadAt,
            c.CreatedAt,
            c.ModifiedAt);
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
