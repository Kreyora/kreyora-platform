using System.Text.Json;
using Hangfire;
using Kreyora.Application.Notifications;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Notifications;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Notifications;

public sealed partial class NotificationDeliveryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDeliveryJob> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Delivered {Count} notifications for tenant {TenantId}")]
    private static partial void LogDelivered(ILogger logger, int count, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing notification delivery cycle for NotificationId={NotificationId} on tenant {TenantId}")]
    private static partial void LogDeliveryError(ILogger logger, Exception ex, string notificationId, string tenantId);

    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<AppDbContext>();
        var tenantIds = await dbContext.Tenants.AsNoTracking()
            .Where(tenant => tenant.Status == TenantStatus.Active)
            .Select(tenant => tenant.Id)
            .ToListAsync();
        var runner = services.GetRequiredService<ITenantJobRunner>();

        foreach (var tenantId in tenantIds)
        {
            await runner.RunAsync(new TenantJobEnvelope(tenantId, "notification-delivery", "{}"), async cancellationToken =>
            {
                await DeliverTenantAsync(services, tenantId, cancellationToken);
            });
        }
    }

    public async Task<int> DeliverTenantAsync(IServiceProvider services, string tenantId, CancellationToken cancellationToken = default)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        var templateRegistry = services.GetRequiredService<INotificationTemplateRegistry>();
        var deliveryProvider = services.GetRequiredService<INotificationDeliveryProvider>();
        var clock = services.GetRequiredService<ITimeProvider>();
        var options = services.GetRequiredService<IOptions<NotificationOptions>>().Value;

        var retryPolicy = new NotificationRetryPolicy(
            options.MaxRetryAttempts,
            options.RetryBackoffMinutes.Select(m => TimeSpan.FromMinutes(m)).ToArray());

        var now = clock.UtcNow;

        var notifications = await dbContext.NotificationRequests
            .Include(n => n.DeliveryAttempts)
            .Where(n => n.TenantId == tenantId &&
                        (n.Status == NotificationStatus.Pending ||
                         (n.Status == NotificationStatus.Failed && n.NextRetryAt <= now)))
            .OrderBy(n => n.CreatedAt)
            .Take(options.DeliveryBatchSize)
            .ToListAsync(cancellationToken);

        var deliveredCount = 0;

        foreach (var notification in notifications)
        {
            if (notification.NextRetryAt.HasValue && notification.NextRetryAt.Value > now)
            {
                continue;
            }

            try
            {
                notification.MarkDelivering(now);
                await dbContext.SaveChangesAsync(cancellationToken);

                // Fetch context for template rendering
                var renderData = await BuildRenderDataAsync(dbContext, tenantId, notification, cancellationToken);

                var rendered = templateRegistry.Render(notification.TemplateCode, notification.TemplateVersion, renderData);

                var startedAt = clock.UtcNow;
                var deliveryRequest = new NotificationDeliveryRequest(
                    notification.Id,
                    tenantId,
                    notification.Channel,
                    notification.RecipientContact,
                    notification.RecipientName,
                    rendered.Subject,
                    rendered.Body,
                    renderData);

                var deliveryResult = await deliveryProvider.DeliverAsync(deliveryRequest, cancellationToken);
                var completedAt = clock.UtcNow;

                var attempt = NotificationDeliveryAttempt.Create(
                    tenantId,
                    notification.Id,
                    notification.AttemptCount,
                    deliveryProvider.ProviderName,
                    startedAt);

                if (deliveryResult.Succeeded)
                {
                    attempt.CompleteSuccess(deliveryResult.ProviderReference, completedAt);
                    notification.RecordSuccess(deliveryResult.ProviderReference, completedAt);
                }
                else
                {
                    var error = deliveryResult.RedactedError ?? "Notification delivery provider returned failure.";
                    attempt.CompleteFailure(error, completedAt);
                    notification.RecordFailure(error, completedAt, retryPolicy);
                }

                notification.DeliveryAttempts.Add(attempt);
                await dbContext.SaveChangesAsync(cancellationToken);
                deliveredCount++;
            }
            catch (Exception ex)
            {
                LogDeliveryError(logger, ex, notification.Id, tenantId);
                var completedAt = clock.UtcNow;
                var error = ex.Message[..Math.Min(ex.Message.Length, 500)];

                var attempt = NotificationDeliveryAttempt.Create(
                    tenantId,
                    notification.Id,
                    notification.AttemptCount,
                    deliveryProvider.ProviderName,
                    now);

                attempt.CompleteFailure(error, completedAt);
                notification.RecordFailure(error, completedAt, retryPolicy);
                notification.DeliveryAttempts.Add(attempt);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (deliveredCount > 0)
        {
            LogDelivered(logger, deliveredCount, tenantId);
        }

        return deliveredCount;
    }

    private static async Task<Dictionary<string, string>> BuildRenderDataAsync(
        AppDbContext dbContext,
        string tenantId,
        NotificationRequest notification,
        CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customerName"] = notification.RecipientName ?? "Customer"
        };

        var outboxMessage = await dbContext.OutboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == notification.SourceEventId, cancellationToken);

        if (outboxMessage is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(outboxMessage.Content);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    data[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => prop.Value.GetRawText()
                    };
                }
            }
            catch
            {
                // Fallback if parsing fails
            }
        }

        return data;
    }
}

