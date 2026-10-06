using Hangfire;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Catalog;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant;

/// <summary>Embeds approved chunks and keeps the index clean (ADR-019 §6). Runs in the current tenant's context.</summary>
public sealed class KnowledgeIndexingService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAiEmbeddingClient embeddings,
    IPrivateObjectStorage storage,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider) : IKnowledgeIndexingService
{
    public const int MaxChunksPerRun = 500;

    /// <summary>The model/size vectors must have to count as indexed under the current configuration.</summary>
    public static (string Model, int Dimensions) ExpectedEmbedding(AiOptions options) =>
        options.Mode == AiMode.Fake ? (FakeEmbeddingClient.ModelName, FakeEmbeddingClient.Dimensions) : (options.Embeddings.Model, options.Embeddings.Dimensions);

    public async Task<int> IndexVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var chunks = await (
            from chunk in dbContext.KnowledgeChunks
            join version in dbContext.KnowledgeDocumentVersions on chunk.VersionId equals version.Id
            where chunk.TenantId == tenantId && version.TenantId == tenantId && chunk.VersionId == versionId && version.State == KnowledgeVersionState.Active
            orderby chunk.ChunkIndex
            select chunk).ToListAsync(cancellationToken);
        return await EmbedAsync(chunks, cancellationToken);
    }

    public async Task<KnowledgeIndexMaintenance> MaintainAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;

        // 1. Orphans: chunks whose version is no longer the document's Active version (defense in depth; retrieval ignores them anyway).
        var orphans = await (
            from chunk in dbContext.KnowledgeChunks
            join version in dbContext.KnowledgeDocumentVersions on chunk.VersionId equals version.Id
            join document in dbContext.KnowledgeDocuments on version.DocumentId equals document.Id
            where chunk.TenantId == tenantId
                && (version.State != KnowledgeVersionState.Active || document.DeletedAt != null || document.ActiveVersionId != version.Id)
            select chunk).ToListAsync(cancellationToken);
        dbContext.KnowledgeChunks.RemoveRange(orphans);
        await dbContext.SaveChangesAsync(cancellationToken);

        // 2. Missing or outdated vectors on active chunks.
        var (model, dimensions) = ExpectedEmbedding(aiOptions.CurrentValue);
        var pending = await dbContext.KnowledgeChunks
            .Where(c => c.TenantId == tenantId && (c.Embedding == null || c.EmbeddingModel != model || c.EmbeddingDimensions != dimensions))
            .OrderBy(c => c.VersionId).ThenBy(c => c.ChunkIndex)
            .Take(MaxChunksPerRun)
            .ToListAsync(cancellationToken);
        var embedded = await EmbedAsync(pending, cancellationToken);

        // 3. Original files whose purge failed earlier.
        var purges = await dbContext.KnowledgeDocumentVersions
            .Where(v => v.TenantId == tenantId && v.State == KnowledgeVersionState.Deleted && v.OriginalObjectKey != null)
            .ToListAsync(cancellationToken);
        var purged = 0;
        foreach (var version in purges)
        {
            try
            {
                await storage.DeleteIfExistsAsync(version.OriginalObjectKey!, cancellationToken);
                version.ConfirmOriginalPurged();
                purged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // stays pending for the next run
            }
        }

        if (purged > 0) await dbContext.SaveChangesAsync(cancellationToken);
        return new KnowledgeIndexMaintenance(embedded, orphans.Count, purged, pending.Count == 0 || embedded > 0);
    }

    public async Task<KnowledgeIndexMaintenance> ReindexAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        await dbContext.KnowledgeChunks.Where(c => c.TenantId == tenantId)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Embedding, (float[]?)null).SetProperty(c => c.IndexedAt, (DateTimeOffset?)null), cancellationToken);
        return await MaintainAsync(cancellationToken);
    }

    private async Task<int> EmbedAsync(List<KnowledgeChunk> chunks, CancellationToken cancellationToken)
    {
        var (model, dimensions) = ExpectedEmbedding(aiOptions.CurrentValue);
        var todo = chunks.Where(c => c.NeedsEmbedding(model, dimensions)).ToList();
        if (todo.Count == 0) return 0;

        var result = await embeddings.EmbedAsync([.. todo.Select(c => c.Text)], AiEmbeddingPurpose.Document, cancellationToken);
        if (!result.IsSuccess)
        {
            return 0; // AI disabled or provider failure: retrieval stays in lexical fallback; the sweeper retries
        }

        var now = timeProvider.UtcNow;
        for (var i = 0; i < todo.Count; i++)
        {
            todo[i].SetEmbedding(result.Vectors[i], result.Model!, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return todo.Count;
    }
}

/// <summary>Hangfire entry points: immediate per-version indexing, per-tenant reindex, and the recurring sweeper.</summary>
public sealed partial class KnowledgeIndexingJob(IServiceScopeFactory scopeFactory, ILogger<KnowledgeIndexingJob> logger)
{
    public const string RecurringJobId = "knowledge-indexing";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge indexing failed for tenant {TenantId}")]
    private static partial void LogFailure(ILogger logger, Exception ex, string tenantId);

    public static void RegisterRecurring(IRecurringJobManager manager) =>
        manager.AddOrUpdate<KnowledgeIndexingJob>(RecurringJobId, job => job.SweepAsync(), "*/5 * * * *");

    public Task IndexVersionAsync(string tenantId, string versionId) =>
        RunForTenantAsync(tenantId, "knowledge-index-version", (service, ct) => service.IndexVersionAsync(versionId, ct));

    public Task ReindexTenantAsync(string tenantId) =>
        RunForTenantAsync(tenantId, "knowledge-reindex", (service, ct) => service.ReindexAsync(ct));

    [DisableConcurrentExecution(timeoutInSeconds: 280)]
    public async Task SweepAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var tenants = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync();
        foreach (var tenantId in tenants)
        {
            await RunForTenantAsync(tenantId, "knowledge-maintain", (service, ct) => service.MaintainAsync(ct));
        }
    }

    private async Task RunForTenantAsync<T>(string tenantId, string jobName, Func<IKnowledgeIndexingService, CancellationToken, Task<T>> work)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        try
        {
            await services.GetRequiredService<ITenantJobRunner>().RunAsync(new TenantJobEnvelope(tenantId, jobName, "{}"), async cancellationToken =>
            {
                using var tenantScope = services.GetRequiredService<ITenantContextAccessor>().BeginScope(new TenantContext(tenantId, null, null, null));
                await work(services.GetRequiredService<IKnowledgeIndexingService>(), cancellationToken);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(logger, ex, tenantId);
        }
    }
}

/// <summary>Queues indexing after commit; a no-op without Hangfire, and never throws into the request.</summary>
public sealed partial class HangfireKnowledgeIndexScheduler(ILogger<HangfireKnowledgeIndexScheduler> logger, IBackgroundJobClient? jobs = null) : IKnowledgeIndexScheduler
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not queue knowledge indexing; the sweeper will pick it up")]
    private static partial void LogQueueFailure(ILogger logger, Exception ex);

    public void ScheduleVersionIndexing(string tenantId, string versionId) =>
        Try(() => jobs?.Enqueue<KnowledgeIndexingJob>(job => job.IndexVersionAsync(tenantId, versionId)));

    public void ScheduleTenantReindex(string tenantId) =>
        Try(() => jobs?.Enqueue<KnowledgeIndexingJob>(job => job.ReindexTenantAsync(tenantId)));

    private void Try(Action enqueue)
    {
        try
        {
            enqueue();
        }
        catch (Exception ex)
        {
            LogQueueFailure(logger, ex);
        }
    }
}
