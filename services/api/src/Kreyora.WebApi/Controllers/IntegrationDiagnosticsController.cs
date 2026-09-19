using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Domain.Integrations;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[RequireTenantContext]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/integrations/webhooks")]
public sealed class IntegrationDiagnosticsController(
    IWebhookProcessingService processingService) : ControllerBase
{
    [HttpGet("dead-letter"), Authorize(Policy = TenantPermissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResult<WebhookDeadLetterDto>>> GetDeadLetter(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ChannelType? channel = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await processingService.GetDeadLetterEventsAsync(
            new WebhookDeadLetterQuery(page, pageSize, channel), cancellationToken));

    [HttpPost("{id}/replay"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<WebhookReplayResult>> Replay(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Title = "Validation Error",
                Status = StatusCodes.Status400BadRequest,
                Detail = "An Idempotency-Key header is required to replay a webhook event."
            });
        }

        return this.ToActionResult(await processingService.ReplayWebhookEventAsync(
            id, idempotencyKey, cancellationToken));
    }
}
