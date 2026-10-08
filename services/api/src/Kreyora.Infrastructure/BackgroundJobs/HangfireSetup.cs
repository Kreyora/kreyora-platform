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

        // M09-S08 (S07 finding F3): how often queued and delayed jobs are picked up. Hangfire's 15 s default added up to
        // ~15 s before an assistant turn started; 2 s costs a few cheap polling queries per second at most.
        var pollInterval = TimeSpan.FromSeconds(PollIntervalSeconds(configuration));

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(connectionString);
            }, new PostgreSqlStorageOptions { QueuePollInterval = pollInterval }));

        // BackgroundJobs:ServerEnabled=false keeps enqueueing (jobs wait in storage) but runs no server in this
        // process: for a separate worker later, and for deterministic failure/replay checks in the M08-S07 sandbox.
        if (configuration.GetValue("BackgroundJobs:ServerEnabled", defaultValue: true))
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = 2;
                options.SchedulePollingInterval = pollInterval;
            });
        }

        return services;
    }

    /// <summary><c>BackgroundJobs:PollIntervalSeconds</c> (default 2), validated at start-up: 1-15 seconds.</summary>
    public static int PollIntervalSeconds(IConfiguration configuration)
    {
        var seconds = configuration.GetValue("BackgroundJobs:PollIntervalSeconds", defaultValue: 2);
        return seconds is >= 1 and <= 15
            ? seconds
            : throw new InvalidOperationException("BackgroundJobs:PollIntervalSeconds must be between 1 and 15.");
    }
}
