using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant;

/// <summary>
/// Approved-knowledge retrieval (ADR-019). Order of operations is the guarantee:
/// 1. tenant from trusted context; 2. SQL filter to the Active version of non-deleted documents (candidates);
/// 3. embed the query once; 4. score candidates only; 5. thresholds; 6. top-k within a character budget.
/// </summary>
public sealed class KnowledgeRetrievalService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAiEmbeddingClient embeddings,
    IOptionsMonitor<AiOptions> aiOptions) : IKnowledgeRetrievalService
{
    public const int MaxQueryCharacters = 1000;

    public async Task<KnowledgeRetrievalResult> RetrieveAsync(string query, CancellationToken cancellationToken = default)
    {
        var options = aiOptions.CurrentValue;
        var normalizedQuery = (query ?? string.Empty).Trim();
        if (normalizedQuery.Length == 0)
        {
            return new KnowledgeRetrievalResult(RetrievalConfidence.None, RetrievalMode.LexicalFallback, []);
        }

        if (normalizedQuery.Length > MaxQueryCharacters)
        {
            normalizedQuery = normalizedQuery[..MaxQueryCharacters];
        }

        // (1) + (2): candidates are only this tenant's chunks of the document's current Active version.
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var candidates = await (
            from chunk in dbContext.KnowledgeChunks.AsNoTracking()
            join version in dbContext.KnowledgeDocumentVersions.AsNoTracking() on chunk.VersionId equals version.Id
            join document in dbContext.KnowledgeDocuments.AsNoTracking() on version.DocumentId equals document.Id
            where chunk.TenantId == tenantId && version.TenantId == tenantId && document.TenantId == tenantId
                && version.State == KnowledgeVersionState.Active
                && document.DeletedAt == null
                && document.ActiveVersionId == version.Id
            select new Candidate(chunk.Text, chunk.Embedding, chunk.EmbeddingModel, chunk.EmbeddingDimensions,
                new KnowledgeCitation(document.Id, document.Title, document.Category, version.Id, version.VersionNumber,
                    chunk.ChunkIndex, chunk.CharStart, chunk.CharEnd, chunk.ContentHash)))
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return new KnowledgeRetrievalResult(RetrievalConfidence.None, RetrievalMode.LexicalFallback, []);
        }

        // (3): one query embedding; any failure (AI disabled, provider down) means lexical fallback.
        var (model, dimensions) = KnowledgeIndexingService.ExpectedEmbedding(options);
        float[]? queryVector = null;
        if (candidates.Any(c => c.Embedding is not null && c.Model == model && c.Dimensions == dimensions))
        {
            var embedded = await embeddings.EmbedAsync([normalizedQuery], AiEmbeddingPurpose.Query, cancellationToken);
            if (embedded.IsSuccess && embedded.Vectors.Count == 1 && embedded.Vectors[0].Length == dimensions && embedded.Model == model)
            {
                queryVector = embedded.Vectors[0];
            }
        }

        // (4) + (5) + (6)
        return Rank(normalizedQuery, queryVector, model, candidates, options.Retrieval);
    }

    /// <summary>Pure scoring and selection (unit-tested): hybrid when a query vector exists, otherwise lexical.</summary>
    public static KnowledgeRetrievalResult Rank(string query, float[]? queryVector, string model, IReadOnlyList<Candidate> candidates, AiRetrievalOptions retrieval)
    {
        ArgumentNullException.ThrowIfNull(retrieval);
        var queryTokens = KnowledgeTokenizer.Tokens(query).ToHashSet(StringComparer.Ordinal);
        var hybrid = queryVector is not null;
        var (relevant, low) = hybrid
            ? (retrieval.RelevantThreshold, retrieval.LowConfidenceThreshold)
            : (retrieval.LexicalRelevantThreshold, retrieval.LexicalLowConfidenceThreshold);

        var scored = candidates
            .Select(c =>
            {
                var lexical = LexicalOverlap(queryTokens, c.Text);
                var score = hybrid && c.Embedding is not null && c.Model == model && c.Embedding.Length == queryVector!.Length
                    ? Cosine(queryVector, c.Embedding) + retrieval.LexicalWeight * lexical
                    : hybrid ? lexical * retrieval.LexicalWeight : lexical; // un-indexed chunk in hybrid mode: boost only
                return (Candidate: c, Score: Math.Round(score, 4));
            })
            .Where(x => x.Score >= low)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Citation.DocumentId, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.Citation.ChunkIndex)
            .ToList();

        var passages = new List<KnowledgePassage>();
        var characters = 0;
        foreach (var (candidate, score) in scored)
        {
            if (passages.Count >= retrieval.TopK || characters + candidate.Text.Length > retrieval.MaxCharacters && passages.Count > 0)
            {
                break;
            }

            passages.Add(new KnowledgePassage(candidate.Text, score, candidate.Citation));
            characters += candidate.Text.Length;
        }

        var confidence = passages.Count == 0 ? RetrievalConfidence.None
            : passages[0].Score >= relevant ? RetrievalConfidence.High
            : RetrievalConfidence.Low;
        return new KnowledgeRetrievalResult(confidence, hybrid ? RetrievalMode.Hybrid : RetrievalMode.LexicalFallback, passages);
    }

    /// <summary>Share of distinct query words present in the passage (0..1).</summary>
    public static double LexicalOverlap(IReadOnlySet<string> queryTokens, string text)
    {
        if (queryTokens.Count == 0) return 0;
        var passageTokens = KnowledgeTokenizer.Tokens(text).ToHashSet(StringComparer.Ordinal);
        return (double)queryTokens.Count(passageTokens.Contains) / queryTokens.Count;
    }

    /// <summary>True cosine similarity; vectors at reduced dimensions are not unit length (ADR-019).</summary>
    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    public sealed record Candidate(string Text, float[]? Embedding, string? Model, int? Dimensions, KnowledgeCitation Citation);
}
