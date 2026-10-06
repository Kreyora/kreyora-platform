using System.Text.Json;
using Kreyora.Application.Assistant;
using Kreyora.Application.Audit;
using Kreyora.Application.Catalog;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Assistant;

/// <summary>
/// The assistant's knowledge library (M09-S02 §B). Content becomes retrievable only when an owner/admin approves a
/// version; approving supersedes the previous Active version; deleting purges uploaded originals and clears text.
/// Knowledge text is never written to logs or audit metadata.
/// </summary>
public sealed class KnowledgeService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantKeyBuilder tenantKeys,
    IPrivateObjectStorage storage,
    IAuditEventService auditEvents,
    ITimeProvider timeProvider) : IKnowledgeService, IApprovedKnowledgeQuery
{
    private static readonly Dictionary<StorePolicyKind, (string Title, KnowledgeCategory Category)> StorePolicyDocuments =
        new()
        {
            [StorePolicyKind.Returns] = ("Returns policy", KnowledgeCategory.Returns),
            [StorePolicyKind.Payment] = ("Payment policy", KnowledgeCategory.Payment),
            [StorePolicyKind.Terms] = ("Terms and conditions", KnowledgeCategory.Other)
        };

    // ---- reads ---------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<KnowledgeDocumentItem>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId();
        var documents = await dbContext.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.DeletedAt == null)
            .OrderByDescending(d => d.ModifiedAt)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<KnowledgeDocumentItem>>.Success(await ToItemsAsync(documents, cancellationToken));
    }

    public async Task<Result<KnowledgeVersionDetail>> GetVersionAsync(string documentId, string versionId, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId();
        var document = await dbContext.KnowledgeDocuments.AsNoTracking()
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId && d.DeletedAt == null, cancellationToken);
        var version = document is null ? null : await dbContext.KnowledgeDocumentVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.TenantId == tenantId && v.Id == versionId && v.DocumentId == documentId, cancellationToken);
        return version is null
            ? NotFound<KnowledgeVersionDetail>()
            : Result<KnowledgeVersionDetail>.Success(new KnowledgeVersionDetail(document!.Id, document.Title, Summary(version), version.ContentText));
    }

    public async Task<IReadOnlyList<ApprovedKnowledgeItem>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId();
        // Only the Active version of each non-deleted document. Pending, rejected, superseded and deleted content is never returned.
        return await (
            from document in dbContext.KnowledgeDocuments.AsNoTracking()
            join version in dbContext.KnowledgeDocumentVersions.AsNoTracking() on document.ActiveVersionId equals version.Id
            where document.TenantId == tenantId && version.TenantId == tenantId
                && document.DeletedAt == null
                && version.State == KnowledgeVersionState.Active
                && version.ContentText != null
            orderby document.Title
            select new ApprovedKnowledgeItem(document.Id, version.Id, document.Title, document.Category, version.ContentText!, version.ContentHash))
            .ToListAsync(cancellationToken);
    }

    // ---- submissions ---------------------------------------------------------------------------------------

    public async Task<Result<KnowledgeDocumentItem>> CreateFromTextAsync(CreateKnowledgeFromTextRequest request, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (text, problem) = KnowledgeText.Normalize(request.Text);
        if (problem != KnowledgeTextProblem.None) return TextProblem<KnowledgeDocumentItem>(problem);
        if (await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken) is { } replay) return replay;

        KnowledgeDocument document;
        try
        {
            document = KnowledgeDocument.Create(TenantId(), request.Title, request.Category, KnowledgeSource.Text);
        }
        catch (ArgumentException ex)
        {
            return Invalid<KnowledgeDocumentItem>(ex.Message);
        }

        dbContext.KnowledgeDocuments.Add(document);
        return await SubmitAsync(document, text!, idempotencyKey, "assistant.knowledge.submitted", cancellationToken);
    }

    public async Task<Result<KnowledgeDocumentItem>> CreateVersionAsync(string documentId, CreateKnowledgeVersionRequest request, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (text, problem) = KnowledgeText.Normalize(request.Text);
        if (problem != KnowledgeTextProblem.None) return TextProblem<KnowledgeDocumentItem>(problem);
        if (await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken) is { } replay) return replay;

        var document = await FindDocumentAsync(documentId, cancellationToken);
        return document is null
            ? NotFound<KnowledgeDocumentItem>()
            : await SubmitAsync(document, text!, idempotencyKey, "assistant.knowledge.submitted", cancellationToken);
    }

    public async Task<Result<KnowledgeDocumentItem>> UploadAsync(KnowledgeUpload upload, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var bytes = await ReadLimitedAsync(upload.Content, KnowledgeText.MaxUploadBytes, cancellationToken);
        if (bytes is null) return TextProblem<KnowledgeDocumentItem>(KnowledgeTextProblem.TooLarge);
        var (text, problem) = KnowledgeText.Extract(upload.FileName, bytes);
        if (problem != KnowledgeTextProblem.None) return TextProblem<KnowledgeDocumentItem>(problem);
        if (await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken) is { } replay) return replay;

        KnowledgeDocument? document;
        if (upload.DocumentId is not null)
        {
            document = await FindDocumentAsync(upload.DocumentId, cancellationToken);
            if (document is null) return NotFound<KnowledgeDocumentItem>();
        }
        else
        {
            try
            {
                var title = string.IsNullOrWhiteSpace(upload.Title) ? Path.GetFileNameWithoutExtension(upload.FileName) : upload.Title;
                document = KnowledgeDocument.Create(TenantId(), title, upload.Category ?? KnowledgeCategory.Other, KnowledgeSource.Upload);
            }
            catch (ArgumentException ex)
            {
                return Invalid<KnowledgeDocumentItem>(ex.Message);
            }

            dbContext.KnowledgeDocuments.Add(document);
        }

        var safeName = Path.GetFileName(upload.FileName);
        var objectKey = tenantKeys.BuildStorageObjectKey("assistant-knowledge", document.Id, $"{Guid.NewGuid():N}{Path.GetExtension(safeName).ToLowerInvariant()}");
        await using (var content = new MemoryStream(bytes, writable: false))
        {
            await storage.PutAsync(new StorageObjectWrite(objectKey, "text/plain; charset=utf-8", bytes.Length, content), cancellationToken);
        }

        var result = await SubmitAsync(document, text!, idempotencyKey, "assistant.knowledge.submitted", cancellationToken,
            objectKey, safeName.Length > 255 ? safeName[..255] : safeName, bytes.Length);
        var tenantId = TenantId();
        if (!await dbContext.KnowledgeDocumentVersions.AnyAsync(v => v.TenantId == tenantId && v.OriginalObjectKey == objectKey, cancellationToken))
        {
            await storage.DeleteIfExistsAsync(objectKey, cancellationToken); // not kept (failure or idempotent replay)
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<KnowledgeDocumentItem>>> ImportStorePoliciesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId();
        var store = await dbContext.Stores.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        var policies = new Dictionary<StorePolicyKind, string?>
        {
            [StorePolicyKind.Returns] = store?.ReturnsPolicy,
            [StorePolicyKind.Payment] = store?.PaymentPolicy,
            [StorePolicyKind.Terms] = store?.TermsPolicy
        };

        var touched = new List<KnowledgeDocument>();
        foreach (var (kind, raw) in policies)
        {
            var (text, problem) = KnowledgeText.Normalize(raw);
            if (problem != KnowledgeTextProblem.None)
            {
                continue;
            }

            var document = await dbContext.KnowledgeDocuments
                .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.StorePolicyKind == kind && d.DeletedAt == null, cancellationToken);
            if (document is null)
            {
                var (title, category) = StorePolicyDocuments[kind];
                document = KnowledgeDocument.Create(tenantId, title, category, KnowledgeSource.StorePolicy, kind);
                dbContext.KnowledgeDocuments.Add(document);
            }
            else
            {
                var hash = KnowledgeText.Hash(text!);
                var unchanged = await dbContext.KnowledgeDocumentVersions.AnyAsync(v => v.TenantId == tenantId && v.DocumentId == document.Id
                    && v.ContentHash == hash && (v.State == KnowledgeVersionState.Active || v.State == KnowledgeVersionState.PendingReview), cancellationToken);
                if (unchanged)
                {
                    continue; // same text already live or awaiting review
                }
            }

            var version = KnowledgeDocumentVersion.Submit(document, text!, UserId(), timeProvider.UtcNow, idempotencyKey: null);
            dbContext.KnowledgeDocumentVersions.Add(version);
            try
            {
                await auditEvents.AppendAsync(Audit("assistant.knowledge.imported", document, version), cancellationToken);
            }
            catch (DbUpdateException)
            {
                dbContext.ChangeTracker.Clear(); // a concurrent import created the same policy document
                return AssistantProblemCodes.Problem<IReadOnlyList<KnowledgeDocumentItem>>(AssistantProblemCodes.KnowledgeChanged,
                    "The store policies were imported by someone else at the same time. Refresh to see them.", 409);
            }

            touched.Add(document);
        }

        if (touched.Count == 0 && policies.Values.All(string.IsNullOrWhiteSpace))
        {
            return AssistantProblemCodes.Problem<IReadOnlyList<KnowledgeDocumentItem>>(AssistantProblemCodes.StorePoliciesMissing,
                "The store has no Returns, Payment or Terms policy text to import yet.", 422);
        }

        return Result<IReadOnlyList<KnowledgeDocumentItem>>.Success(await ToItemsAsync(touched, cancellationToken));
    }

    // ---- review --------------------------------------------------------------------------------------------

    public async Task<Result<KnowledgeDocumentItem>> ApproveAsync(string documentId, string versionId, CancellationToken cancellationToken = default)
    {
        var document = await FindDocumentAsync(documentId, cancellationToken);
        var version = document is null ? null : await FindVersionAsync(documentId, versionId, cancellationToken);
        if (version is null) return NotFound<KnowledgeDocumentItem>();
        if (version.State != KnowledgeVersionState.PendingReview) return InvalidTransition<KnowledgeDocumentItem>(version.State, "approve");

        var now = timeProvider.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Supersede first and save: PostgreSQL checks the one-Active-per-document index row by row.
            if (document!.ActiveVersionId is { } previousId && await FindVersionAsync(documentId, previousId, cancellationToken) is { State: KnowledgeVersionState.Active } previous)
            {
                previous.Supersede();
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            version.Approve(UserId(), now);
            document.Activate(version.Id);
            await auditEvents.AppendAsync(Audit("assistant.knowledge.approved", document, version), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return AssistantProblemCodes.Problem<KnowledgeDocumentItem>(AssistantProblemCodes.KnowledgeChanged,
                "This document was changed by someone else. Refresh and try again.", 409);
        }

        return Result<KnowledgeDocumentItem>.Success((await ToItemsAsync([document], cancellationToken))[0]);
    }

    public async Task<Result<KnowledgeDocumentItem>> RejectAsync(string documentId, string versionId, RejectKnowledgeVersionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = await FindDocumentAsync(documentId, cancellationToken);
        var version = document is null ? null : await FindVersionAsync(documentId, versionId, cancellationToken);
        if (version is null) return NotFound<KnowledgeDocumentItem>();
        if (version.State != KnowledgeVersionState.PendingReview) return InvalidTransition<KnowledgeDocumentItem>(version.State, "reject");

        try
        {
            version.Reject(UserId(), timeProvider.UtcNow, request.Note);
        }
        catch (ArgumentException ex)
        {
            return Invalid<KnowledgeDocumentItem>(ex.Message);
        }

        try
        {
            await auditEvents.AppendAsync(Audit("assistant.knowledge.rejected", document!, version), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return AssistantProblemCodes.Problem<KnowledgeDocumentItem>(AssistantProblemCodes.KnowledgeChanged,
                "This document was changed by someone else. Refresh and try again.", 409);
        }

        return Result<KnowledgeDocumentItem>.Success((await ToItemsAsync([document!], cancellationToken))[0]);
    }

    public async Task<Result<KnowledgeDocumentItem>> DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var document = await FindDocumentAsync(documentId, cancellationToken);
        if (document is null) return NotFound<KnowledgeDocumentItem>();

        var tenantId = TenantId();
        var versions = await dbContext.KnowledgeDocumentVersions.Where(v => v.TenantId == tenantId && v.DocumentId == documentId).ToListAsync(cancellationToken);
        var keys = versions.Select(v => v.MarkDeleted()).OfType<string>().ToList();
        document.MarkDeleted(timeProvider.UtcNow);
        var item = Item(document, null, []);

        try
        {
            await auditEvents.AppendAsync(new AuditEventWrite("assistant.knowledge.deleted", "knowledge_document", document.Id,
                Metadata: JsonSerializer.Serialize(new { versions = versions.Count, category = document.Category.ToString() })), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return AssistantProblemCodes.Problem<KnowledgeDocumentItem>(AssistantProblemCodes.KnowledgeChanged,
                "This document was changed by someone else. Refresh and try again.", 409);
        }

        // Text is already cleared in the database; now purge the uploaded originals.
        foreach (var key in keys)
        {
            await storage.DeleteIfExistsAsync(key, cancellationToken);
        }

        return Result<KnowledgeDocumentItem>.Success(item);
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private async Task<Result<KnowledgeDocumentItem>> SubmitAsync(
        KnowledgeDocument document,
        string text,
        string? idempotencyKey,
        string auditAction,
        CancellationToken cancellationToken,
        string? objectKey = null,
        string? fileName = null,
        long? byteSize = null)
    {
        var version = KnowledgeDocumentVersion.Submit(document, text, UserId(), timeProvider.UtcNow,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(), objectKey, fileName, byteSize);
        dbContext.KnowledgeDocumentVersions.Add(version);
        try
        {
            await auditEvents.AppendAsync(Audit(auditAction, document, version), cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Same idempotency key raced in, or the document changed concurrently.
            dbContext.ChangeTracker.Clear();
            return await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                ?? AssistantProblemCodes.Problem<KnowledgeDocumentItem>(AssistantProblemCodes.KnowledgeChanged,
                    "This document was changed by someone else. Refresh and try again.", 409);
        }

        return Result<KnowledgeDocumentItem>.Success((await ToItemsAsync([document], cancellationToken))[0]);
    }

    private async Task<Result<KnowledgeDocumentItem>?> FindByIdempotencyKeyAsync(string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
        var tenantId = TenantId();
        var key = idempotencyKey.Trim();
        var version = await dbContext.KnowledgeDocumentVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.TenantId == tenantId && v.IdempotencyKey == key, cancellationToken);
        if (version is null) return null;
        var document = await dbContext.KnowledgeDocuments.AsNoTracking().SingleAsync(d => d.TenantId == tenantId && d.Id == version.DocumentId, cancellationToken);
        return Result<KnowledgeDocumentItem>.Success((await ToItemsAsync([document], cancellationToken))[0]);
    }

    private Task<KnowledgeDocument?> FindDocumentAsync(string documentId, CancellationToken cancellationToken)
    {
        var tenantId = TenantId();
        return dbContext.KnowledgeDocuments.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId && d.DeletedAt == null, cancellationToken);
    }

    private Task<KnowledgeDocumentVersion?> FindVersionAsync(string documentId, string versionId, CancellationToken cancellationToken)
    {
        var tenantId = TenantId();
        return dbContext.KnowledgeDocumentVersions.SingleOrDefaultAsync(v => v.TenantId == tenantId && v.Id == versionId && v.DocumentId == documentId, cancellationToken);
    }

    private async Task<IReadOnlyList<KnowledgeDocumentItem>> ToItemsAsync(List<KnowledgeDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0) return [];
        var tenantId = TenantId();
        var ids = documents.Select(d => d.Id).ToList();
        var versions = await dbContext.KnowledgeDocumentVersions.AsNoTracking()
            .Where(v => v.TenantId == tenantId && ids.Contains(v.DocumentId)
                && (v.State == KnowledgeVersionState.Active || v.State == KnowledgeVersionState.PendingReview))
            .ToListAsync(cancellationToken);
        return documents.Select(d => Item(d,
            versions.FirstOrDefault(v => v.DocumentId == d.Id && v.State == KnowledgeVersionState.Active),
            versions.Where(v => v.DocumentId == d.Id && v.State == KnowledgeVersionState.PendingReview).OrderBy(v => v.VersionNumber).ToList())).ToList();
    }

    private static KnowledgeDocumentItem Item(KnowledgeDocument d, KnowledgeDocumentVersion? active, IReadOnlyList<KnowledgeDocumentVersion> pending) =>
        new(d.Id, d.Title, d.Category, d.Source, d.StorePolicyKind, active is null ? null : Summary(active),
            [.. pending.Select(Summary)], d.LatestVersionNumber, d.CreatedAt, d.ModifiedAt);

    private static KnowledgeVersionSummary Summary(KnowledgeDocumentVersion v) =>
        new(v.Id, v.VersionNumber, v.State, v.CharacterCount, v.OriginalFileName, v.SubmittedByUserId, v.SubmittedAt, v.ReviewedByUserId, v.ReviewedAt, v.ReviewNote);

    private static AuditEventWrite Audit(string action, KnowledgeDocument document, KnowledgeDocumentVersion version) =>
        new(action, "knowledge_document", document.Id,
            Metadata: JsonSerializer.Serialize(new
            {
                versionId = version.Id,
                versionNumber = version.VersionNumber,
                category = document.Category.ToString(),
                source = document.Source.ToString(),
                characters = version.CharacterCount
            }));

    private static async Task<byte[]?> ReadLimitedAsync(Stream content, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private string TenantId() => tenantContext.RequireCurrent().TenantId;

    private string UserId() => tenantContext.RequireCurrent().UserId ?? "system";

    private static Result<T> NotFound<T>() =>
        AssistantProblemCodes.Problem<T>(AssistantProblemCodes.KnowledgeNotFound, "The knowledge document or version was not found in this workspace.", 404);

    private static Result<T> Invalid<T>(string detail) =>
        AssistantProblemCodes.Problem<T>(AssistantProblemCodes.KnowledgeInvalid, detail, 400);

    private static Result<T> InvalidTransition<T>(KnowledgeVersionState state, string action) =>
        AssistantProblemCodes.Problem<T>(AssistantProblemCodes.KnowledgeInvalidTransition, $"A version that is {state} cannot be {action}d.", 409);

    private static Result<T> TextProblem<T>(KnowledgeTextProblem problem) => AssistantProblemCodes.Problem<T>(problem switch
    {
        KnowledgeTextProblem.Empty => AssistantProblemCodes.KnowledgeEmpty,
        KnowledgeTextProblem.TooLong => AssistantProblemCodes.KnowledgeTooLong,
        KnowledgeTextProblem.TooLarge => AssistantProblemCodes.KnowledgeTooLarge,
        KnowledgeTextProblem.UnsupportedType => AssistantProblemCodes.KnowledgeUnsupportedType,
        _ => AssistantProblemCodes.KnowledgeInvalidEncoding
    }, KnowledgeText.Describe(problem), 422);
}
