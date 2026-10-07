namespace Kreyora.Application.Assistant;

// ---- Conversation integration (M09-S07, ADR-022) ---------------------------------------------------------------

/// <summary>
/// Collects what inbound processing created (customer messages, Instagram-app echoes) and schedules the follow-up work
/// only after the webhook transaction committed (<see cref="Flush"/>), or drops it when the transaction failed
/// (<see cref="Discard"/>). Scoped to one processing run; never throws into it.
/// </summary>
public interface IAssistantInboundHook
{
    void CustomerMessageReceived(string tenantId, string conversationId, string messageId);

    void NativeReplyReceived(string tenantId, string messageId);

    void Flush();

    void Discard();
}

/// <summary>Background scheduling for assistant turns and native-reply checks (Hangfire; a no-op without it).</summary>
public interface IAssistantTurnScheduler
{
    void ScheduleTurn(string tenantId, string conversationId, string messageId, TimeSpan delay, int attempt = 0);

    void ScheduleNativeReplyCheck(string tenantId, string messageId, TimeSpan delay);
}

/// <summary>Whether a workspace may use the assistant (operator allowlist until M10 plan entitlements).</summary>
public interface IAssistantEntitlementQuery
{
    bool IsEntitled(string tenantId);
}

/// <summary>
/// Decides whether an Instagram-app echo was the seller replying from the provider's app (M09-S07 Q6). If it still is
/// unmatched to any Kreyora send after the check delay, the conversation is taken over (audited, automation suppressed).
/// </summary>
public interface INativeReplyTakeoverService
{
    /// <returns>True when this check took the conversation over.</returns>
    Task<bool> CheckAsync(string messageId, CancellationToken cancellationToken = default);
}
