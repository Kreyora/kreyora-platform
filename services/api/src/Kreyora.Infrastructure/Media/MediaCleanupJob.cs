using Hangfire;
using Kreyora.Application.Catalog;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.Infrastructure.Media;

public sealed class MediaCleanupJob(IServiceScopeFactory scopeFactory)
{
    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var tenantIds = await services.GetRequiredService<AppDbContext>().Tenants.AsNoTracking()
            .Where(tenant => tenant.Status == TenantStatus.Active).Select(tenant => tenant.Id).ToListAsync();
        var runner = services.GetRequiredService<ITenantJobRunner>();
        var media = services.GetRequiredService<IMediaAssetService>();
        foreach (var tenantId in tenantIds)
        {
            await runner.RunAsync(new TenantJobEnvelope(tenantId, "media-cleanup", "{}"), media.CleanupAsync);
        }
    }
}
