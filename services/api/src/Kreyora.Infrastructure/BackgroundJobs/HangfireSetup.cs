using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.Infrastructure.BackgroundJobs;

public static class HangfireSetup
{
    public static IServiceCollection AddHangfireServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetValue<string>("Database:ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("kreyora");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(connectionString);
            }));

        // BackgroundJobs:ServerEnabled=false keeps enqueueing (jobs wait in storage) but runs no server in this
        // process: for a separate worker later, and for deterministic failure/replay checks in the M08-S07 sandbox.
        if (configuration.GetValue("BackgroundJobs:ServerEnabled", defaultValue: true))
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = 2;
            });
        }

        return services;
    }
}
