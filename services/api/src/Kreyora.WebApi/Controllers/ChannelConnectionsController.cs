using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/integrations/connections")]
public sealed class ChannelConnectionsController(IChannelConnectionService connectionService) : ControllerBase
{
    [HttpGet, Authorize(Policy = TenantPermissions.IntegrationsRead)]
    public async Task<ActionResult<IReadOnlyList<ChannelConnectionDto>>> GetConnections(
        [FromQuery] string? storeId,
        [FromQuery] ChannelType? channel,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.GetConnectionsAsync(storeId, channel, cancellationToken));

    [HttpGet("{id}"), Authorize(Policy = TenantPermissions.IntegrationsRead)]
    public async Task<ActionResult<ChannelConnectionDto>> GetConnectionById(
        string id,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.GetConnectionByIdAsync(id, cancellationToken));

    [HttpPost, Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ChannelConnectionDto>> CreateConnection(
        [FromBody] CreateChannelConnectionRequest request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.CreateConnectionAsync(request, cancellationToken));

    [HttpPut("{id}"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ChannelConnectionDto>> UpdateConnection(
        string id,
        [FromBody] UpdateChannelConnectionRequest request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.UpdateConnectionAsync(id, request, cancellationToken));

    [HttpPost("{id}/rotate-secret"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ChannelConnectionDto>> RotateSecret(
        string id,
        [FromBody] RotateSecretRequestBody body,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.RotateConnectionSecretsAsync(id, body.TargetKeyVersion, cancellationToken));

    [HttpPost("{id}/disable"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ChannelConnectionDto>> Disable(
        string id,
        [FromBody] DisableConnectionRequestBody? body,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.DisableConnectionAsync(id, body?.Reason, cancellationToken));

    [HttpPost("{id}/enable"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<ChannelConnectionDto>> Enable(
        string id,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.EnableConnectionAsync(id, cancellationToken));

    [HttpDelete("{id}"), Authorize(Policy = TenantPermissions.IntegrationsWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<bool>> Delete(
        string id,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.DeleteConnectionAsync(id, cancellationToken));

    [HttpPost("{id}/health"), Authorize(Policy = TenantPermissions.IntegrationsRead), ValidateAntiForgeryToken]
    public async Task<ActionResult<ConnectionHealthResult>> CheckHealth(
        string id,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await connectionService.CheckHealthAsync(id, cancellationToken));
}

public sealed record RotateSecretRequestBody(string TargetKeyVersion);

public sealed record DisableConnectionRequestBody(string? Reason = null);

