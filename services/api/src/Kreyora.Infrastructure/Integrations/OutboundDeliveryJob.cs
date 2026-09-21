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

public sealed partial class OutboundDeliveryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboundDeliveryJob> logger)
{
    private const int DefaultBatchSize = 50;

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed {Count} outbound messages for tenant {TenantId}")]
    private static partial void LogProcessed(ILogger logger, int count, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing outbound delivery for MessageId={MessageId} on tenant {TenantId}")]
    private static partial void LogDeliveryError(ILogger logger, Exception ex, string messageId, string tenantId);

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
            await runner.RunAsync(new TenantJobEnvelope(tenantId, "outbound-delivery", "{}"), async cancellationToken =>
            {
                await ProcessTenantOutboundAsync(services, tenantId, cancellationToken);
            });
        }
    }

    public async Task<int> ProcessTenantOutboundAsync(
        IServiceProvider services,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenantContext = services.GetRequiredService<ITenantContextAccessor>();
        using var tenantScope = tenantContext.BeginScope(new TenantContext(tenantId, null, null, null));

        var dbContext = services.GetRequiredService<AppDbContext>();
        var outboundService = services.GetRequiredService<IOutboundMessageService>();
        var clock = services.GetRequiredService<ITimeProvider>();
        var now = clock.UtcNow;

        var candidateIds = await dbContext.OutboundMessages
            .Where(m => m.TenantId == tenantId &&
                        (m.Status == OutboundMessageStatus.Queued ||
                         (m.Status == OutboundMessageStatus.Failed && m.NextRetryAt <= now)))
            .OrderBy(m => m.QueuedAt)
            .Take(DefaultBatchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var processedCount = 0;

        foreach (var messageId in candidateIds)
        {
            try
            {
                await outboundService.ProcessDeliveryAsync(messageId, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                LogDeliveryError(logger, ex, messageId, tenantId);
            }
        }

        if (processedCount > 0)
        {
            LogProcessed(logger, processedCount, tenantId);
        }

        return processedCount;
    }
}

