using Asp.Versioning;
using Kreyora.Application.Assistant;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

/// <summary>Assistant settings, readiness, the approved knowledge library (M09-S02/S03) and the read-tool console (M09-S04). No AI is invoked here.</summary>
[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/assistant")]
public sealed class AssistantController(
    IAssistantPolicyService policies,
    IAssistantReadinessService readiness,
    IKnowledgeService knowledge,
    IKnowledgeRetrievalService retrieval,
    IKnowledgeIndexScheduler indexScheduler,
    ITenantContextAccessor tenantContext,
    IAssistantToolConsoleService tools,
    IAssistantTurnService turns,
    IAssistantTurnLogQuery turnLog) : ControllerBase
{
    private const long UploadRequestLimitBytes = KnowledgeText.MaxUploadBytes + 16 * 1024;

    [HttpGet("policy"), Authorize(Policy = TenantPermissions.AiConfigurationRead)]
    public async Task<ActionResult<AssistantPolicyItem>> GetPolicy(CancellationToken cancellationToken = default) =>
        this.ToActionResult(await policies.GetAsync(cancellationToken));

    [HttpPut("policy"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<AssistantPolicyItem>> UpdatePolicy([FromBody] UpdateAssistantPolicyRequest request, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await policies.UpdateAsync(request, cancellationToken));

    [HttpGet("readiness"), Authorize(Policy = TenantPermissions.AiConfigurationRead)]
    public async Task<ActionResult<AssistantReadinessItem>> GetReadiness(CancellationToken cancellationToken = default) =>
        this.ToActionResult(await readiness.GetAsync(cancellationToken));

    /// <summary>
    /// Owner playground (M09-S06): made-up customer messages run through the real assistant turn with seller-preview tools
    /// (dry run). Nothing is sent and nothing is held or created. Don't paste real customer messages.
    /// </summary>
    [HttpPost("playground"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<AssistantPlaygroundResult>> Playground([FromBody] AssistantPlaygroundRequest request, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await turns.PlaygroundAsync(request, cancellationToken));

    /// <summary>The redacted assistant turn log, newest first: outcomes, versions, model/tool/knowledge metadata, budgets. No message text.</summary>
    [HttpGet("turns"), Authorize(Policy = TenantPermissions.AiConfigurationRead)]
    public async Task<ActionResult<Kreyora.Application.Audit.CursorPage<AssistantTurnItem>>> ListTurns(
        [FromQuery] string? conversationId, [FromQuery] string? cursor, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Ok(await turnLog.ListAsync(conversationId, cursor, pageSize, cancellationToken));

    /// <summary>Usage for the last <paramref name="days"/> UTC days (1-30): turns by outcome, model calls, tokens, estimated cost. Aggregates only.</summary>
    [HttpGet("usage"), Authorize(Policy = TenantPermissions.AiConfigurationRead)]
    public async Task<ActionResult<AssistantUsageItem>> GetUsage([FromQuery] int days = 7, CancellationToken cancellationToken = default) =>
        Ok(await turnLog.UsageAsync(days, cancellationToken));

    /// <summary>The read-tool registry: names, versions, descriptions, schemas, and which are on in the policy.</summary>
    [HttpGet("tools"), Authorize(Policy = TenantPermissions.AiConfigurationRead)]
    public async Task<ActionResult<AssistantToolCatalog>> ListTools(CancellationToken cancellationToken = default) =>
        Ok(await tools.GetCatalogAsync(cancellationToken));

    /// <summary>Runs one read tool as a seller preview (shop context, no customer). Returns exactly what the model would see.</summary>
    [HttpPost("tools/{toolName}/preview"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<AssistantToolPreviewResult>> PreviewTool(string toolName, [FromBody] AssistantToolPreviewRequest request, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await tools.PreviewAsync(toolName, request, cancellationToken));

    [HttpGet("knowledge"), Authorize(Policy = TenantPermissions.AiConfigurationRead)]
    public async Task<ActionResult<IReadOnlyList<KnowledgeDocumentItem>>> ListKnowledge(CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.ListAsync(cancellationToken));

    [HttpPost("knowledge"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<KnowledgeDocumentItem>> CreateKnowledge(
        [FromBody] CreateKnowledgeFromTextRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.CreateFromTextAsync(request, idempotencyKey, cancellationToken));

    [HttpPost("knowledge/upload"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    [RequestSizeLimit(UploadRequestLimitBytes), RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimitBytes)]
    public async Task<ActionResult<KnowledgeDocumentItem>> UploadKnowledge(
        IFormFile file,
        [FromForm] string? title,
        [FromForm] KnowledgeCategory? category,
        [FromForm] string? documentId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var stream = file.OpenReadStream();
        return this.ToActionResult(await knowledge.UploadAsync(new KnowledgeUpload(file.FileName, stream, title, category, documentId), idempotencyKey, cancellationToken));
    }

    /// <summary>Test console: which approved passages would the assistant see for this question (ADR-019)?</summary>
    [HttpPost("knowledge/search"), Authorize(Policy = TenantPermissions.AiConfigurationRead), ValidateAntiForgeryToken]
    public async Task<ActionResult<KnowledgeRetrievalResult>> Search([FromBody] KnowledgeSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await retrieval.RetrieveAsync(request.Query, cancellationToken));
    }

    /// <summary>Queues re-embedding of this workspace's knowledge (e.g. after a model change).</summary>
    [HttpPost("knowledge/reindex"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public ActionResult Reindex()
    {
        indexScheduler.ScheduleTenantReindex(tenantContext.RequireCurrent().TenantId);
        return Accepted();
    }

    [HttpPost("knowledge/import-store-policies"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<IReadOnlyList<KnowledgeDocumentItem>>> ImportStorePolicies(CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.ImportStorePoliciesAsync(cancellationToken));

    [HttpPost("knowledge/{documentId}/versions"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<KnowledgeDocumentItem>> CreateVersion(
        string documentId,
        [FromBody] CreateKnowledgeVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.CreateVersionAsync(documentId, request, idempotencyKey, cancellationToken));

    [HttpGet("knowledge/{documentId}/versions/{versionId}"), Authorize(Policy = TenantPermissions.AiConfigurationRead)]
    public async Task<ActionResult<KnowledgeVersionDetail>> GetVersion(string documentId, string versionId, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.GetVersionAsync(documentId, versionId, cancellationToken));

    [HttpPost("knowledge/{documentId}/versions/{versionId}/approve"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<KnowledgeDocumentItem>> Approve(string documentId, string versionId, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.ApproveAsync(documentId, versionId, cancellationToken));

    [HttpPost("knowledge/{documentId}/versions/{versionId}/reject"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<KnowledgeDocumentItem>> Reject(
        string documentId,
        string versionId,
        [FromBody] RejectKnowledgeVersionRequest request,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.RejectAsync(documentId, versionId, request, cancellationToken));

    [HttpDelete("knowledge/{documentId}"), Authorize(Policy = TenantPermissions.AiConfigurationWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<KnowledgeDocumentItem>> Delete(string documentId, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await knowledge.DeleteAsync(documentId, cancellationToken));
}
