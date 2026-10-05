using Kreyora.Domain.Common;

namespace Kreyora.Domain.Conversations;

/// <summary>A label on a conversation. Stored and returned in M08-S04; edit operations arrive in M08-S05.</summary>
public sealed class ConversationLabel : BaseEntity, ITenantOwned
{
    public const int LabelMaxLength = 48;

    private ConversationLabel() { }

    public string TenantId { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;

    public static ConversationLabel Create(string tenantId, string conversationId, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var trimmed = label.Trim();
        if (trimmed.Length > LabelMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(label));
        }

        return new ConversationLabel { TenantId = tenantId, ConversationId = conversationId, Label = trimmed };
    }
}
