using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[Authorize(Policy = TenantPermissions.PermissionsRead)]
[RequireTenantContext]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/permissions")]
public sealed class PermissionsController(ITenantContextAccessor tenantContext) : ControllerBase
{
    [HttpGet]
    public ActionResult<EffectivePermissionsResponse> Get()
    {
        var context = tenantContext.RequireCurrent();
        return Ok(new EffectivePermissionsResponse(context.TenantId, context.Role?.ToString(), context.IsReadOnlySupport, TenantPermissions.For(context)));
    }
}

public sealed record EffectivePermissionsResponse(string TenantId, string? Role, bool IsReadOnlySupport, IReadOnlyList<string> Permissions);
