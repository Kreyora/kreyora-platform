using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Models;
using Kreyora.Application.Payments;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Common;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Payments;

public sealed class StorePaymentConfigurationService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents) : IStorePaymentConfigurationService
{
    public async Task<Result<StorePaymentConfigurationItem>> GetConfigurationAsync(string storeId, CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.StorefrontRead);
        _ = tenantContext.RequireCurrent();

        var normalizedStoreId = Require(storeId, nameof(storeId), 26);
        var storeExists = await dbContext.Stores.AnyAsync(s => s.Id == normalizedStoreId, cancellationToken);
        if (!storeExists)
        {
            return Result<StorePaymentConfigurationItem>.NotFound("Store not found.");
        }

        var config = await dbContext.StorePaymentConfigurations
            .SingleOrDefaultAsync(c => c.StoreId == normalizedStoreId, cancellationToken);

        if (config is null)
        {
            // Default: COD and Merchant QR both enabled by default for existing stores
            return Result<StorePaymentConfigurationItem>.Success(new StorePaymentConfigurationItem(
                string.Empty,
                normalizedStoreId,
                CodEnabled: true,
                MerchantQrEnabled: true,
                MerchantQrInstructions: null,
                MerchantQrMediaAssetId: null));
        }

        return Result<StorePaymentConfigurationItem>.Success(Map(config));
    }

    public async Task<Result<StorePaymentConfigurationItem>> UpdateConfigurationAsync(UpdateStorePaymentConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.StorefrontWrite);
        var context = tenantContext.RequireCurrent();

        var normalizedStoreId = Require(request.StoreId, nameof(request.StoreId), 26);
        var store = await dbContext.Stores.SingleOrDefaultAsync(s => s.Id == normalizedStoreId, cancellationToken);
        if (store is null)
        {
            return Result<StorePaymentConfigurationItem>.NotFound("Store not found.");
        }

        if (!request.CodEnabled && !request.MerchantQrEnabled)
        {
            return Result<StorePaymentConfigurationItem>.ValidationError("At least one payment method (COD or Merchant QR) must be enabled.");
        }

        if (!string.IsNullOrWhiteSpace(request.MerchantQrMediaAssetId))
        {
            var mediaExists = await dbContext.MediaAssets
                .AnyAsync(m => m.Id == request.MerchantQrMediaAssetId, cancellationToken);
            if (!mediaExists)
            {
                return Result<StorePaymentConfigurationItem>.ValidationError("Referenced QR code media asset was not found.");
            }
        }

        try
        {
            var config = await dbContext.StorePaymentConfigurations
                .SingleOrDefaultAsync(c => c.StoreId == normalizedStoreId, cancellationToken);

            if (config is null)
            {
                config = StorePaymentConfiguration.Create(
                    context.TenantId,
                    normalizedStoreId,
                    request.CodEnabled,
                    request.MerchantQrEnabled,
                    request.MerchantQrInstructions,
                    request.MerchantQrMediaAssetId);
                dbContext.StorePaymentConfigurations.Add(config);
            }
            else
            {
                config.Update(
                    request.CodEnabled,
                    request.MerchantQrEnabled,
                    request.MerchantQrInstructions,
                    request.MerchantQrMediaAssetId);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(new AuditEventWrite(
                Action: "store.payment_configuration.updated",
                TargetType: "store-payment-configuration",
                TargetId: config.Id,
                Metadata: System.Text.Json.JsonSerializer.Serialize(new
                {
                    storeId = normalizedStoreId,
                    codEnabled = config.CodEnabled,
                    merchantQrEnabled = config.MerchantQrEnabled
                })), cancellationToken);

            return Result<StorePaymentConfigurationItem>.Success(Map(config));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<StorePaymentConfigurationItem>.ValidationError(exception.Message);
        }
    }

    private static StorePaymentConfigurationItem Map(StorePaymentConfiguration config) => new(
        config.Id,
        config.StoreId,
        config.CodEnabled,
        config.MerchantQrEnabled,
        config.MerchantQrInstructions,
        config.MerchantQrMediaAssetId);

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
}
