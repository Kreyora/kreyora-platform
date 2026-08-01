using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Support;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[Authorize(Policy = TenantPermissions.SupportGrantManage)]
[RequireTenantContext]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/support-access-grants")]
public sealed class SupportAccessGrantsController(ISupportAccessGrantService grants) : ControllerBase
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<SupportAccessGrantSummary>> Create([FromBody] CreateSupportAccessGrantRequest request, CancellationToken cancellationToken)
    {
        var grant = await grants.CreateAsync(request, cancellationToken);
        return Created($"/v1/support-access-grants/{grant.Id}", grant);
    }

    [HttpDelete("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(string id, CancellationToken cancellationToken)
    {
        await grants.RevokeAsync(id, cancellationToken);
        return NoContent();
    }
}
