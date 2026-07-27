using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kreyora.ServiceDefaults;

/// <summary>
/// Aspire-compatible service defaults. Will be expanded in M02-S04 when the
/// Aspire workload is installed. For now this provides a minimal health-check
/// registration so the WebApi project can reference it without errors.
/// </summary>
public static class Extensions
{
    public static IServiceCollection AddServiceDefaults(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy());

        return services;
    }

    public static WebApplication MapServiceDefaults(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        return app;
    }
}
