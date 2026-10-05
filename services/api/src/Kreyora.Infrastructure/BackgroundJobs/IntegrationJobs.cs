using Hangfire;
using Kreyora.Application.Integrations;
using Kreyora.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.BackgroundJobs;

/// <summary>
/// Recurring integration sweepers (retries, backoff, stale-Processing reclaim). These were registered in DI
/// since M07 but never scheduled; M08-S06 schedules them.
/// </summary>
public static class IntegrationJobRegistration
{
    public const string WebhookProcessingJobId = "webhook-processing";
    public const string OutboundDeliveryJobId = "outbound-delivery";

    public static void RegisterRecurring(IRecurringJobManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        manager.AddOrUpdate<WebhookProcessingJob>(WebhookProcessingJobId, job => job.RunAsync(), Cron.Minutely);
        manager.AddOrUpdate<OutboundDeliveryJob>(OutboundDeliveryJobId, job => job.RunAsync(), Cron.Minutely);
    }
}

/// <summary>
/// Enqueues an immediate Hangfire job for one webhook event or outbound message. A no-op when Hangfire is not
/// configured (no database connection, test hosts). Failures are logged, never thrown: the sweepers cover them.
/// </summary>
public sealed partial class HangfireIntegrationWorkScheduler(
    IServiceProvider serviceProvider,
    ILogger<HangfireIntegrationWorkScheduler> logger) : IIntegrationWorkScheduler
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not enqueue immediate {Work} for {Id}; the recurring sweeper will process it")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception ex, string work, string id);

    public void ScheduleWebhookProcessing(string webhookEventId) =>
        Enqueue("webhook processing", webhookEventId, client =>
            client.Enqueue<IWebhookProcessingService>(service => service.ProcessWebhookEventAsync(webhookEventId, CancellationToken.None)));

    public void ScheduleOutboundDelivery(string outboundMessageId) =>
        Enqueue("outbound delivery", outboundMessageId, client =>
            client.Enqueue<IOutboundMessageService>(service => service.ProcessDeliveryAsync(outboundMessageId, CancellationToken.None)));

    private void Enqueue(string work, string id, Action<IBackgroundJobClient> enqueue)
    {
        var client = serviceProvider.GetService<IBackgroundJobClient>();
        if (client is null)
        {
            return;
        }

        try
        {
            enqueue(client);
        }
        catch (Exception ex)
        {
            LogEnqueueFailed(logger, ex, work, id);
        }
    }
}
