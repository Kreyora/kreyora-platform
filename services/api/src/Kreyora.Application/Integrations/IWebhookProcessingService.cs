using Kreyora.Application.Models;

namespace Kreyora.Application.Integrations;

public interface IWebhookProcessingService
{
    Task<WebhookProcessingResult> ProcessWebhookEventAsync(
        string webhookEventId,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<WebhookDeadLetterDto>>> GetDeadLetterEventsAsync(
        WebhookDeadLetterQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<WebhookReplayResult>> ReplayWebhookEventAsync(
        string webhookEventId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

