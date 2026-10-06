using Asp.Versioning;
using Kreyora.Application.Assistant;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

/// <summary>Assistant settings, readiness and the approved knowledge library (M09-S02). No AI is invoked here.</summary>
[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/assistant")]
public sealed class AssistantController(
    IAssistantPolicyService policies,
    IAssistantReadinessService readiness,
    IKnowledgeService knowledge,
    IKnowledgeRetrievalService retrieval,
    IKnowledgeIndexScheduler indexScheduler,
    ITenantContextAccessor tenantContext) : ControllerBase
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
