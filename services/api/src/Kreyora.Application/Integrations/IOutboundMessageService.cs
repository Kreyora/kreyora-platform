using Kreyora.Application.Models;
using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public interface IOutboundMessageService
{
    Task<OutboundMessageResult> QueueMessageAsync(
        QueueOutboundMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<OutboundMessageDto?> GetMessageAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboundDeliveryAttemptDto>> GetDeliveryAttemptsAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OutboundMessageDto>> GetMessagesAsync(
        OutboundMessageQuery query,
        CancellationToken cancellationToken = default);

    Task<OutboundMessageResult> CancelMessageAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    Task<OutboundMessageResult> ReplayMessageAsync(
        string messageId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task ProcessDeliveryAsync(
        string outboundMessageId,
        CancellationToken cancellationToken = default);

    Task ProcessStatusReceiptAsync(
        string connectionId,
        string providerMessageId,
        MessageDeliveryStatus status,
        DateTimeOffset? timestamp,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OutboundDeadLetterDto>> GetDeadLetterMessagesAsync(
        OutboundDeadLetterQuery query,
        CancellationToken cancellationToken = default);
}

