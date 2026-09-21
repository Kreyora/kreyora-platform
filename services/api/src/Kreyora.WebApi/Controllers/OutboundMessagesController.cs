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
[Route("v{version:apiVersion}/integrations/messages")]
public sealed class OutboundMessagesController(
    IOutboundMessageService messageService) : ControllerBase
{
    [HttpPost, Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<OutboundMessageResult>> QueueMessage(
        [FromBody] QueueOutboundMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await messageService.QueueMessageAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Title = "Message Queueing Failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = result.ErrorMessage
            });
        }

        if (result.IsIdempotentDuplicate)
        {
            return Ok(result);
        }

        return CreatedAtAction(nameof(GetMessage), new { id = result.OutboundMessageId }, result);
    }

    [HttpGet("{id}"), Authorize(Policy = TenantPermissions.IntegrationsRead)]
    public async Task<ActionResult<OutboundMessageDto>> GetMessage(
        string id,
        CancellationToken cancellationToken = default)
    {
        var message = await messageService.GetMessageAsync(id, cancellationToken);
        if (message is null)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Title = "Message Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Outbound message '{id}' was not found."
            });
        }

        return Ok(message);
    }

    [HttpGet("{id}/attempts"), Authorize(Policy = TenantPermissions.IntegrationsRead)]
    public async Task<ActionResult<IReadOnlyList<OutboundDeliveryAttemptDto>>> GetDeliveryAttempts(
        string id,
        CancellationToken cancellationToken = default)
    {
        var attempts = await messageService.GetDeliveryAttemptsAsync(id, cancellationToken);
        return Ok(attempts);
    }

    [HttpGet, Authorize(Policy = TenantPermissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResult<OutboundMessageDto>>> GetMessages(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OutboundMessageStatus? status = null,
        [FromQuery] ChannelType? channel = null,
        [FromQuery] string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await messageService.GetMessagesAsync(
            new OutboundMessageQuery(page, pageSize, status, channel, connectionId),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{id}/cancel"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<OutboundMessageResult>> CancelMessage(
        string id,
        CancellationToken cancellationToken = default)
    {
        var result = await messageService.CancelMessageAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Title = "Message Cancellation Failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = result.ErrorMessage
            });
        }

        return Ok(result);
    }

    [HttpPost("{id}/replay"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<OutboundMessageResult>> ReplayMessage(
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
                Detail = "An Idempotency-Key header is required to replay an outbound message."
            });
        }

        var result = await messageService.ReplayMessageAsync(id, idempotencyKey, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Title = "Message Replay Failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = result.ErrorMessage
            });
        }

        return Ok(result);
    }

    [HttpGet("dead-letter"), Authorize(Policy = TenantPermissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResult<OutboundDeadLetterDto>>> GetDeadLetter(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ChannelType? channel = null,
        CancellationToken cancellationToken = default)
    {
        var result = await messageService.GetDeadLetterMessagesAsync(
            new OutboundDeadLetterQuery(page, pageSize, channel),
            cancellationToken);

        return Ok(result);
    }
}

