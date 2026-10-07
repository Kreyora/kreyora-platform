using Kreyora.Domain.Common;

namespace Kreyora.Domain.Assistant;

public enum AssistantTurnOutcome
{
    Running = 1,
    Replied = 2,
    Escalated = 3,
    Fallback = 4,
    Skipped = 5,
    Superseded = 6,
    Blocked = 7,
    Abandoned = 8
}

/// <summary>One model call in a turn: who answered, how long, how many tokens. Never the prompt or the answer.</summary>
public sealed record TurnModelCall(string Profile, string? Provider, string? Model, long LatencyMs, int InputTokens, int OutputTokens, string Outcome);

/// <summary>One tool execution, copied from the registry's values-free trace.</summary>
public sealed record TurnToolStep(string Tool, int ToolVersion, string Outcome, long DurationMs, IReadOnlyList<string> ArgumentFields, string ArgumentsHash, int ResultCount, bool Replayed, bool DryRun);

/// <summary>A knowledge passage offered to the model (IDs and score only).</summary>
public sealed record TurnCitation(string DocumentId, string VersionId, int ChunkIndex, double Score);

/// <summary>
/// The redacted AI action log (M09-S06 Q9, ADR-021): one row per assistant turn, written for every outcome. Holds
/// versions, model/tool/knowledge metadata, budgets used, rule codes and the outbound message ID — never prompts,
/// customer text, reply text, tool argument values, keys or model reasoning. While <see cref="AssistantTurnOutcome.Running"/>
/// it also serves as the per-conversation lease (a partial unique index allows one running turn per conversation).
/// </summary>
public sealed class AssistantTurn : BaseEntity, ITenantOwned
{
    public const int ReasonMaxLength = 64;

    private AssistantTurn() { }

    public string TenantId { get; private set; } = string.Empty;
    public string? ConversationId { get; private set; }
    public string? TriggerMessageId { get; private set; }
    public string TurnKey { get; private set; } = string.Empty;
    public bool IsPlayground { get; private set; }
    public AssistantTurnOutcome Outcome { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? PolicyVersion { get; private set; }
    public string? PromptVersion { get; private set; }
    public string? RegistryVersion { get; private set; }
    public List<TurnModelCall> ModelCalls { get; private set; } = [];
    public List<TurnToolStep> ToolSteps { get; private set; } = [];
    public List<TurnCitation> Citations { get; private set; } = [];
    public List<string> ValidationCodes { get; private set; } = [];
    public int ModelCallCount { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public long EstimatedCostMicroUsd { get; private set; }
    public string? OutboundMessageId { get; private set; }

    public static AssistantTurn Start(string tenantId, string? conversationId, string? triggerMessageId, string turnKey, bool isPlayground, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnKey);
        return new AssistantTurn
        {
            TenantId = tenantId, ConversationId = conversationId, TriggerMessageId = triggerMessageId, TurnKey = turnKey,
            IsPlayground = isPlayground, Outcome = AssistantTurnOutcome.Running, ReasonCode = "running", StartedAt = now
        };
    }

    public bool IsFinished => Outcome != AssistantTurnOutcome.Running;

    /// <summary>A running turn older than this is treated as crashed and may be restarted.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan deadline) => Outcome == AssistantTurnOutcome.Running && now - StartedAt > deadline + TimeSpan.FromMinutes(1);

    /// <summary>Restarts a crashed turn under the same key (its partial record is discarded).</summary>
    public void Restart(DateTimeOffset now)
    {
        Outcome = AssistantTurnOutcome.Running;
        ReasonCode = "running";
        StartedAt = now;
        FinishedAt = null;
        ModelCalls = [];
        ToolSteps = [];
        Citations = [];
        ValidationCodes = [];
        ModelCallCount = 0;
        InputTokens = 0;
        OutputTokens = 0;
        EstimatedCostMicroUsd = 0;
        OutboundMessageId = null;
    }

    public void RecordVersions(string policyVersion, string promptVersion, string registryVersion)
    {
        PolicyVersion = policyVersion;
        PromptVersion = promptVersion;
        RegistryVersion = registryVersion;
    }

    public void RecordModelCall(TurnModelCall call, long costMicroUsd)
    {
        ArgumentNullException.ThrowIfNull(call);
        ModelCalls.Add(call);
        ModelCallCount++;
        InputTokens += call.InputTokens;
        OutputTokens += call.OutputTokens;
        EstimatedCostMicroUsd += costMicroUsd;
    }

    public void RecordToolStep(TurnToolStep step) => ToolSteps.Add(step);

    public void RecordCitations(IEnumerable<TurnCitation> citations) => Citations.AddRange(citations);

    public void RecordValidation(IEnumerable<string> codes) => ValidationCodes.AddRange(codes.Where(c => !ValidationCodes.Contains(c)));

    public void Finish(AssistantTurnOutcome outcome, string reasonCode, DateTimeOffset now, string? outboundMessageId = null)
    {
        if (outcome == AssistantTurnOutcome.Running) throw new ArgumentOutOfRangeException(nameof(outcome));
        Outcome = outcome;
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "unspecified" : reasonCode.Length > ReasonMaxLength ? reasonCode[..ReasonMaxLength] : reasonCode;
        FinishedAt = now;
        OutboundMessageId = outboundMessageId ?? OutboundMessageId;
    }
}
