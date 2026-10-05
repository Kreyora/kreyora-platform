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
    IConversationInboxService conversationInboxService) : ControllerBase
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
}
