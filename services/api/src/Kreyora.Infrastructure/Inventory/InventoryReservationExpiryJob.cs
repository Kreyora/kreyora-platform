using Hangfire;
using Kreyora.Application.Inventory;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.Infrastructure.Inventory;

public sealed class InventoryReservationExpiryJob(IServiceScopeFactory scopeFactory)
{
    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<AppDbContext>();
        var tenantIds = await dbContext.Tenants.AsNoTracking()
            .Where(tenant => tenant.Status == TenantStatus.Active)
            .Select(tenant => tenant.Id)
            .ToListAsync();
        var runner = services.GetRequiredService<ITenantJobRunner>();
        var inventory = services.GetRequiredService<IInventoryService>();

        foreach (var tenantId in tenantIds)
        {
            await runner.RunAsync(new TenantJobEnvelope(tenantId, "inventory-reservation-expiry", "{}"), async cancellationToken =>
            {
                await inventory.ExpireDueReservationsAsync(cancellationToken);
            });
        }
    }
}
