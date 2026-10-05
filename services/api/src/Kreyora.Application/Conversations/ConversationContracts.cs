using Kreyora.Application.Models;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Conversations;

/// <summary>
/// Turns one newly normalized inbound event into identity, conversation, message, receipt and reaction
/// state (ADR-016). Called by webhook processing inside the same unit of work as the inbound event, so
/// both commit atomically. Adds tracked changes only; the caller saves.
/// </summary>
public interface IConversationIngestionService
{
    Task IngestAsync(
        InboundEvent inboundEvent,
        NormalizedInboundPayload payload,
        ChannelConnection connection,
        CancellationToken cancellationToken = default);
}

public interface IConversationQueryService
{
    Task<Result<PagedResult<ConversationSummaryItem>>> ListConversationsAsync(
        ConversationQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ConversationDetailItem>> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    Task<Result<MessagePage>> GetMessagesAsync(
        string conversationId,
        string? beforeMessageId,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public interface IConversationInboxService
{
    /// <summary>Resets the staff unread count. Idempotent.</summary>
    Task<Result<ConversationDetailItem>> MarkReadAsync(
        string conversationId,
        CancellationToken cancellationToken = default);
}

public interface IConversationPrivacyService
{
    /// <summary>Owner-only, audited erasure of one customer channel identity's message content.</summary>
    Task<Result<IdentityErasureResult>> EraseIdentityAsync(
        string customerChannelIdentityId,
        CancellationToken cancellationToken = default);
}

public sealed record ConversationQuery(
    int Page = 1,
    int PageSize = 20,
    ConversationStatus? Status = null,
    string? ConnectionId = null,
    bool UnreadOnly = false,
    string? AssignedTo = null);

public sealed record ConversationSummaryItem(
    string Id,
    string ConnectionId,
    ChannelType Channel,
    ConversationStatus Status,
    string CustomerLabel,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    IReadOnlyList<string> Labels,
    string? AssignedUserId,
    DateTimeOffset? AssignedAt,
    bool IsAutomationActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record CustomerIdentitySummary(
    string Id,
    ChannelType Channel,
    string CustomerLabel,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string? CustomerId,
    bool IsErased);

public sealed record ConversationDetailItem(
    string Id,
    string ConnectionId,
    string? StoreId,
    ChannelType Channel,
    ConversationStatus Status,
    AutomationMode AutomationMode,
    bool IsAutomationActive,
    CustomerIdentitySummary Customer,
    int UnreadCount,
    IReadOnlyList<string> Labels,
    string? AssignedUserId,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset? LastCustomerMessageAt,
    DateTimeOffset? CustomerLastReadAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record MessageReactionSummary(string Emoji, int Count);

public sealed record MessageItem(
    string Id,
    string ConversationId,
    MessageDirection Direction,
    MessageOrigin Origin,
    MessageKind Kind,
    string? Text,
    string? MediaUrl,
    string? MediaContentType,
    MessageDeliveryStatus? DeliveryStatus,
    DateTimeOffset OccurredAt,
    bool IsRedacted,
    IReadOnlyList<MessageReactionSummary> Reactions);

/// <summary>Messages in chronological order; <see cref="NextBeforeMessageId"/> pages to older messages.</summary>
public sealed record MessagePage(IReadOnlyList<MessageItem> Items, string? NextBeforeMessageId);

public sealed record IdentityErasureResult(
    string CustomerChannelIdentityId,
    int MessagesRedacted,
    int ReactionsRemoved,
    bool AlreadyErased);
