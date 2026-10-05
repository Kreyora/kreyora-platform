namespace Kreyora.Application.Integrations;

/// <summary>
/// Requests prompt background processing after a commit (M08-S06). Best effort: if scheduling fails or no
/// job server is configured, the recurring sweepers still pick the work up, so nothing is lost.
/// </summary>
public interface IIntegrationWorkScheduler
{
    void ScheduleWebhookProcessing(string webhookEventId);

    void ScheduleOutboundDelivery(string outboundMessageId);
}
