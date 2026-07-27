using Kreyora.Application.Abstractions;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<Domain.Abstractions.ITimeProvider, SystemTimeProvider>();

        var connectionString = configuration.GetValue<string>("Database:ConnectionString");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                }));

            services.AddScoped<IUnitOfWork, UnitOfWork>();
        }

        return services;
    }
}
