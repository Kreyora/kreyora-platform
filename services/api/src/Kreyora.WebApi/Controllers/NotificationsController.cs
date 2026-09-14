using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Models;
using Kreyora.Application.Notifications;
using Kreyora.Domain.Notifications;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/notifications")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet, Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<PagedResult<NotificationSummary>>> GetNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] NotificationStatus? status = null,
        [FromQuery] NotificationChannel? channel = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await notificationService.GetNotificationsAsync(
            new NotificationQuery(page, pageSize, status, channel), cancellationToken));

    [HttpGet("dead-letter"), Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<PagedResult<NotificationSummary>>> GetDeadLetter(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await notificationService.GetDeadLetterAsync(
            new NotificationDeadLetterQuery(page, pageSize), cancellationToken));

    [HttpGet("{id}"), Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<NotificationDetail>> GetNotification(
        string id, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await notificationService.GetNotificationAsync(id, cancellationToken));

    [HttpPost("{id}/replay"), Authorize(Policy = TenantPermissions.OrdersWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<NotificationDetail>> Replay(
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
                Detail = "An Idempotency-Key header is required to replay a notification."
            });
        }

        return this.ToActionResult(await notificationService.ReplayAsync(
            new ReplayNotificationRequest(id, idempotencyKey), cancellationToken));
    }
}

