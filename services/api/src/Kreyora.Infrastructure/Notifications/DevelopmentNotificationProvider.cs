using Kreyora.Application.Notifications;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Notifications;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Notifications;

public sealed partial class DevelopmentNotificationProvider(
    AppDbContext dbContext,
    ITimeProvider clock,
    ILogger<DevelopmentNotificationProvider> logger) : INotificationDeliveryProvider
{
    public const string Name = "Development";

    public string ProviderName => Name;

    [LoggerMessage(Level = LogLevel.Information, Message = "Development notification delivered: NotificationId={NotificationId}, Channel={Channel}, Recipient={RecipientRedacted}, Subject={Subject}")]
    private static partial void LogDelivered(ILogger logger, string notificationId, NotificationChannel channel, string recipientRedacted, string subject);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to record development notification delivery log for NotificationId={NotificationId}")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception ex, string notificationId);

    public async Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var redactedContact = PiiRedaction.RedactContact(request.RecipientContact, request.Channel);

            var log = new NotificationDeliveryLog
            {
                TenantId = request.TenantId,
                NotificationRequestId = request.NotificationId,
                Channel = request.Channel,
                RecipientRedacted = redactedContact,
                SubjectRendered = request.Subject,
                BodyRendered = request.Body,
                RenderedAt = clock.UtcNow,
                ProviderName = Name
            };

            dbContext.NotificationDeliveryLogs.Add(log);
            await dbContext.SaveChangesAsync(cancellationToken);

            LogDelivered(logger, request.NotificationId, request.Channel, redactedContact, request.Subject);

            return new NotificationDeliveryResult(true, $"dev-{log.Id}", null);
        }
        catch (Exception ex)
        {
            LogDeliveryFailed(logger, ex, request.NotificationId);
            return new NotificationDeliveryResult(false, null, "Failed to write development delivery log.");
        }
    }
}

