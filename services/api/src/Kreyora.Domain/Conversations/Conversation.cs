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

    private static DateTimeOffset Max(DateTimeOffset? current, DateTimeOffset candidate) =>
        current.HasValue && current.Value >= candidate ? current.Value : candidate;
}
