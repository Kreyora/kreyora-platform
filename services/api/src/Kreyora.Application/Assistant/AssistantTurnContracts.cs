using Kreyora.Application.Audit;
using Kreyora.Application.Models;
using Kreyora.Domain.Assistant;

namespace Kreyora.Application.Assistant;

// ---- Assistant turns (M09-S06, ADR-021) -----------------------------------------------------------------------

/// <summary>Stable reason codes for turn outcomes (logged; shown to owners; never contain content).</summary>
public static class AssistantTurnReasons
{
    public const string Replied = "replied";
    public const string ConversationNotFound = "conversation_not_found";
    public const string InvalidTrigger = "invalid_trigger";
    public const string DuplicateTrigger = "duplicate_trigger";
    public const string ConversationBusy = "conversation_busy";
    public const string TenantBusy = "tenant_busy";
    public const string PlatformDisabled = "platform_disabled";
    public const string AssistantInactive = "assistant_inactive";
    public const string AutomationPaused = "automation_paused";
    public const string OutsideHours = "outside_hours";
    public const string ReplyRateLimit = "reply_rate_limit";
    public const string TenantDailyLimit = "tenant_daily_limit";
    public const string PlatformDailyLimit = "platform_daily_limit";
    public const string CircuitOpen = "circuit_open";
    public const string DataPolicy = "data_policy";
    public const string KeywordEscalation = "keyword_escalation";
    public const string PersonRequested = "person_requested";
    public const string UnrecognizedMedia = "unrecognized_media";
    public const string ModelEscalation = "model_escalation";
    public const string ProviderFailure = "provider_failure";
    public const string LoopLimit = "loop_limit";
    public const string ToolLimit = "tool_limit";
    public const string TokenBudget = "token_budget";
    public const string TurnDeadline = "turn_deadline";
    public const string ValidationFailed = "validation_failed";
    public const string Truncated = "truncated";
    public const string Superseded = "superseded";
    public const string TakenOverDuringTurn = "taken_over_during_turn";
    public const string FallbackCooldown = "fallback_cooldown";
    public const string EnqueueDenied = "enqueue_denied";
    public const string UnexpectedError = "unexpected_error";
    public const string Playground = "playground";
    public const string ConnectionUnavailable = "connection_unavailable";
    public const string NotEntitled = "not_entitled";
    public const string CustomerSafety = "customer_safety";
    public const string ReceivedBeforeRelease = "received_before_release";
}

public sealed record AssistantTurnResult(string? TurnId, AssistantTurnOutcome Outcome, string ReasonCode, string? OutboundMessageId, bool Replayed);

/// <param name="From"><c>customer</c> or <c>shop</c>.</param>
public sealed record AssistantPlaygroundMessage(string From, string Text);

public sealed record AssistantPlaygroundRequest(IReadOnlyList<AssistantPlaygroundMessage> Messages);

public sealed record AssistantPlaygroundToolUse(string Tool, string Outcome, bool DryRun);

public sealed record AssistantPlaygroundResult(
    string TurnId,
    AssistantTurnOutcome Outcome,
    string ReasonCode,
    string? Reply,
    IReadOnlyList<AssistantPlaygroundToolUse> Tools,
    IReadOnlyList<TurnCitation> Citations,
    int ModelCalls,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd,
    IReadOnlyList<string> ValidationCodes);

/// <summary>
/// Runs one bounded assistant turn (M09-S06). <see cref="RunAsync"/> is called for a customer message (S07 wires it to
/// inbound events); it never throws for model, tool or budget problems and always leaves a turn log row when it ran.
/// </summary>
public interface IAssistantTurnService
{
    Task<AssistantTurnResult> RunAsync(string conversationId, string triggerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Owner playground: made-up messages, seller-preview tools (dry run), nothing sent.</summary>
    Task<Result<AssistantPlaygroundResult>> PlaygroundAsync(AssistantPlaygroundRequest request, CancellationToken cancellationToken = default);
}

public sealed record AssistantTurnItem(
    string Id,
    string? ConversationId,
    bool IsPlayground,
    AssistantTurnOutcome Outcome,
    string ReasonCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? PolicyVersion,
    string? PromptVersion,
    string? RegistryVersion,
    IReadOnlyList<TurnModelCall> ModelCalls,
    IReadOnlyList<TurnToolStep> ToolSteps,
    IReadOnlyList<TurnCitation> Citations,
    IReadOnlyList<string> ValidationCodes,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd,
    string? OutboundMessageId);

/// <summary>Owner/admin read of the redacted turn log (newest first).</summary>
public interface IAssistantTurnLogQuery
{
    Task<CursorPage<AssistantTurnItem>> ListAsync(string? conversationId, string? cursor, int pageSize, CancellationToken cancellationToken = default);
}
