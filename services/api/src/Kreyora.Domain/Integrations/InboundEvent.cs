using System.Security.Cryptography;
using System.Text;
using Kreyora.Domain.Common;
using Kreyora.Domain.Tenancy;

namespace Kreyora.Domain.Integrations;

public sealed class InboundEvent : BaseEntity, ITenantOwned
{
    public const int ProviderMessageIdMaxLength = 512;
    public const int DeduplicationKeyLength = 64;
    public const int SchemaVersionMaxLength = 16;
    public const int EventTypeMaxLength = 64;

    public string TenantId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public string WebhookEventId { get; private set; } = string.Empty;
    public ChannelType Channel { get; private set; }
    public string? ProviderMessageId { get; private set; }

    /// <summary>
    /// Lowercase hex SHA-256 of the event identity; unique per connection. Distinguishes events that
    /// reference the same provider message (e.g. a read receipt and a reaction on one message).
    /// </summary>
    public string DeduplicationKey { get; private set; } = string.Empty;
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
        DateTimeOffset occurredAt,
        string? deduplicationKey = null)
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

        if (deduplicationKey != null && deduplicationKey.Length != DeduplicationKeyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(deduplicationKey), $"DeduplicationKey must be a {DeduplicationKeyLength}-character hash.");
        }

        var normalizedProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId.Trim();
        var now = DateTimeOffset.UtcNow;
        var inbound = new InboundEvent
        {
            TenantId = tenantId,
            ConnectionId = connectionId,
            WebhookEventId = webhookEventId,
            Channel = channel,
            ProviderMessageId = normalizedProviderMessageId,
            SchemaVersion = schemaVersion,
            EventType = eventType,
            PayloadJson = payloadJson,
            OccurredAt = occurredAt,
            CreatedAt = now,
            ModifiedAt = now
        };

        // Legacy identity (M07): the provider message ID, or no dedup at all (unique row ID) when absent.
        inbound.DeduplicationKey = deduplicationKey ?? HashIdentity(normalizedProviderMessageId ?? inbound.Id);
        return inbound;
    }

    /// <summary>Lowercase hex SHA-256 of an event identity string (UTF-8).</summary>
    public static string HashIdentity(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}

