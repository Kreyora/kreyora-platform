using Kreyora.Application.Abstractions;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<Domain.Abstractions.ITimeProvider, SystemTimeProvider>();

        return services;
    }
}
