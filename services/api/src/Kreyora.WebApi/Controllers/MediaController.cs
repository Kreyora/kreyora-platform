using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/media")]
public sealed class MediaController(IMediaAssetService media) : ControllerBase
{
    [HttpPost("initiate"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<MediaAssetItem>> Initiate(InitiateMediaUploadRequest request, CancellationToken cancellationToken) => this.ToActionResult(await media.InitiateUploadAsync(request, cancellationToken));
    [HttpPost("{id}/complete"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<MediaAssetItem>> Complete(string id, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return this.ToActionResult(await media.CompleteUploadAsync(new CompleteMediaUploadRequest(id), stream, cancellationToken));
    }
    [HttpPost("{id}/attach"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<MediaAssetItem>> Attach(string id, AttachBody body, CancellationToken cancellationToken) => this.ToActionResult(await media.AttachToProductAsync(new AttachMediaToProductRequest(id, body.ProductId, body.SortOrder, body.AltText), cancellationToken));
    [HttpPut("{id}/order"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<MediaAssetItem>> Reorder(string id, ReorderBody body, CancellationToken cancellationToken) => this.ToActionResult(await media.ReorderAsync(new ReorderMediaRequest(id, body.SortOrder, body.AltText), cancellationToken));
    [HttpDelete("{id}"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<MediaAssetItem>> Delete(string id, CancellationToken cancellationToken) => this.ToActionResult(await media.RequestDeletionAsync(id, cancellationToken));
    [HttpGet("products/{productId}"), Authorize(Policy = TenantPermissions.CatalogRead)]
    public async Task<ActionResult<IReadOnlyList<MediaAssetItem>>> List(string productId, CancellationToken cancellationToken) => this.ToActionResult(await media.ListForProductAsync(productId, cancellationToken));
    [HttpGet("{id}/content"), Authorize(Policy = TenantPermissions.CatalogRead)]
    public async Task<IActionResult> Content(string id, CancellationToken cancellationToken)
    {
        var result = await media.OpenReadAsync(id, cancellationToken);
        if (result.IsSuccess) return File(result.Value!.Content, result.Value.ContentType, enableRangeProcessing: false);
        var error = result.Error!;
        return StatusCode(error.Status, new ProblemDetails { Type = error.Type, Title = error.Title, Status = error.Status, Detail = error.Detail });
    }
}

public sealed record AttachBody(string ProductId, int SortOrder, string? AltText);
public sealed record ReorderBody(int SortOrder, string? AltText);
