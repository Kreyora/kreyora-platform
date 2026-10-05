using Kreyora.Application.Conversations;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Domain.Conversations;

namespace Kreyora.Infrastructure.Conversations;

internal static class ConversationMapping
{
    public static MessageItem ToItem(Message m, IReadOnlyList<MessageReactionSummary> reactions, string? deliveryFailureCode = null) => new(
        m.Id,
        m.ConversationId,
        m.Direction,
        m.Origin,
        m.Kind,
        m.Text,
        m.MediaUrl,
        m.MediaContentType,
        m.DeliveryStatus,
        m.OccurredAt,
        m.RedactedAt.HasValue,
        reactions,
        m.IsPending,
        m.ActorUserId,
        deliveryFailureCode);

    /// <summary>RFC 7807 problem carrying a stable ADR-017 reason code.</summary>
    public static Result<T> Denied<T>(string reasonCode, string detail, int status = 422) =>
        Result<T>.Failure(new ErrorDetail
        {
            Type = ConversationDenialReasons.ProblemType(reasonCode),
            Title = reasonCode,
            Status = status,
            Detail = detail
        });
}
