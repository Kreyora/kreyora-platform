namespace Kreyora.Application.Integrations;

public interface IWebhookIngressService
{
    Task<WebhookIngressResult> HandleWebhookAsync(
        WebhookIngressCommand command,
        CancellationToken cancellationToken = default);

    Task<WebhookChallengeResult> HandleChallengeAsync(
        WebhookChallengeCommand command,
        CancellationToken cancellationToken = default);
}

