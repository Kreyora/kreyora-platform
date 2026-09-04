using Hangfire;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.Infrastructure.Storefront;

public sealed class CheckoutSessionExpiryJob(IServiceScopeFactory scopeFactory)
{
    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<AppDbContext>();
        var tenantIds = await dbContext.Tenants.AsNoTracking().Where(tenant => tenant.Status == TenantStatus.Active).Select(tenant => tenant.Id).ToListAsync();
        var runner = services.GetRequiredService<ITenantJobRunner>();
        var checkout = services.GetRequiredService<IStorefrontCheckoutSessionService>();
        foreach (var tenantId in tenantIds)
        {
            await runner.RunAsync(new TenantJobEnvelope(tenantId, "checkout-session-expiry", "{}"), checkout.ExpireDueSessionsAsync);
        }
    }
}
