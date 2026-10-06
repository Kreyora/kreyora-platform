using Kreyora.Application.Models;
using Kreyora.Domain.Assistant;

namespace Kreyora.Application.Assistant;

/// <summary>Stable problem codes for assistant APIs (RFC 7807 <c>type</c> = <c>urn:kreyora:problem:&lt;code&gt;</c>).</summary>
public static class AssistantProblemCodes
{
    public const string PolicyChanged = "assistant_policy_changed";
    public const string PolicyInvalid = "assistant_policy_invalid";
    public const string KnowledgeNotFound = "knowledge_not_found";
    public const string KnowledgeInvalidTransition = "knowledge_invalid_transition";
    public const string KnowledgeChanged = "knowledge_changed";
    public const string KnowledgeEmpty = "knowledge_empty";
    public const string KnowledgeTooLong = "knowledge_too_long";
    public const string KnowledgeTooLarge = "knowledge_too_large";
    public const string KnowledgeUnsupportedType = "knowledge_unsupported_type";
    public const string KnowledgeInvalidEncoding = "knowledge_invalid_encoding";
    public const string KnowledgeInvalid = "knowledge_invalid";
    public const string StorePoliciesMissing = "store_policies_missing";

    public static string ProblemType(string code) => $"urn:kreyora:problem:{code}";

    public static Result<T> Problem<T>(string code, string detail, int status, IDictionary<string, string[]>? errors = null) =>
        Result<T>.Failure(new ErrorDetail { Type = ProblemType(code), Title = code, Status = status, Detail = detail, Errors = errors });
}

// ---- Policy -------------------------------------------------------------------------------------------------

public sealed record AssistantPolicyItem(
    bool Enabled,
    AssistantReplyStyle ReplyStyle,
    IReadOnlyList<string> SupportedLanguages,
    AssistantTone Tone,
    string? BrandNote,
    IReadOnlyList<DailyHours> BusinessHours,
    string TimeZone,
    OutsideHoursBehavior OutsideHoursBehavior,
    UnrecognizedMediaBehavior UnrecognizedMediaBehavior,
    IReadOnlyList<string> EscalationKeywords,
    IReadOnlyList<string> AllowedTools,
    int MaxToolSteps,
    int MaxRepliesPerConversationPerHour,
    int MaxOutputTokens,
    DateTimeOffset? ReviewedAt,
    string Version,
    IReadOnlyList<string> FixedEscalationCategories,
    IReadOnlyList<string> AvailableTools,
    AssistantPlatformCaps PlatformCaps);

/// <param name="Version">The <see cref="AssistantPolicyItem.Version"/> the seller edited; a stale value returns 409.</param>
public sealed record UpdateAssistantPolicyRequest(
    bool Enabled,
    AssistantReplyStyle ReplyStyle,
    IReadOnlyList<string> SupportedLanguages,
    AssistantTone Tone,
    string? BrandNote,
    IReadOnlyList<DailyHours> BusinessHours,
    OutsideHoursBehavior OutsideHoursBehavior,
    UnrecognizedMediaBehavior UnrecognizedMediaBehavior,
    IReadOnlyList<string> EscalationKeywords,
    IReadOnlyList<string> AllowedTools,
    int MaxToolSteps,
    int MaxRepliesPerConversationPerHour,
    int MaxOutputTokens,
    string Version);

public interface IAssistantPolicyService
{
    /// <summary>Returns the workspace policy, creating safe defaults on first read.</summary>
    Task<Result<AssistantPolicyItem>> GetAsync(CancellationToken cancellationToken = default);

    Task<Result<AssistantPolicyItem>> UpdateAsync(UpdateAssistantPolicyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Effective policy for orchestration (S06): budgets already clamped to platform caps.</summary>
public interface IAssistantPolicyQuery
{
    Task<AssistantPolicyItem> GetEffectiveAsync(CancellationToken cancellationToken = default);
}

// ---- Readiness / activation ---------------------------------------------------------------------------------

public sealed record AssistantReadinessCheck(string Code, bool Passed, bool Required, string Detail, IReadOnlyList<string> Blockers);

/// <param name="IsActive">Seller toggle on AND every required check passed AND the platform AI switch on.</param>
public sealed record AssistantReadinessItem(bool IsActive, bool SellerEnabled, bool PlatformEnabled, IReadOnlyList<AssistantReadinessCheck> Checks);

public interface IAssistantReadinessService
{
    Task<Result<AssistantReadinessItem>> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Checked before any AI invocation (S07). Computed on read, never stored.</summary>
public interface IAssistantActivationQuery
{
    Task<bool> IsActiveAsync(CancellationToken cancellationToken = default);
}

// ---- Knowledge ----------------------------------------------------------------------------------------------

public sealed record KnowledgeVersionSummary(
    string Id,
    int VersionNumber,
    KnowledgeVersionState State,
    int CharacterCount,
    string? OriginalFileName,
    string SubmittedByUserId,
    DateTimeOffset SubmittedAt,
    string? ReviewedByUserId,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    bool HasSuspiciousInstructions = false);

/// <summary>Index state of the active version: chunks exist at approval; vectors follow (ADR-019).</summary>
public sealed record KnowledgeIndexStatus(int Chunks, int Indexed);

public sealed record KnowledgeDocumentItem(
    string Id,
    string Title,
    KnowledgeCategory Category,
    KnowledgeSource Source,
    StorePolicyKind? StorePolicyKind,
    KnowledgeVersionSummary? ActiveVersion,
    IReadOnlyList<KnowledgeVersionSummary> PendingVersions,
    int LatestVersionNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    KnowledgeIndexStatus? IndexStatus = null);

public sealed record KnowledgeVersionDetail(string DocumentId, string DocumentTitle, KnowledgeVersionSummary Version, string? Text);

public sealed record CreateKnowledgeFromTextRequest(string Title, KnowledgeCategory Category, string Text);

public sealed record CreateKnowledgeVersionRequest(string Text);

public sealed record RejectKnowledgeVersionRequest(string? Note);

/// <summary>A <c>.txt</c>/<c>.md</c> upload. <paramref name="DocumentId"/> set: a new version of that document.</summary>
public sealed record KnowledgeUpload(string FileName, Stream Content, string? Title, KnowledgeCategory? Category, string? DocumentId);

public interface IKnowledgeService
{
    Task<Result<IReadOnlyList<KnowledgeDocumentItem>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<KnowledgeVersionDetail>> GetVersionAsync(string documentId, string versionId, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentItem>> CreateFromTextAsync(CreateKnowledgeFromTextRequest request, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentItem>> CreateVersionAsync(string documentId, CreateKnowledgeVersionRequest request, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentItem>> UploadAsync(KnowledgeUpload upload, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentItem>> ApproveAsync(string documentId, string versionId, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentItem>> RejectAsync(string documentId, string versionId, RejectKnowledgeVersionRequest request, CancellationToken cancellationToken = default);

    Task<Result<KnowledgeDocumentItem>> DeleteAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>Creates PendingReview drafts from the store's Returns / Payment / Terms policies; never auto-approves.</summary>
    Task<Result<IReadOnlyList<KnowledgeDocumentItem>>> ImportStorePoliciesAsync(CancellationToken cancellationToken = default);
}

/// <summary>One approved, active knowledge version, the only content retrieval (S03) may index.</summary>
public sealed record ApprovedKnowledgeItem(string DocumentId, string VersionId, string Title, KnowledgeCategory Category, string Text, string ContentHash);

public interface IApprovedKnowledgeQuery
{
    /// <summary>Active versions of non-deleted documents in the current workspace. Nothing else is ever returned.</summary>
    Task<IReadOnlyList<ApprovedKnowledgeItem>> GetActiveAsync(CancellationToken cancellationToken = default);
}

// ---- Retrieval (M09-S03, ADR-019) ---------------------------------------------------------------------------

/// <summary>Queues embedding work after commit (Hangfire in production; no-op when jobs are unavailable).</summary>
public interface IKnowledgeIndexScheduler
{
    void ScheduleVersionIndexing(string tenantId, string versionId);

    void ScheduleTenantReindex(string tenantId);
}

/// <summary>Embeds a version's chunks and maintains the index (missing vectors, model changes, orphans).</summary>
public interface IKnowledgeIndexingService
{
    /// <summary>Embeds chunks of the given version that lack a vector for the configured model; returns how many were embedded.</summary>
    Task<int> IndexVersionAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>Current tenant: embeds missing/outdated chunks, removes orphan chunks, retries pending original-file purges.</summary>
    Task<KnowledgeIndexMaintenance> MaintainAsync(CancellationToken cancellationToken = default);

    /// <summary>Current tenant: clears every vector and re-embeds (the reindex workflow, e.g. after a model change).</summary>
    Task<KnowledgeIndexMaintenance> ReindexAsync(CancellationToken cancellationToken = default);
}

public sealed record KnowledgeIndexMaintenance(int Embedded, int OrphanChunksRemoved, int OriginalsPurged, bool EmbeddingAvailable);

public enum RetrievalConfidence
{
    None,
    Low,
    High
}

public enum RetrievalMode
{
    /// <summary>Embedding similarity plus a small lexical boost.</summary>
    Hybrid,
    /// <summary>Embeddings unavailable (AI disabled, provider failure, not indexed): lexical overlap only.</summary>
    LexicalFallback
}

/// <summary>Exact source of a passage: <c>version.ContentText[CharStart..CharEnd]</c>.</summary>
public sealed record KnowledgeCitation(string DocumentId, string DocumentTitle, KnowledgeCategory Category, string VersionId, int VersionNumber, int ChunkIndex, int CharStart, int CharEnd, string ContentHash);

/// <param name="Untrusted">Always true: passages are reference data, never instructions (S06 wraps them as quoted data).</param>
public sealed record KnowledgePassage(string Text, double Score, KnowledgeCitation Citation, bool Untrusted = true);

public sealed record KnowledgeRetrievalResult(RetrievalConfidence Confidence, RetrievalMode Mode, IReadOnlyList<KnowledgePassage> Passages);

public interface IKnowledgeRetrievalService
{
    /// <summary>
    /// Approved passages of the current workspace for a customer message. Tenant and approval state are filtered in SQL
    /// before any ranking; nothing is returned below the low-confidence threshold.
    /// </summary>
    Task<KnowledgeRetrievalResult> RetrieveAsync(string query, CancellationToken cancellationToken = default);
}

public sealed record KnowledgeSearchRequest(string Query);
