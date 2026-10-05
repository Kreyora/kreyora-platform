using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Conversations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Conversations;

/// <summary>
/// Right-to-erasure for one customer channel identity. Owner only. Content (text, media URLs, display name,
/// reactions) is removed; timeline structure and counts stay for integrity. Changes and the audit event are
/// saved together. Audit metadata contains counts only, never content or provider IDs.
/// </summary>
public sealed class ConversationPrivacyService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer,
    IAuditEventService auditEvents) : IConversationPrivacyService
{
    public async Task<Result<IdentityErasureResult>> EraseIdentityAsync(
        string customerChannelIdentityId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.ConversationsWrite);
        var context = tenantContext.RequireCurrent();
        if (context.Role != TenantRole.Owner || context.IsReadOnlySupport)
        {
            return Result<IdentityErasureResult>.Forbidden("Only a workspace owner can erase customer conversation data.");
        }

        var identity = await dbContext.CustomerChannelIdentities
            .SingleOrDefaultAsync(i => i.TenantId == context.TenantId && i.Id == customerChannelIdentityId, cancellationToken);
        if (identity == null)
        {
            return Result<IdentityErasureResult>.NotFound("Customer identity not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var alreadyErased = !identity.Erase(now);

        var conversationIds = await dbContext.Conversations
            .Where(c => c.TenantId == context.TenantId && c.CustomerChannelIdentityId == identity.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var messages = await dbContext.Messages
            .Where(m => m.TenantId == context.TenantId && conversationIds.Contains(m.ConversationId) && m.RedactedAt == null)
            .ToListAsync(cancellationToken);
        var redacted = messages.Count(m => m.Redact(now));

        var reactions = await dbContext.MessageReactions
            .Where(r => r.TenantId == context.TenantId && r.ConnectionId == identity.ConnectionId && r.ReactorChannelId == identity.ExternalUserId)
            .ToListAsync(cancellationToken);
        dbContext.MessageReactions.RemoveRange(reactions);

        if (alreadyErased && redacted == 0 && reactions.Count == 0)
        {
            return Result<IdentityErasureResult>.Success(new IdentityErasureResult(identity.Id, 0, 0, AlreadyErased: true));
        }

        // AppendAsync saves the context: erasure and its audit record commit together.
        await auditEvents.AppendAsync(new AuditEventWrite(
            Action: "conversations.identity.erased",
            TargetType: "customer-channel-identity",
            TargetId: identity.Id,
            Metadata: JsonSerializer.Serialize(new
            {
                channel = identity.Channel.ToString(),
                messagesRedacted = redacted,
                reactionsRemoved = reactions.Count
            })), cancellationToken);

        return Result<IdentityErasureResult>.Success(
            new IdentityErasureResult(identity.Id, redacted, reactions.Count, alreadyErased));
    }
}
