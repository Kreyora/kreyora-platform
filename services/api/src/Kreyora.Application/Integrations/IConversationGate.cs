using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

/// <summary>
/// Decides whether an outbound message may be enqueued or sent (ADR-017). Checked at enqueue and again
/// immediately before delivery, so a takeover or a closed provider window stops messages already queued.
/// </summary>
public interface IConversationGate
{
    Task<ConversationGateResult> CheckSendPermissionAsync(
        string tenantId,
        string connectionId,
        string? conversationId,
        OutboundMessageOrigin origin,
        CancellationToken cancellationToken = default);
}

/// <param name="ReasonCode">Stable machine-readable denial code (see <c>ConversationDenialReasons</c>).</param>
/// <param name="MessagingTag">Provider message tag required for this send (e.g. HUMAN_AGENT), when allowed with one.</param>
public sealed record ConversationGateResult(
    bool Allowed,
    string? DenialReason = null,
    string? ReasonCode = null,
    string? MessagingTag = null)
{
    /// <summary>Outbound request metadata key carrying <see cref="MessagingTag"/> to the provider.</summary>
    public const string MessagingTagMetadataKey = "messaging_tag";

    public static ConversationGateResult Allow(string? messagingTag = null) => new(true, MessagingTag: messagingTag);
    public static ConversationGateResult Deny(string reason, string? reasonCode = null) => new(false, reason, reasonCode);
}

/// <summary>Stable denial codes for conversation sends; surfaced as RFC 7807 problem types.</summary>
public static class ConversationDenialReasons
{
    public const string ConversationNotFound = "conversation_not_found";
    public const string ConversationIsSpam = "conversation_is_spam";
    public const string AutomationPausedByTakeover = "automation_paused_by_takeover";
    public const string ConnectionInactive = "connection_inactive";
    public const string CapabilityUnsupported = "capability_unsupported";
    public const string TextTooLong = "text_too_long";
    public const string TextRequired = "text_required";
    public const string WindowClosed = "window_closed";
    public const string WindowClosedHumanAgentUnavailable = "window_closed_human_agent_unavailable";
    public const string DeliveryUnconfirmed = "delivery_unconfirmed";
    public const string InvalidTransition = "invalid_transition";
    public const string ConversationChanged = "conversation_changed";
    public const string AssigneeNotMember = "assignee_not_member";

    public static string ProblemType(string reasonCode) => $"urn:kreyora:problem:{reasonCode}";
}
