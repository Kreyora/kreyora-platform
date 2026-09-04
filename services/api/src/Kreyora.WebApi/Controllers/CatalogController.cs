using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, Authorize(Policy = TenantPermissions.CatalogRead), RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/catalog/products")]
public sealed class CatalogController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CatalogProductPage>> List(
        [FromQuery] string? search,
        [FromQuery] Kreyora.Domain.Catalog.ProductPublishState? publishState,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) => this.ToActionResult(await catalog.ListProductsAsync(new CatalogProductQuery(search, publishState, cursor, pageSize), cancellationToken));
    [HttpGet("{id}")] public async Task<ActionResult<CatalogProduct>> Get(string id, CancellationToken cancellationToken) => this.ToActionResult(await catalog.GetProductAsync(id, cancellationToken));
    [HttpPost, Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<CatalogProduct>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.CreateProductAsync(request, cancellationToken);
        return result.IsSuccess ? Created($"/v1/catalog/products/{result.Value!.Id}", result.Value) : this.ToActionResult(result);
    }
    [HttpPut("{id}"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<CatalogProduct>> Update(string id, UpdateProductBody body, CancellationToken cancellationToken) => this.ToActionResult(await catalog.UpdateProductAsync(new UpdateProductRequest(id, body.Title, body.Description, body.Slug, body.ExpectedVersion), cancellationToken));
    [HttpPost("{id}/variants"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<CatalogProduct>> AddVariant(string id, AddVariantBody body, CancellationToken cancellationToken) => this.ToActionResult(await catalog.AddVariantAsync(new AddProductVariantRequest(id, body.Sku, body.Name, body.Options, body.PriceNpr, body.CompareAtPriceNpr, body.IsPublished, body.ExpectedVersion), cancellationToken));
    [HttpPut("{id}/variants/{variantId}"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<CatalogProduct>> UpdateVariant(string id, string variantId, AddVariantBody body, CancellationToken cancellationToken) => this.ToActionResult(await catalog.UpdateVariantAsync(new UpdateProductVariantRequest(id, variantId, body.Sku, body.Name, body.Options, body.PriceNpr, body.CompareAtPriceNpr, body.IsPublished, body.ExpectedVersion), cancellationToken));
    [HttpPost("{id}/publication"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<CatalogProduct>> Publication(string id, PublicationBody body, CancellationToken cancellationToken) => this.ToActionResult(await catalog.ChangePublicationStateAsync(new ChangeProductPublicationStateRequest(id, body.State, body.ExpectedVersion), cancellationToken));
    [HttpPost("{id}/archive"), Authorize(Policy = TenantPermissions.CatalogWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<CatalogProduct>> Archive(string id, VersionBody body, CancellationToken cancellationToken) => this.ToActionResult(await catalog.ArchiveProductAsync(new ArchiveProductRequest(id, body.ExpectedVersion), cancellationToken));
}

public sealed record UpdateProductBody(string Title, string? Description, string Slug, uint ExpectedVersion);
public sealed record AddVariantBody(string Sku, string Name, IReadOnlyDictionary<string, string>? Options, decimal PriceNpr, decimal? CompareAtPriceNpr, bool IsPublished, uint ExpectedVersion);
public sealed record PublicationBody(Kreyora.Domain.Catalog.ProductPublishState State, uint ExpectedVersion);
public sealed record VersionBody(uint ExpectedVersion);
