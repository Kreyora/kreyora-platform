using System.Text.Json;
using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public interface IChannelProvider
{
    ChannelType Channel { get; }
    ChannelCapabilities Capabilities { get; }

    /// <summary>
    /// Extracts the provider-side account identifier (e.g. IGID) from a webhook body for
    /// connection resolution. Default returns null; providers with body-routed webhooks override.
    /// </summary>
    string? ResolveExternalAccountId(JsonDocument body) => null;

    /// <summary>
    /// True when webhook signatures are verified with a per-connection secret (default). Providers whose
    /// signatures use one app-level secret return false: ingress then verifies the signature before any
    /// connection lookup and never decrypts connection credentials on the webhook path (ADR-015).
    /// </summary>
    bool UsesConnectionSecretForSignature => true;

    /// <summary>HTTP status returned for an accepted delivery. Default 202; Meta documents 200 OK.</summary>
    int AcknowledgementStatusCode => 202;

    /// <summary>
    /// Splits a verified multi-account delivery into one payload per provider account (ADR-015).
    /// Default null: the provider does not split and the whole body maps to one connection.
    /// </summary>
    IReadOnlyList<WebhookAccountSlice>? SplitByAccount(JsonDocument body) => null;

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

