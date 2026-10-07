using Kreyora.Domain.Common;
using Kreyora.Domain.Integrations;

namespace Kreyora.Domain.Conversations;

/// <summary>Plan §10.4 conversation states; <see cref="Closed"/> and <see cref="Spam"/> are dispositions.</summary>
public enum ConversationStatus
{
    New = 1,
    BotActive = 2,
    HumanAssigned = 3,
    AwaitingCustomer = 4,
    CheckoutInProgress = 5,
    OrderCreated = 6,
    Resolved = 7,
    Closed = 8,
    Spam = 9
}

/// <summary>Staff-initiated status changes (ADR-017).</summary>
public enum ConversationStatusAction
{
    Resolve = 1,
    Reopen = 2,
    Close = 3,
    MarkSpam = 4,
    UnmarkSpam = 5
}

/// <summary>The single current owner mode of a conversation (plan §10.4).</summary>
public enum AutomationMode
{
    Automated = 1,
    HumanTakeover = 2
}

/// <summary>
/// One continuous thread per customer channel identity per connection (ADR-016). Assignment, labels and
/// automation fields are stored here; their operations arrive in M08-S05.
/// </summary>
public sealed class Conversation : BaseEntity, ITenantOwned
{
    private Conversation() { }

    public string TenantId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public string? StoreId { get; private set; }
    public string CustomerChannelIdentityId { get; private set; } = string.Empty;
    public ChannelType Channel { get; private set; }
    public ConversationStatus Status { get; private set; }
    public AutomationMode AutomationMode { get; private set; }
    public string? AssignedUserId { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public int UnreadCount { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public DateTimeOffset? LastCustomerMessageAt { get; private set; }
    public DateTimeOffset? CustomerLastReadAt { get; private set; }

    /// <summary>Why the assistant handed this conversation to a person (M09-S05); a fixed category, never customer text.</summary>
    public string? EscalationCategory { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>
    /// When automation was last handed back (M09-S07 Q7). Customer messages received before it belong to the person who
    /// owned the conversation; the assistant answers only later messages.
    /// </summary>
    public DateTimeOffset? AutomationResumedAt { get; private set; }

    public bool IsAutomationActive => AutomationMode == AutomationMode.Automated;

    /// <summary>New threads start as <see cref="ConversationStatus.New"/> with automation as owner (plan §10.4 new → bot_active).</summary>
    public static Conversation Start(
        string tenantId,
        string connectionId,
        string? storeId,
        string customerChannelIdentityId,
        ChannelType channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerChannelIdentityId);

        return new Conversation
        {
            TenantId = tenantId,
            ConnectionId = connectionId,
            StoreId = string.IsNullOrWhiteSpace(storeId) ? null : storeId,
            CustomerChannelIdentityId = customerChannelIdentityId,
            Channel = channel,
            Status = ConversationStatus.New,
            AutomationMode = AutomationMode.Automated
        };
    }

    /// <summary>
    /// ADR-016: Resolved/Closed reopen to New; Spam stays Spam and stays silent (no unread increment).
    /// Timestamps take the maximum so out-of-order delivery is safe.
    /// </summary>
    public void RecordInboundMessage(DateTimeOffset occurredAt)
    {
        if (Status is ConversationStatus.Resolved or ConversationStatus.Closed)
        {
            Status = ConversationStatus.New;
        }

        if (Status != ConversationStatus.Spam)
        {
            UnreadCount++;
        }

        LastMessageAt = Max(LastMessageAt, occurredAt);
        LastCustomerMessageAt = Max(LastCustomerMessageAt, occurredAt);
    }

    /// <summary>Seen-receipt watermark: the latest moment the customer is known to have read the thread.</summary>
    public void RecordCustomerRead(DateTimeOffset occurredAt) =>
        CustomerLastReadAt = Max(CustomerLastReadAt, occurredAt);

    public void MarkReadByStaff() => UnreadCount = 0;

    /// <summary>Business-side message (staff, automation, or provider echo): moves only the last-activity time.</summary>
    public void RecordOutboundMessage(DateTimeOffset occurredAt) => LastMessageAt = Max(LastMessageAt, occurredAt);

    /// <summary>
    /// ADR-017: a human owns the conversation; automation may not send. Active threads move to HumanAssigned;
    /// Resolved/Closed/Spam keep their disposition. Returns false when already taken over (idempotent).
    /// </summary>
    public bool TakeOver()
    {
        if (AutomationMode == AutomationMode.HumanTakeover)
        {
            return false;
        }

        AutomationMode = AutomationMode.HumanTakeover;
        if (!IsDisposition(Status))
        {
            Status = ConversationStatus.HumanAssigned;
        }

        return true;
    }

    /// <summary>
    /// The assistant hands the conversation to a person (M09-S05 EscalateToHuman): takeover plus the reason category.
    /// Returns false when a human already owns the conversation (idempotent; the first reason is kept).
    /// </summary>
    public bool Escalate(string category, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (!TakeOver()) return false;
        EscalationCategory = category.Length > EscalationCategoryMaxLength ? category[..EscalationCategoryMaxLength] : category;
        EscalatedAt = now;
        return true;
    }

    public const int EscalationCategoryMaxLength = 48;

    /// <summary>ADR-017: explicit hand-back to automation. Returns false when already automated (idempotent).</summary>
    public bool Release(DateTimeOffset? now = null)
    {
        if (AutomationMode == AutomationMode.Automated)
        {
            return false;
        }

        AutomationMode = AutomationMode.Automated;
        AutomationResumedAt = now ?? DateTimeOffset.UtcNow;
        EscalationCategory = null; // the hand-back ends the escalation; its record stays in the audit log
        EscalatedAt = null;
        if (!IsDisposition(Status))
        {
            Status = ConversationStatus.BotActive;
        }

        return true;
    }

    public void Assign(string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        AssignedUserId = userId;
        AssignedAt = now;
    }

    public void Unassign()
    {
        AssignedUserId = null;
        AssignedAt = null;
    }

    /// <summary>
    /// ADR-017 staff status actions. Returns false for a no-op (already in the target state) and throws
    /// <see cref="InvalidOperationException"/> for a transition the state machine forbids.
    /// </summary>
    public bool ApplyStatusAction(ConversationStatusAction action)
    {
        var target = action switch
        {
            ConversationStatusAction.Resolve when Status == ConversationStatus.Spam => throw Invalid(action),
            ConversationStatusAction.Resolve => ConversationStatus.Resolved,
            ConversationStatusAction.Close when Status == ConversationStatus.Spam => throw Invalid(action),
            ConversationStatusAction.Close => ConversationStatus.Closed,
            ConversationStatusAction.Reopen when Status is ConversationStatus.Resolved or ConversationStatus.Closed => ConversationStatus.New,
            ConversationStatusAction.Reopen => throw Invalid(action),
            ConversationStatusAction.MarkSpam => ConversationStatus.Spam,
            ConversationStatusAction.UnmarkSpam when Status == ConversationStatus.Spam => ConversationStatus.New,
            ConversationStatusAction.UnmarkSpam => throw Invalid(action),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        if (target == Status)
        {
            return false;
        }

        Status = target;
        return true;
    }

    private InvalidOperationException Invalid(ConversationStatusAction action) =>
        new($"Cannot {action} a conversation in status {Status}.");

    private static bool IsDisposition(ConversationStatus status) =>
        status is ConversationStatus.Resolved or ConversationStatus.Closed or ConversationStatus.Spam;

    private static DateTimeOffset Max(DateTimeOffset? current, DateTimeOffset candidate) =>
        current.HasValue && current.Value >= candidate ? current.Value : candidate;
}
