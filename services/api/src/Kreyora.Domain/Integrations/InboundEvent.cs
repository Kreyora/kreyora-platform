using Kreyora.Domain.Common;
using Kreyora.Domain.Tenancy;

namespace Kreyora.Domain.Integrations;

public sealed class InboundEvent : BaseEntity, ITenantOwned
{
    public const int ProviderMessageIdMaxLength = 128;
    public const int SchemaVersionMaxLength = 16;
    public const int EventTypeMaxLength = 64;

    public string TenantId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public string WebhookEventId { get; private set; } = string.Empty;
    public ChannelType Channel { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string SchemaVersion { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }

    private InboundEvent() { }

    public static InboundEvent Create(
        string tenantId,
        string connectionId,
        string webhookEventId,
        ChannelType channel,
        string? providerMessageId,
        string schemaVersion,
        string eventType,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        if (providerMessageId != null && providerMessageId.Length > ProviderMessageIdMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(providerMessageId), $"ProviderMessageId cannot exceed {ProviderMessageIdMaxLength} characters.");
        }

        if (schemaVersion.Length > SchemaVersionMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), $"SchemaVersion cannot exceed {SchemaVersionMaxLength} characters.");
        }

        if (eventType.Length > EventTypeMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"EventType cannot exceed {EventTypeMaxLength} characters.");
        }

        var now = DateTimeOffset.UtcNow;
        return new InboundEvent
        {
            TenantId = tenantId,
            ConnectionId = connectionId,
            WebhookEventId = webhookEventId,
            Channel = channel,
            ProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId.Trim(),
            SchemaVersion = schemaVersion,
            EventType = eventType,
            PayloadJson = payloadJson,
            OccurredAt = occurredAt,
            CreatedAt = now,
            ModifiedAt = now
        };
    }
}

