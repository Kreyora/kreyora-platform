using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Storefront;
using Kreyora.Domain.Storefront;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, Authorize(Policy = TenantPermissions.StorefrontRead), RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/store")]
public sealed class StorefrontAdministrationController(IStorefrontAdministrationService stores) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StorefrontStore>> Get(CancellationToken cancellationToken) => this.ToActionResult(await stores.GetStoreAsync(cancellationToken));

    [HttpPost, Authorize(Policy = TenantPermissions.StorefrontWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<StorefrontStore>> Create(StoreSettingsInput settings, CancellationToken cancellationToken)
    {
        var result = await stores.CreateStoreAsync(new CreateStoreRequest(settings, Request.Headers.IdempotencyKey()), cancellationToken);
        return result.IsSuccess ? Created("/v1/store", result.Value) : this.ToActionResult(result);
    }

    [HttpPut, Authorize(Policy = TenantPermissions.StorefrontWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<StorefrontStore>> Update(UpdateStoreBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await stores.UpdateStoreAsync(new UpdateStoreRequest(body.Settings, body.ExpectedVersion), cancellationToken));

    [HttpGet("readiness")]
    public async Task<ActionResult<StoreReadiness>> Readiness(CancellationToken cancellationToken) => this.ToActionResult(await stores.GetReadinessAsync(cancellationToken));

    [HttpPost("activate"), Authorize(Policy = TenantPermissions.StorefrontWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<StorefrontStore>> Activate(VersionBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await stores.ActivateStoreAsync(new ActivateStoreRequest(body.ExpectedVersion, Request.Headers.IdempotencyKey()), cancellationToken));

    [HttpGet("publications")]
    public async Task<ActionResult<StorePublicationPage>> ListPublications([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await stores.ListPublicationsAsync(new StorePublicationQuery(page, pageSize), cancellationToken));

    [HttpPut("publications/{productId}"), Authorize(Policy = TenantPermissions.StorefrontWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<StoreProductPublicationItem>> SetPublication(string productId, StorePublicationBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await stores.SetProductVisibilityAsync(new SetStoreProductVisibilityRequest(productId, body.Visibility, body.ExpectedVersion, Request.Headers.IdempotencyKey()), cancellationToken));
}

public sealed record UpdateStoreBody(StoreSettingsInput Settings, uint ExpectedVersion);
public sealed record StorePublicationBody(StoreProductVisibility Visibility, uint ExpectedVersion);

internal static class StorefrontRequestHeaders
{
    public static string IdempotencyKey(this IHeaderDictionary headers) => headers.TryGetValue("Idempotency-Key", out var key) ? key.ToString() : string.Empty;
}
