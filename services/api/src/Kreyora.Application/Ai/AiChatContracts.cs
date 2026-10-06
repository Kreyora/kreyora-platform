namespace Kreyora.Application.Ai;

/// <summary>
/// Provider-neutral chat completion boundary for every AI feature (M09, ADR-018). Callers choose a model
/// profile, never a provider, URL or key. Failures are returned as values so orchestration can always fall back
/// safely; implementations must not throw for provider problems.
/// </summary>
public interface IAiChatClient
{
    Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Which configured model to use. Mapped to provider and model by configuration only.</summary>
public enum AiModelProfile
{
    Primary,
    Fallback
}

public enum AiChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public enum AiToolChoice
{
    Auto,
    None,
    Required
}

public enum AiFinishReason
{
    Stop,
    ToolCalls,
    Length,
    ContentFilter,
    Other
}

public enum AiFailureKind
{
    /// <summary>The global kill switch is off.</summary>
    Disabled,
    /// <summary>Live mode without a usable provider, key or model, or the provider rejected the key.</summary>
    NotConfigured,
    /// <summary>The request declared personal data but the data policy does not allow it for this provider.</summary>
    PolicyViolation,
    InvalidRequest,
    Timeout,
    RateLimited,
    ProviderUnavailable,
    InvalidResponse,
    ContentRefused
}

/// <summary>A tool the model may call. <see cref="ParametersJsonSchema"/> is a JSON Schema object.</summary>
public sealed record AiToolDefinition(string Name, string Description, string ParametersJsonSchema);

/// <summary>A tool call requested by the model. <see cref="ArgumentsJson"/> is untrusted model output.</summary>
/// <param name="ProviderData">
/// Opaque provider data that must be sent back unchanged with this tool call on the next turn (e.g. Gemini 3
/// "thought signatures", which Google requires for multi-turn tool use). Never inspected, logged or persisted.
/// </param>
public sealed record AiToolCall(string Id, string Name, string ArgumentsJson, string? ProviderData = null);

public sealed record AiChatMessage(
    AiChatRole Role,
    string? Content,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public static AiChatMessage System(string content) => new(AiChatRole.System, content);

    public static AiChatMessage User(string content) => new(AiChatRole.User, content);

    public static AiChatMessage Assistant(string? content, IReadOnlyList<AiToolCall>? toolCalls = null) =>
        new(AiChatRole.Assistant, content, toolCalls);

    public static AiChatMessage ToolResult(string toolCallId, string content) =>
        new(AiChatRole.Tool, content, ToolCallId: toolCallId);
}

/// <param name="ContainsPersonalData">
/// Set by the caller when any message carries real customer content. Such requests are refused unless the data
/// policy allows personal data for the target provider (free tiers never qualify).
/// </param>
public sealed record AiChatRequest(
    IReadOnlyList<AiChatMessage> Messages,
    IReadOnlyList<AiToolDefinition>? Tools = null,
    AiToolChoice ToolChoice = AiToolChoice.Auto,
    AiModelProfile Profile = AiModelProfile.Primary,
    int? MaxOutputTokens = null,
    double? Temperature = null,
    TimeSpan? Timeout = null,
    bool ContainsPersonalData = false);

public sealed record AiUsage(int InputTokens, int OutputTokens);

public sealed record AiChatResult
{
    public bool IsSuccess { get; private init; }

    public string? Text { get; private init; }

    public IReadOnlyList<AiToolCall> ToolCalls { get; private init; } = [];

    public AiFinishReason FinishReason { get; private init; }

    public AiUsage? Usage { get; private init; }

    /// <summary>Informational: provider/model that answered, e.g. for redacted traces. Never used for decisions.</summary>
    public string? Provider { get; private init; }

    public string? Model { get; private init; }

    public TimeSpan Latency { get; private init; }

    public AiFailureKind? Failure { get; private init; }

    /// <summary>Safe, fixed description of the failure. Never contains prompt content or keys.</summary>
    public string? FailureMessage { get; private init; }

    public TimeSpan? RetryAfter { get; private init; }

    /// <summary>Profiles tried, in order (e.g. Primary then Fallback).</summary>
    public IReadOnlyList<AiModelProfile> Attempts { get; init; } = [];

    public static AiChatResult Success(
        string? text,
        IReadOnlyList<AiToolCall>? toolCalls,
        AiFinishReason finishReason,
        AiUsage? usage,
        string? provider,
        string? model,
        TimeSpan latency) => new()
    {
        IsSuccess = true,
        Text = text,
        ToolCalls = toolCalls ?? [],
        FinishReason = finishReason,
        Usage = usage,
        Provider = provider,
        Model = model,
        Latency = latency
    };

    public static AiChatResult Failed(
        AiFailureKind failure,
        string message,
        string? provider = null,
        string? model = null,
        TimeSpan latency = default,
        TimeSpan? retryAfter = null) => new()
    {
        IsSuccess = false,
        Failure = failure,
        FailureMessage = message,
        Provider = provider,
        Model = model,
        Latency = latency,
        RetryAfter = retryAfter
    };
}
