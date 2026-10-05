using Kreyora.Application.Authorization;
using Kreyora.Application.Conversations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Conversations;

public sealed class ConversationInboxService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer,
    IConversationQueryService queryService) : IConversationInboxService
{
    public async Task<Result<ConversationDetailItem>> MarkReadAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.ConversationsWrite);
        var tenantId = tenantContext.RequireCurrent().TenantId;

        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == conversationId, cancellationToken);
        if (conversation == null)
        {
            return Result<ConversationDetailItem>.NotFound("Conversation not found.");
        }

        if (conversation.UnreadCount != 0)
        {
            conversation.MarkReadByStaff();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await queryService.GetConversationAsync(conversationId, cancellationToken);
    }
}
