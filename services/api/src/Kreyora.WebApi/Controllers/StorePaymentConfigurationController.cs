using Asp.Versioning;
using Kreyora.Application.Authorization;
using Kreyora.Application.Payments;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController, RequireTenantContext, ApiVersion("1.0")]
[Route("v{version:apiVersion}/store")]
public sealed class StorePaymentConfigurationController(IStorePaymentConfigurationService configService) : ControllerBase
{
    [HttpGet("{storeId}/payment-configuration"), Authorize(Policy = TenantPermissions.StorefrontRead)]
    public async Task<ActionResult<StorePaymentConfigurationItem>> GetConfiguration(string storeId, CancellationToken cancellationToken) =>
        this.ToActionResult(await configService.GetConfigurationAsync(storeId, cancellationToken));

    [HttpPut("{storeId}/payment-configuration"), Authorize(Policy = TenantPermissions.StorefrontWrite), ValidateAntiForgeryToken]
    public async Task<ActionResult<StorePaymentConfigurationItem>> UpdateConfiguration(
        string storeId,
        UpdateStorePaymentConfigurationBody body,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await configService.UpdateConfigurationAsync(
            new UpdateStorePaymentConfigurationRequest(
                storeId,
                body.CodEnabled,
                body.MerchantQrEnabled,
                body.MerchantQrInstructions,
                body.MerchantQrMediaAssetId),
            cancellationToken));
}

public sealed record UpdateStorePaymentConfigurationBody(
    bool CodEnabled,
    bool MerchantQrEnabled,
    string? MerchantQrInstructions = null,
    string? MerchantQrMediaAssetId = null);

