using Hangfire;
using Kreyora.Application.Integrations;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Integrations;

public sealed partial class WebhookProcessingJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookProcessingJob> logger)
{
    private const int DefaultBatchSize = 50;

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed {Count} webhook events for tenant {TenantId}")]
    private static partial void LogProcessed(ILogger logger, int count, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing webhook processing for EventId={EventId} on tenant {TenantId}")]
    private static partial void LogProcessingError(ILogger logger, Exception ex, string eventId, string tenantId);

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
            await runner.RunAsync(new TenantJobEnvelope(tenantId, "webhook-processing", "{}"), async cancellationToken =>
            {
                await ProcessTenantWebhooksAsync(services, tenantId, cancellationToken);
            });
        }
    }

    public async Task<int> ProcessTenantWebhooksAsync(
        IServiceProvider services,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenantContext = services.GetRequiredService<ITenantContextAccessor>();
        using var tenantScope = tenantContext.BeginScope(new TenantContext(tenantId, null, null, null));

        var dbContext = services.GetRequiredService<AppDbContext>();
        var processingService = services.GetRequiredService<IWebhookProcessingService>();
        var clock = services.GetRequiredService<ITimeProvider>();
        var now = clock.UtcNow;

        var candidateEventIds = await dbContext.WebhookEvents
            .Where(e => e.TenantId == tenantId &&
                        (e.ProcessingStatus == WebhookProcessingStatus.Received ||
                         (e.ProcessingStatus == WebhookProcessingStatus.Failed && e.NextRetryAt <= now)))
            .OrderBy(e => e.ReceivedAt)
            .Take(DefaultBatchSize)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var processedCount = 0;

        foreach (var eventId in candidateEventIds)
        {
            try
            {
                var result = await processingService.ProcessWebhookEventAsync(eventId, cancellationToken);
                if (result.Succeeded)
                {
                    processedCount++;
                }
            }
            catch (Exception ex)
            {
                LogProcessingError(logger, ex, eventId, tenantId);
            }
        }

        if (processedCount > 0)
        {
            LogProcessed(logger, processedCount, tenantId);
        }

        return processedCount;
    }
}
