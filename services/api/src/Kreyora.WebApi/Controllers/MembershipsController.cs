using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[Authorize(Policy = TenantPermissions.MembershipManage)]
[RequireTenantContext]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/memberships")]
public sealed class MembershipsController(ITenantMembershipService memberships) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MembershipSummary>>> Get(CancellationToken cancellationToken) =>
        Ok(await memberships.GetMembersAsync(cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<MembershipSummary>> Grant(GrantMembershipRequest request, CancellationToken cancellationToken)
    {
        var membership = await memberships.GrantMembershipByEmailAsync(request.Email, request.Role, cancellationToken);
        var items = await memberships.GetMembersAsync(cancellationToken);
        return Created($"/v1/memberships/{membership.Id}", items.Single(item => item.Id == membership.Id));
    }

    [HttpPatch("{id}/role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(string id, ChangeMembershipRoleRequest request, CancellationToken cancellationToken)
    {
        await memberships.ChangeMembershipRoleAsync(id, request.Role, cancellationToken); return NoContent();
    }

    [HttpPost("{id}/suspend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(string id, CancellationToken cancellationToken)
    { await memberships.SuspendMembershipAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id}/reactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(string id, CancellationToken cancellationToken)
    { await memberships.ReactivateMembershipAsync(id, cancellationToken); return NoContent(); }

    [HttpDelete("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(string id, CancellationToken cancellationToken)
    { await memberships.RevokeMembershipAsync(id, cancellationToken); return NoContent(); }
}

public sealed record GrantMembershipRequest(string Email, TenantRole Role);
public sealed record ChangeMembershipRoleRequest(TenantRole Role);
