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

    /// <summary>Active members of the current workspace (display name and role only) for assignment and sender names.</summary>
    Task<Result<IReadOnlyList<ConversationAssigneeItem>>> ListAssigneesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Staff inbox operations (ADR-016/017). All require <c>conversations.write</c>.</summary>
public interface IConversationInboxService
{
    /// <summary>Resets the staff unread count. Idempotent.</summary>
    Task<Result<ConversationDetailItem>> MarkReadAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Human takeover: automation stops and queued automation messages are cancelled atomically. Audited.</summary>
    Task<Result<ConversationDetailItem>> TakeOverAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Explicit hand-back to automation. Audited.</summary>
    Task<Result<ConversationDetailItem>> ReleaseAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Assigns to an active member of the same tenant. Audited.</summary>
    Task<Result<ConversationDetailItem>> AssignAsync(string conversationId, string userId, CancellationToken cancellationToken = default);

    Task<Result<ConversationDetailItem>> UnassignAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the label set (≤ 20 labels, ≤ 48 characters, case-insensitive de-duplication).</summary>
    Task<Result<ConversationDetailItem>> SetLabelsAsync(string conversationId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default);

    /// <summary>Resolve, reopen, close, mark or unmark spam. Invalid transitions return 409. Audited.</summary>
    Task<Result<ConversationDetailItem>> ChangeStatusAsync(string conversationId, ConversationStatusAction action, CancellationToken cancellationToken = default);
}

/// <summary>Replies into a conversation through the durable outbox (ADR-017).</summary>
public interface IConversationReplyService
{
    /// <summary>
    /// Staff reply: records a pending timeline message and the outbox message atomically; an automated
    /// conversation is taken over in the same transaction. Same idempotency key → same message.
    /// </summary>
    Task<Result<MessageItem>> SendStaffReplyAsync(
        string conversationId,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Automation (M09) placeholder: enqueues an automation message, refused while a human has taken over.
    /// Automation messages enter the timeline only when the provider accepts them. No HTTP endpoint.
    /// </summary>
    Task<Result<string>> EnqueueAutomationReplyAsync(
        string conversationId,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The assistant's hand-off notice (M09-S06, ADR-021): like an automation reply, but still delivered after the
    /// takeover it accompanies. Messaging-window and connection rules still apply.
    /// </summary>
    Task<Result<string>> EnqueueHandoffNoticeAsync(
        string conversationId,
        string text,
        string idempotencyKey,
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
    string? AssignedTo = null,
    bool NeedsPerson = false);

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
    DateTimeOffset ModifiedAt,
    string? EscalationCategory = null,
    DateTimeOffset? WaitingSince = null,
    string? CustomerUsername = null);

public sealed record CustomerIdentitySummary(
    string Id,
    ChannelType Channel,
    string CustomerLabel,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string? CustomerId,
    bool IsErased,
    string? Username = null);

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
    DateTimeOffset ModifiedAt,
    string? EscalationCategory = null,
    DateTimeOffset? EscalatedAt = null);

/// <summary>
/// The assistant's hand-over to a person (M09-S05 EscalateToHuman): a system takeover with a reason category, queued
/// automation cancelled, audited without customer text. Idempotent.
/// </summary>
public interface IConversationEscalationService
{
    Task<Result<ConversationEscalationResult>> EscalateAsync(string conversationId, string category, CancellationToken cancellationToken = default);
}

public sealed record ConversationEscalationResult(bool Changed, string Category, DateTimeOffset? EscalatedAt);

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
    IReadOnlyList<MessageReactionSummary> Reactions,
    bool IsPending = false,
    string? ActorUserId = null,
    string? DeliveryFailureCode = null);

public sealed record ConversationAssigneeItem(string UserId, string DisplayName, Kreyora.Domain.Tenancy.TenantRole Role);

/// <summary>Messages in chronological order; <see cref="NextBeforeMessageId"/> pages to older messages.</summary>
public sealed record MessagePage(IReadOnlyList<MessageItem> Items, string? NextBeforeMessageId);

public sealed record StaffReplyRequest(string Text);

public sealed record AssignConversationRequest(string UserId);

public sealed record SetConversationLabelsRequest(IReadOnlyList<string> Labels);

public sealed record ChangeConversationStatusRequest(ConversationStatusAction Action);

public sealed record IdentityErasureResult(
    string CustomerChannelIdentityId,
    int MessagesRedacted,
    int ReactionsRemoved,
    bool AlreadyErased);
