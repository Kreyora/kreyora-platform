using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Storefront;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, Authorize(Policy = TenantPermissions.StorefrontRead), RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/store/delivery-rules")]
public sealed class DeliveryRulesController(IDeliveryRuleService deliveryRules) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DeliveryRulePage>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await deliveryRules.ListAsync(new DeliveryRuleQuery(page, pageSize), cancellationToken));

    [HttpGet("{id}")]
    public async Task<ActionResult<DeliveryRuleItem>> Get(string id, CancellationToken cancellationToken) =>
        this.ToActionResult(await deliveryRules.GetAsync(id, cancellationToken));

    [HttpPost, Authorize(Policy = TenantPermissions.StorefrontWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<DeliveryRuleItem>> Create(DeliveryRuleInput rule, CancellationToken cancellationToken)
    {
        var result = await deliveryRules.CreateAsync(new CreateDeliveryRuleRequest(rule, Request.Headers.IdempotencyKey()), cancellationToken);
        return result.IsSuccess ? Created($"/v1/store/delivery-rules/{result.Value!.Id}", result.Value) : this.ToActionResult(result);
    }

    [HttpPut("{id}"), Authorize(Policy = TenantPermissions.StorefrontWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<DeliveryRuleItem>> Update(string id, UpdateDeliveryRuleBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await deliveryRules.UpdateAsync(new UpdateDeliveryRuleRequest(id, body.Rule, body.ExpectedVersion), cancellationToken));
}

public sealed record UpdateDeliveryRuleBody(DeliveryRuleInput Rule, uint ExpectedVersion);
