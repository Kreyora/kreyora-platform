using Asp.Versioning;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[Authorize(Policy = TenantPermissions.AuditRead)]
[RequireTenantContext]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/audit-events")]
public sealed class AuditEventsController(IAuditEventService auditEvents, ITenantContextAccessor tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CursorPage<AuditEventItem>>> Get([FromQuery] string? cursor, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var page = await auditEvents.GetPageAsync(cursor, pageSize, cancellationToken);
        if (tenantContext.RequireCurrent().IsReadOnlySupport)
        {
            await auditEvents.AppendAsync(new AuditEventWrite("support.audit-history.viewed", "audit-event-history", "tenant"), cancellationToken);
        }
        return Ok(page);
    }
}
