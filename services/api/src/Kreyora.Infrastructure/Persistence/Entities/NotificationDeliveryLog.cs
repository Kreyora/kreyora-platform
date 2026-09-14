using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Common;
using Kreyora.Domain.Notifications;

namespace Kreyora.Infrastructure.Persistence.Entities;

public sealed class NotificationDeliveryLog : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public required string TenantId { get; set; }
    public required string NotificationRequestId { get; set; }
    public required NotificationChannel Channel { get; set; }
    public required string RecipientRedacted { get; set; }
    public required string SubjectRendered { get; set; }
    public required string BodyRendered { get; set; }
    public DateTimeOffset RenderedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string ProviderName { get; set; }
}

