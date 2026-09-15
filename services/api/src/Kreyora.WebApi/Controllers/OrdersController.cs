using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Models;
using Kreyora.Application.Orders;
using Kreyora.Domain.Orders;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/orders")]
public sealed class OrdersController(
    IOrderQueryService orderQueryService,
    IOrderOperationService orderOperationService) : ControllerBase
{
    [HttpGet, Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<PagedResult<OrderSummaryItem>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] PaymentStatus? paymentStatus = null,
        [FromQuery] FulfilmentStatus? fulfilmentStatus = null,
        [FromQuery] OrderPaymentMethod? paymentMethod = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await orderQueryService.ListOrdersAsync(
            new OrderQuery(page, pageSize, status, paymentStatus, fulfilmentStatus, paymentMethod, source, search),
            cancellationToken));

    [HttpGet("{id}"), Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<OrderDetailItem>> Get(
        string id,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await orderQueryService.GetOrderDetailAsync(id, cancellationToken));

    [HttpGet("{id}/actions"), Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<IReadOnlyList<OrderActionEvaluation>>> GetAllowedActions(
        string id,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await orderOperationService.GetAllowedActionsAsync(id, cancellationToken));

    [HttpPost("{id}/actions"), Authorize(Policy = TenantPermissions.OrdersRead), ValidateAntiForgeryToken]
    public async Task<ActionResult<OrderOperationResult>> ExecuteAction(
        string id,
        [FromBody] ExecuteOrderActionBody body,
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
                Detail = "An Idempotency-Key header is required to execute an order action."
            });
        }

        ArgumentNullException.ThrowIfNull(body);

        var request = new ExecuteOrderActionRequest(
            OrderId: id,
            Action: body.Action,
            Reason: body.Reason,
            ExpectedVersion: body.ExpectedVersion,
            IdempotencyKey: idempotencyKey,
            PaymentAttemptId: body.PaymentAttemptId,
            ProviderReference: body.ProviderReference);

        var result = await orderOperationService.ExecuteActionAsync(request, cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpGet("{id}/activity"), Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<IReadOnlyList<OrderActivityItem>>> GetActivity(
        string id,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await orderQueryService.GetOrderActivityAsync(id, cancellationToken));

    [HttpGet("{id}/notifications"), Authorize(Policy = TenantPermissions.OrdersRead)]
    public async Task<ActionResult<IReadOnlyList<OrderNotificationItem>>> GetNotifications(
        string id,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await orderQueryService.GetOrderNotificationsAsync(id, cancellationToken));
}

public sealed record ExecuteOrderActionBody(
    OrderAction Action,
    string? Reason,
    uint ExpectedVersion,
    string? PaymentAttemptId = null,
    string? ProviderReference = null);

