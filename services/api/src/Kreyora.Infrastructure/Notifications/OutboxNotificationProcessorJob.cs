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

public sealed partial class OutboxNotificationProcessorJob(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxNotificationProcessorJob> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Processed {Count} outbox notification events for tenant {TenantId}")]
    private static partial void LogProcessed(ILogger logger, int count, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process outbox notification message {MessageId} for tenant {TenantId}")]
    private static partial void LogMessageFailed(ILogger logger, Exception ex, string messageId, string tenantId);

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
            await runner.RunAsync(new TenantJobEnvelope(tenantId, "outbox-notification-processor", "{}"), async cancellationToken =>
            {
                await ProcessTenantAsync(services, tenantId, cancellationToken);
            });
        }
    }

    public async Task<int> ProcessTenantAsync(IServiceProvider services, string tenantId, CancellationToken cancellationToken = default)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        var templateRegistry = services.GetRequiredService<INotificationTemplateRegistry>();
        var clock = services.GetRequiredService<ITimeProvider>();
        var options = services.GetRequiredService<IOptions<NotificationOptions>>().Value;

        var messages = await dbContext.OutboxMessages
            .Where(m => m.TenantId == tenantId && m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(options.ProcessorBatchSize)
            .ToListAsync(cancellationToken);

        var processedCount = 0;

        foreach (var message in messages)
        {
            try
            {
                var templates = templateRegistry.GetTemplatesForEvent(message.Type);
                if (templates.Count == 0)
                {
                    // No notification templates configured for this event type
                    message.ProcessedAt = clock.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    processedCount++;
                    continue;
                }

                // Parse orderId from outbox content
                string? orderId = null;
                try
                {
                    using var doc = JsonDocument.Parse(message.Content);
                    if (doc.RootElement.TryGetProperty("orderId", out var orderIdProp))
                    {
                        orderId = orderIdProp.GetString();
                    }
                }
                catch
                {
                    // If JSON parsing fails, mark processed with error
                    message.ProcessedAt = clock.UtcNow;
                    message.Error = "Invalid JSON in outbox message content.";
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(orderId))
                {
                    message.ProcessedAt = clock.UtcNow;
                    message.Error = "No orderId found in outbox message.";
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var order = await dbContext.Orders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, cancellationToken);

                if (order is null)
                {
                    message.ProcessedAt = clock.UtcNow;
                    message.Error = $"Referenced order '{orderId}' not found.";
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                foreach (var template in templates)
                {
                    var recipientContact = template.Channel switch
                    {
                        NotificationChannel.Email => order.CustomerEmail,
                        NotificationChannel.Sms => order.CustomerPhone,
                        _ => order.CustomerEmail
                    };

                    if (string.IsNullOrWhiteSpace(recipientContact))
                    {
                        continue;
                    }

                    var idempotencyKey = $"{message.Id}:{template.TemplateCode}:{(int)template.Channel}";

                    var alreadyExists = await dbContext.NotificationRequests
                        .AnyAsync(n => n.TenantId == tenantId && n.IdempotencyKey == idempotencyKey, cancellationToken);

                    if (!alreadyExists)
                    {
                        var notification = NotificationRequest.Create(
                            tenantId,
                            message.Type,
                            message.Id,
                            template.TemplateCode,
                            template.TemplateVersion,
                            template.Channel,
                            recipientContact,
                            order.CustomerName,
                            maxAttempts: options.MaxRetryAttempts,
                            idempotencyKey: idempotencyKey);

                        dbContext.NotificationRequests.Add(notification);
                    }
                }

                message.ProcessedAt = clock.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                LogMessageFailed(logger, ex, message.Id, tenantId);
                message.Error = ex.Message[..Math.Min(ex.Message.Length, 500)];
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (processedCount > 0)
        {
            LogProcessed(logger, processedCount, tenantId);
        }

        return processedCount;
    }
}

