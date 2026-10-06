using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kreyora.Domain.Common;

namespace Kreyora.Domain.Assistant;

/// <summary>
/// A passage of one approved knowledge version (M09-S03, ADR-019). <see cref="Text"/> is exactly
/// <c>version.ContentText[CharStart..CharEnd]</c>, which makes every citation traceable.
/// </summary>
public sealed class KnowledgeChunk : BaseEntity, ITenantOwned
{
    private KnowledgeChunk() { }

    public string TenantId { get; private set; } = string.Empty;
    public string DocumentId { get; private set; } = string.Empty;
    public string VersionId { get; private set; } = string.Empty;
    public int ChunkIndex { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int CharStart { get; private set; }
    public int CharEnd { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public float[]? Embedding { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public int? EmbeddingDimensions { get; private set; }
    public DateTimeOffset? IndexedAt { get; private set; }

    public static KnowledgeChunk Create(KnowledgeDocumentVersion version, ChunkSpan span) => new()
    {
        TenantId = version.TenantId,
        DocumentId = version.DocumentId,
        VersionId = version.Id,
        ChunkIndex = span.Index,
        Text = span.Text,
        CharStart = span.Start,
        CharEnd = span.End,
        ContentHash = KnowledgeText.Hash(span.Text)
    };

    public void SetEmbedding(float[] vector, string model, DateTimeOffset now)
    {
        Embedding = vector;
        EmbeddingModel = model;
        EmbeddingDimensions = vector.Length;
        IndexedAt = now;
    }

    public bool NeedsEmbedding(string model, int dimensions) =>
        Embedding is null || !string.Equals(EmbeddingModel, model, StringComparison.Ordinal) || EmbeddingDimensions != dimensions;
}

/// <summary>One chunk boundary: <c>Text == source[Start..End]</c>.</summary>
public sealed record ChunkSpan(int Index, int Start, int End, string Text);

/// <summary>
/// Deterministic, paragraph-aware chunking (ADR-019): about <see cref="TargetSize"/> characters with
/// <see cref="Overlap"/> characters of overlap, cutting only at whitespace (never inside a word or a Devanagari
/// grapheme cluster), preferring paragraph breaks, then sentence ends, then spaces.
/// </summary>
public static class KnowledgeChunker
{
    public const int TargetSize = 800;
    public const int Overlap = 100;

    public static IReadOnlyList<ChunkSpan> Chunk(string text, int targetSize = TargetSize, int overlap = Overlap)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<ChunkSpan>();
        var start = SkipWhitespace(text, 0);
        while (start < text.Length)
        {
            var hardEnd = Math.Min(text.Length, start + targetSize);
            var end = hardEnd >= text.Length ? text.Length : ChooseBreak(text, start, hardEnd);
            var trimmedEnd = end;
            while (trimmedEnd > start && char.IsWhiteSpace(text[trimmedEnd - 1])) trimmedEnd--;
            if (trimmedEnd > start)
            {
                spans.Add(new ChunkSpan(spans.Count, start, trimmedEnd, text[start..trimmedEnd]));
            }

            if (end >= text.Length) break;

            // Next chunk starts about `overlap` characters back, at a word boundary, but always moves forward.
            var next = BackToWordStart(text, Math.Max(start + 1, end - overlap));
            start = SkipWhitespace(text, next <= start ? end : next);
        }

        return spans;
    }

    private static int ChooseBreak(string text, int start, int hardEnd)
    {
        var minimum = start + (hardEnd - start) / 2; // don't make chunks tiny
        var paragraph = text.LastIndexOf("\n\n", hardEnd - 1, hardEnd - minimum, StringComparison.Ordinal);
        if (paragraph >= minimum) return paragraph + 2;

        for (var i = hardEnd - 1; i >= minimum; i--)
        {
            if (text[i] is '\n' || (char.IsWhiteSpace(text[i]) && i > 0 && text[i - 1] is '.' or '!' or '?' or '।' or '|'))
            {
                return i + 1;
            }
        }

        for (var i = hardEnd - 1; i > start; i--)
        {
            if (char.IsWhiteSpace(text[i])) return i + 1;
        }

        // No whitespace at all (one huge token): cut on a grapheme boundary.
        var boundaries = StringInfo.ParseCombiningCharacters(text[start..hardEnd]);
        return start + boundaries[^1];
    }

    private static int BackToWordStart(string text, int index)
    {
        while (index > 0 && !char.IsWhiteSpace(text[index - 1])) index--;
        return index;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }
}

/// <summary>Language-neutral tokens for lexical scoring and the fake embedder. Keeps Devanagari letters with their marks.</summary>
public static class KnowledgeTokenizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "is", "are", "and", "or", "to", "of", "in", "on", "for", "it", "do", "does", "you", "your", "i", "me", "my", "we", "our", "be", "with", "at"
    };

    public static IEnumerable<string> Tokens(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var builder = new StringBuilder();
        foreach (var raw in text.Normalize(NormalizationForm.FormC))
        {
            var ch = raw is >= '०' and <= '९' ? (char)('0' + (raw - '०')) : char.ToLowerInvariant(raw);
            var category = char.GetUnicodeCategory(ch);
            if (char.IsLetterOrDigit(ch) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0)
            {
                var token = builder.ToString();
                builder.Clear();
                if (!StopWords.Contains(token)) yield return token;
            }
        }

        if (builder.Length > 0 && !StopWords.Contains(builder.ToString())) yield return builder.ToString();
    }
}

/// <summary>
/// Flags text that looks like instructions to an AI ("ignore previous instructions", "reveal your system prompt") so
/// the reviewer is warned before approval (M09-S03 Q8). It never blocks: approved content is always treated as data.
/// </summary>
public static partial class SuspiciousInstructionDetector
{
    [GeneratedRegex(
        @"ignore\s+(all\s+|any\s+)?(the\s+)?(previous|prior|above|earlier)\s+(instructions|rules|prompts?)" +
        @"|disregard\s+(all\s+|any\s+)?(the\s+)?(previous|prior|above|your)\s+(instructions|rules)" +
        @"|(reveal|print|show|repeat)\s+(me\s+)?(your|the)\s+(system\s+prompt|instructions|hidden\s+instructions)" +
        @"|system\s+prompt|you\s+are\s+now\s+(an?\s+|in\s+)?|developer\s+mode|jailbreak|act\s+as\s+(an?\s+)?(admin|developer|owner)" +
        @"|rules\s+birsa|niyam\s+birsa",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool LooksSuspicious(string? text) => !string.IsNullOrEmpty(text) && Pattern().IsMatch(text);
}
