using System.Security.Claims;
using Asp.Versioning;
using Kreyora.Application.Tenancy;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[Authorize]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/workspaces")]
public sealed class WorkspacesController(
    ITenantContextResolutionService resolver,
    ITenantContextAccessor tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkspaceSummary>>> GetActiveWorkspaces(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? Unauthorized()
            : Ok(await resolver.GetActiveWorkspacesAsync(userId, cancellationToken));
    }

    [HttpGet("current")]
    [RequireTenantContext]
    public async Task<ActionResult<WorkspaceSummary>> GetCurrentWorkspace(CancellationToken cancellationToken)
    {
        var current = tenantContext.RequireCurrent();
        var workspaces = await resolver.GetActiveWorkspacesAsync(current.UserId!, cancellationToken);
        var workspace = workspaces.Single(workspace => workspace.TenantId == current.TenantId);
        return Ok(workspace);
    }
}
