using System.Text.Json;
using Kreyora.Application.Ai;
using Kreyora.Application.Models;

namespace Kreyora.Application.Assistant;

// ---- Tool registry (M09-S04) ----------------------------------------------------------------------------------

/// <summary>
/// Context every tool runs in, built by the server from trusted state (tenant scope, conversation, policy) and never
/// from model output. The model's arguments are lookup keys only.
/// </summary>
public sealed record AssistantToolContext(
    string TenantId,
    string StoreId,
    string? ConversationId,
    string? CustomerChannelIdentityId,
    string? CustomerId,
    IReadOnlyList<string> AllowedTools,
    bool IsSellerPreview,
    bool IsAutomationActive = true,
    string? TurnId = null);

/// <summary>Builds <see cref="AssistantToolContext"/> for the current tenant.</summary>
public interface IAssistantToolContextFactory
{
    /// <summary>Context for a customer conversation; the tools enabled in the shop's policy are allowed.</summary>
    /// <param name="turnId">The orchestration turn (S06); write calls in the same turn with the same arguments replay one result.</param>
    Task<Result<AssistantToolContext>> ForConversationAsync(string conversationId, string? turnId = null, CancellationToken cancellationToken = default);

    /// <summary>Seller preview: shop context without a customer; every registered read tool may be previewed.</summary>
    Task<Result<AssistantToolContext>> ForSellerPreviewAsync(CancellationToken cancellationToken = default);
}

/// <summary>One read tool. Implementations call application queries only and return minimized data.</summary>
public interface IAssistantTool
{
    string Name { get; }

    int Version { get; }

    string Description { get; }

    /// <summary>Strict JSON schema (the subset the registry validates); unknown fields are rejected.</summary>
    string ParametersSchema { get; }

    /// <summary>Runs in its own DI scope with the context's tenant; <paramref name="arguments"/> are already validated.</summary>
    Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>A tool's answer: data (serialized for the model) or a stable error code. Never an exception.</summary>
public sealed record AssistantToolResult
{
    public bool IsSuccess { get; private init; }

    public object? Data { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    /// <summary>How many records the answer describes (trace only).</summary>
    public int ResultCount { get; private init; }

    public static AssistantToolResult Success(object data, int resultCount) =>
        new() { IsSuccess = true, Data = data, ResultCount = resultCount };

    public static AssistantToolResult Failed(string code, string message, object? data = null) =>
        new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message, Data = data };
}

/// <summary>Stable tool error codes the model (and S06 orchestration) can act on.</summary>
public static class AssistantToolErrorCodes
{
    public const string ToolNotAllowed = "tool_not_allowed";
    public const string InvalidArguments = "invalid_arguments";
    public const string Timeout = "timeout";
    public const string Unavailable = "unavailable";
    public const string NotFound = "not_found";
    public const string VerificationRequired = "verification_required";
    public const string NotVerified = "not_verified";
    public const string Locked = "locked";
    public const string PlaceUnknown = "place_unknown";
    public const string PlaceNotServed = "place_not_served";
    public const string NeedsMoreDetail = "needs_more_detail";
    public const string StoreUnavailable = "store_unavailable";
    public const string ConfirmationRequired = "confirmation_required";
    public const string AutomationPaused = "automation_paused";
    public const string ConversationRequired = "conversation_required";
    public const string Stale = "stale";
    public const string LimitReached = "limit_reached";
}

/// <summary>
/// Per-call facts the registry gives a write tool through its DI scope (M09-S05): the idempotency key, the action ID
/// the completed call will be stored under, and an optional server-only reference to keep with it.
/// </summary>
public sealed class AssistantCallContext
{
    public string ActionId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? InternalReference { get; set; }

    public DateTimeOffset? ReferenceExpiresAt { get; set; }
}

/// <summary>
/// Values-free record of one tool execution (ADR-018 data handling): argument field names and a hash, never values,
/// customer text or PII. Logged now; persisted with the S06 action log.
/// </summary>
public sealed record AssistantToolTrace(
    string RegistryVersion,
    string Tool,
    int ToolVersion,
    string CallId,
    string? ConversationId,
    bool SellerPreview,
    DateTimeOffset StartedAt,
    long DurationMs,
    string Outcome,
    IReadOnlyList<string> ArgumentFields,
    string ArgumentsHash,
    int ResultCount,
    bool Replayed = false,
    bool DryRun = false);

/// <summary>What the registry hands back: the JSON the model sees, plus the trace.</summary>
public sealed record AssistantToolOutcome(string ResultJson, AssistantToolTrace Trace)
{
    public bool IsSuccess => Trace.Outcome == "ok";
}

/// <summary>Versioned, allowlisted registry of read tools (M09-S04).</summary>
public interface IAssistantToolRegistry
{
    string Version { get; }

    IReadOnlyList<AssistantToolDescriptor> Describe(IReadOnlyList<string> enabledTools);

    /// <summary>Definitions for the model: registered read tools that the context allows.</summary>
    IReadOnlyList<AiToolDefinition> GetDefinitions(AssistantToolContext context);

    Task<AssistantToolOutcome> ExecuteAsync(AssistantToolContext context, AiToolCall toolCall, CancellationToken cancellationToken = default);
}

public sealed record AssistantToolDescriptor(string Name, int Version, string Description, JsonElement ParametersSchema, bool EnabledInPolicy);

public sealed record AssistantToolCatalog(string RegistryVersion, IReadOnlyList<AssistantToolDescriptor> Tools);

public sealed record AssistantToolPreviewRequest(JsonElement? Arguments);

public sealed record AssistantToolPreviewResult(JsonElement Result, AssistantToolTrace Trace);

/// <summary>Owner tooling: the registry and a seller preview of one tool.</summary>
public interface IAssistantToolConsoleService
{
    Task<AssistantToolCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<Result<AssistantToolPreviewResult>> PreviewAsync(string toolName, AssistantToolPreviewRequest request, CancellationToken cancellationToken = default);
}

// ---- Product references the customer sends (M1, Q6-A) ----------------------------------------------------------

/// <summary>A product the customer linked to, matched exactly against the shop's own storefront.</summary>
public sealed record ProductReference(string ProductId, string Title, string ProductSlug, string Source);

/// <summary>
/// Finds links to this shop's own storefront products in customer text. Deterministic, never fetches a URL, never
/// matches another shop. Run by orchestration on inbound messages (S07), not offered to the model.
/// </summary>
public interface IProductReferenceResolver
{
    Task<IReadOnlyList<ProductReference>> ResolveAsync(string? text, CancellationToken cancellationToken = default);
}
