using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public interface IChannelProvider
{
    ChannelType Channel { get; }
    ChannelCapabilities Capabilities { get; }

    Task<WebhookValidationResult> ValidateWebhookAsync(
        WebhookValidationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NormalizedInboundEnvelope>> NormalizeInboundAsync(
        RawWebhookPayload rawPayload,
        CancellationToken cancellationToken = default);

    Task<OutboundDeliveryResult> SendMessageAsync(
        ChannelConnectionSnapshot connection,
        OutboundMessageRequest message,
        CancellationToken cancellationToken = default);

    Task<ConnectionHealthResult> ValidateOrRefreshConnectionAsync(
        ChannelConnectionSnapshot connection,
        CancellationToken cancellationToken = default);
}

