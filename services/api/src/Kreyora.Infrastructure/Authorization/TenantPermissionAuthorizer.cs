using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Kreyora.Infrastructure.Authorization;

public sealed class TenantPermissionAuthorizer(ITenantContextAccessor tenantContext) : ITenantPermissionAuthorizer
{
    public bool IsAllowed(TenantContext context, string permission) => TenantPermissions.IsAllowed(context, permission);

    public void Demand(string permission)
    {
        var context = tenantContext.RequireCurrent();
        if (!IsAllowed(context, permission))
        {
            throw new UnauthorizedAccessException("The current tenant role is not permitted to perform this operation.");
        }
    }
}

public sealed record TenantPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class TenantPermissionHandler(
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer) : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
    {
        var current = tenantContext.Current;
        if (current is not null && authorizer.IsAllowed(current, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
