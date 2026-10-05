using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Conversations;
using Kreyora.Application.Models;
using Kreyora.Domain.Conversations;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/conversations")]
public sealed class ConversationsController(
    IConversationQueryService conversationQueryService,
    IConversationInboxService conversationInboxService,
    IConversationReplyService conversationReplyService) : ControllerBase
{
    [HttpGet, Authorize(Policy = TenantPermissions.ConversationsRead)]
    public async Task<ActionResult<PagedResult<ConversationSummaryItem>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ConversationStatus? status = null,
        [FromQuery] string? connectionId = null,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] string? assignedTo = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await conversationQueryService.ListConversationsAsync(
            new ConversationQuery(page, pageSize, status, connectionId, unreadOnly, assignedTo),
            cancellationToken));

    [HttpGet("{id}"), Authorize(Policy = TenantPermissions.ConversationsRead)]
    public async Task<ActionResult<ConversationDetailItem>> Get(
        string id,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await conversationQueryService.GetConversationAsync(id, cancellationToken));

    [HttpGet("{id}/messages"), Authorize(Policy = TenantPermissions.ConversationsRead)]
    public async Task<ActionResult<MessagePage>> GetMessages(
        string id,
        [FromQuery] string? before = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await conversationQueryService.GetMessagesAsync(id, before, pageSize, cancellationToken));

    [HttpPost("{id}/read"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConversationDetailItem>> MarkRead(
        string id,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await conversationInboxService.MarkReadAsync(id, cancellationToken));

    [HttpPost("{id}/replies"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<MessageItem>> Reply(
        string id,
        [FromBody] StaffReplyRequest body,
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
                Detail = "An Idempotency-Key header is required to send a reply."
            });
        }

        ArgumentNullException.ThrowIfNull(body);
        return this.ToActionResult(await conversationReplyService.SendStaffReplyAsync(id, body.Text, idempotencyKey, cancellationToken));
    }

    [HttpPost("{id}/takeover"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConversationDetailItem>> TakeOver(string id, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await conversationInboxService.TakeOverAsync(id, cancellationToken));

    [HttpPost("{id}/release"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConversationDetailItem>> Release(string id, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await conversationInboxService.ReleaseAsync(id, cancellationToken));

    [HttpPost("{id}/assign"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConversationDetailItem>> Assign(
        string id,
        [FromBody] AssignConversationRequest body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        return this.ToActionResult(await conversationInboxService.AssignAsync(id, body.UserId, cancellationToken));
    }

    [HttpPost("{id}/unassign"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConversationDetailItem>> Unassign(string id, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await conversationInboxService.UnassignAsync(id, cancellationToken));

    [HttpPut("{id}/labels"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConversationDetailItem>> SetLabels(
        string id,
        [FromBody] SetConversationLabelsRequest body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        return this.ToActionResult(await conversationInboxService.SetLabelsAsync(id, body.Labels, cancellationToken));
    }

    [HttpPost("{id}/status"), Authorize(Policy = TenantPermissions.ConversationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConversationDetailItem>> ChangeStatus(
        string id,
        [FromBody] ChangeConversationStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        return this.ToActionResult(await conversationInboxService.ChangeStatusAsync(id, body.Action, cancellationToken));
    }
}
