using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kreyora.Domain.Common;

namespace Kreyora.Domain.Assistant;

public enum KnowledgeCategory
{
    Faq = 1,
    Delivery = 2,
    Returns = 3,
    Payment = 4,
    Brand = 5,
    Other = 6
}

public enum KnowledgeSource
{
    Text = 1,
    Upload = 2,
    StorePolicy = 3
}

public enum StorePolicyKind
{
    Returns = 1,
    Payment = 2,
    Terms = 3
}

public enum KnowledgeVersionState
{
    PendingReview = 1,
    Active = 2,
    Superseded = 3,
    Rejected = 4,
    Deleted = 5
}

/// <summary>
/// Seller-approved knowledge for the assistant (FAQ, delivery, returns, brand). The document points at its one
/// Active version; only Active versions of non-deleted documents may ever be retrieved (M09-S02).
/// </summary>
public sealed class KnowledgeDocument : BaseEntity, ITenantOwned
{
    public const int TitleMaxLength = 150;

    private KnowledgeDocument() { }

    public string TenantId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public KnowledgeCategory Category { get; private set; }
    public KnowledgeSource Source { get; private set; }
    public StorePolicyKind? StorePolicyKind { get; private set; }
    public string? ActiveVersionId { get; private set; }
    public int LatestVersionNumber { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static KnowledgeDocument Create(string tenantId, string title, KnowledgeCategory category, KnowledgeSource source, StorePolicyKind? storePolicyKind = null)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("A title is required.", nameof(title)) : title.Trim();
        if (normalizedTitle.Length > TitleMaxLength) throw new ArgumentOutOfRangeException(nameof(title), $"Titles are limited to {TitleMaxLength} characters.");
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        return new KnowledgeDocument
        {
            TenantId = tenantId,
            Title = normalizedTitle,
            Category = category,
            Source = source,
            StorePolicyKind = source == KnowledgeSource.StorePolicy ? storePolicyKind ?? throw new ArgumentNullException(nameof(storePolicyKind)) : null
        };
    }

    public int NextVersionNumber() => ++LatestVersionNumber;

    public void Activate(string versionId) => ActiveVersionId = versionId;

    public void MarkDeleted(DateTimeOffset now)
    {
        DeletedAt ??= now;
        ActiveVersionId = null;
    }
}

public sealed class KnowledgeDocumentVersion : BaseEntity, ITenantOwned
{
    public const int ReviewNoteMaxLength = 500;

    private KnowledgeDocumentVersion() { }

    public string TenantId { get; private set; } = string.Empty;
    public string DocumentId { get; private set; } = string.Empty;
    public int VersionNumber { get; private set; }
    public KnowledgeVersionState State { get; private set; }

    /// <summary>Extracted plain text; cleared when the document is deleted.</summary>
    public string? ContentText { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public int CharacterCount { get; private set; }

    /// <summary>The uploaded original in private storage (uploads only); purged on delete.</summary>
    public string? OriginalObjectKey { get; private set; }
    public string? OriginalFileName { get; private set; }
    public long? OriginalByteSize { get; private set; }

    public string? IdempotencyKey { get; private set; }
    public string SubmittedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; private set; }
    public string? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }

    public static KnowledgeDocumentVersion Submit(
        KnowledgeDocument document,
        string normalizedText,
        string submittedByUserId,
        DateTimeOffset now,
        string? idempotencyKey,
        string? originalObjectKey = null,
        string? originalFileName = null,
        long? originalByteSize = null) => new()
    {
        TenantId = document.TenantId,
        DocumentId = document.Id,
        VersionNumber = document.NextVersionNumber(),
        State = KnowledgeVersionState.PendingReview,
        ContentText = normalizedText,
        ContentHash = KnowledgeText.Hash(normalizedText),
        CharacterCount = normalizedText.Length,
        OriginalObjectKey = originalObjectKey,
        OriginalFileName = originalFileName,
        OriginalByteSize = originalByteSize,
        IdempotencyKey = idempotencyKey,
        SubmittedByUserId = submittedByUserId,
        SubmittedAt = now
    };

    public void Approve(string reviewerUserId, DateTimeOffset now)
    {
        RequireState(KnowledgeVersionState.PendingReview, "approve");
        State = KnowledgeVersionState.Active;
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = now;
    }

    public void Reject(string reviewerUserId, DateTimeOffset now, string? note)
    {
        RequireState(KnowledgeVersionState.PendingReview, "reject");
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > ReviewNoteMaxLength }) throw new ArgumentOutOfRangeException(nameof(note), $"Review notes are limited to {ReviewNoteMaxLength} characters.");
        State = KnowledgeVersionState.Rejected;
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = now;
        ReviewNote = trimmed;
    }

    public void Supersede()
    {
        RequireState(KnowledgeVersionState.Active, "supersede");
        State = KnowledgeVersionState.Superseded;
    }

    /// <summary>Clears the text and returns the storage key to purge, if any.</summary>
    public string? MarkDeleted()
    {
        State = KnowledgeVersionState.Deleted;
        ContentText = null;
        var key = OriginalObjectKey;
        OriginalObjectKey = null;
        return key;
    }

    private void RequireState(KnowledgeVersionState expected, string action)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"Cannot {action} a knowledge version in state {State}.");
        }
    }
}

public enum KnowledgeTextProblem
{
    None,
    Empty,
    TooLong,
    TooLarge,
    UnsupportedType,
    InvalidEncoding
}

/// <summary>Validation and normalization of knowledge text and uploads. Only plain text ever reaches storage or a model.</summary>
public static class KnowledgeText
{
    public const int MaxCharacters = 30_000;
    public const int MaxUploadBytes = 256 * 1024;
    public static readonly IReadOnlyList<string> AllowedExtensions = [".txt", ".md"];

    public static (string? Text, KnowledgeTextProblem Problem) Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (null, KnowledgeTextProblem.Empty);
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            if (ch is '\n' or '\t' || !char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        var text = builder.ToString().Trim();
        if (text.Length == 0) return (null, KnowledgeTextProblem.Empty);
        return text.Length > MaxCharacters ? (null, KnowledgeTextProblem.TooLong) : (text, KnowledgeTextProblem.None);
    }

    /// <summary>Accepts UTF-8 <c>.txt</c>/<c>.md</c> files only; PDF, Word and other binaries are refused before extraction.</summary>
    public static (string? Text, KnowledgeTextProblem Problem) Extract(string fileName, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxUploadBytes) return (null, KnowledgeTextProblem.TooLarge);
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) || LooksBinary(bytes)) return (null, KnowledgeTextProblem.UnsupportedType);

        string decoded;
        try
        {
            decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (null, KnowledgeTextProblem.InvalidEncoding);
        }

        return Normalize(decoded.TrimStart('﻿'));
    }

    public static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("%PDF"u8) || bytes.StartsWith("PK\u0003\u0004"u8) || bytes.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }))
        {
            return true; // PDF, Office (zip) and legacy Office files
        }

        return bytes.IndexOf((byte)0) >= 0;
    }

    public static string Describe(KnowledgeTextProblem problem) => problem switch
    {
        KnowledgeTextProblem.Empty => "The text is empty.",
        KnowledgeTextProblem.TooLong => string.Create(CultureInfo.InvariantCulture, $"The text is longer than {MaxCharacters:N0} characters. Split it into smaller documents."),
        KnowledgeTextProblem.TooLarge => string.Create(CultureInfo.InvariantCulture, $"The file is larger than {MaxUploadBytes / 1024} KB."),
        KnowledgeTextProblem.UnsupportedType => "Only .txt and .md files are supported. For PDF or Word documents, paste the text instead.",
        KnowledgeTextProblem.InvalidEncoding => "The file is not valid UTF-8 text.",
        _ => "The content is valid."
    };
}
