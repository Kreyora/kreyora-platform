using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

/// <summary>
/// Internal outbox entry point for application services that have already authorized the caller
/// (e.g. conversation replies under <c>conversations.write</c>). Adds the outbound message to the current
/// unit of work without saving, so the caller commits it together with its own changes. Applies the
/// connection, capability, gate and idempotency rules of the M07 outbox.
/// </summary>
public interface IOutboundEnqueuer
{
    Task<OutboundEnqueueResult> EnqueueAsync(
        OutboundEnqueueRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OutboundEnqueueRequest(
    string TenantId,
    string ConnectionId,
    string ConversationId,
    string RecipientChannelId,
    string IdempotencyKey,
    string Text,
    OutboundMessageOrigin Origin,
    string? ActorUserId);

/// <param name="ExistingOutboundMessageId">Set when the idempotency key was already used (no new message added).</param>
public sealed record OutboundEnqueueResult(
    bool Allowed,
    OutboundMessage? Message,
    string? ExistingOutboundMessageId,
    string? DenialReason,
    string? ReasonCode)
{
    public static OutboundEnqueueResult Added(OutboundMessage message) => new(true, message, null, null, null);
    public static OutboundEnqueueResult Duplicate(string existingId) => new(true, null, existingId, null, null);
    public static OutboundEnqueueResult Denied(string reason, string? code) => new(false, null, null, reason, code);
}

/// <summary>
/// Keeps the conversation timeline in step with outbox delivery (implemented by the Conversations module).
/// Called inside the delivery unit of work; adds tracked changes only.
/// </summary>
public interface IConversationOutboundReconciler
{
    Task OnDeliverySucceededAsync(OutboundMessage message, string providerMessageId, DateTimeOffset sentAt, CancellationToken cancellationToken = default);

    Task OnDeliveryFailedPermanentlyAsync(OutboundMessage message, CancellationToken cancellationToken = default);
}
