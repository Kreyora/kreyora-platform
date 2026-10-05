using Kreyora.Domain.Common;

namespace Kreyora.Domain.Conversations;

/// <summary>
/// The current reaction of one reactor on one provider message. Keyed by the provider message ID with no
/// foreign key, because reactions may arrive before (or without) the message they reference.
/// Last write wins by provider time, so late-arriving older events never override newer ones.
/// </summary>
public sealed class MessageReaction : BaseEntity, ITenantOwned
{
    public const int ReactorChannelIdMaxLength = 128;
    public const int EmojiMaxLength = 32;

    private MessageReaction() { }

    public string TenantId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public string ProviderMessageId { get; private set; } = string.Empty;
    public string ReactorChannelId { get; private set; } = string.Empty;
    public string Emoji { get; private set; } = string.Empty;
    public bool IsRemoved { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static MessageReaction Create(
        string tenantId,
        string connectionId,
        string providerMessageId,
        string reactorChannelId,
        string emoji,
        bool isRemoved,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reactorChannelId);
        if (providerMessageId.Length > Message.ProviderMessageIdMaxLength || reactorChannelId.Length > ReactorChannelIdMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(providerMessageId));
        }

        return new MessageReaction
        {
            TenantId = tenantId,
            ConnectionId = connectionId,
            ProviderMessageId = providerMessageId,
            ReactorChannelId = reactorChannelId,
            Emoji = Bound(emoji),
            IsRemoved = isRemoved,
            OccurredAt = occurredAt
        };
    }

    /// <summary>Applies a reaction event if it is not older than the current state.</summary>
    public bool Apply(string emoji, bool isRemoved, DateTimeOffset occurredAt)
    {
        if (occurredAt < OccurredAt)
        {
            return false;
        }

        // A removal may omit the emoji; keep the last known one for context.
        if (!string.IsNullOrEmpty(emoji))
        {
            Emoji = Bound(emoji);
        }

        IsRemoved = isRemoved;
        OccurredAt = occurredAt;
        return true;
    }

    private static string Bound(string? emoji)
    {
        var value = emoji ?? string.Empty;
        return value.Length > EmojiMaxLength ? value[..EmojiMaxLength] : value;
    }
}
