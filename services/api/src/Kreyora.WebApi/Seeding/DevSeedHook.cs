using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kreyora.WebApi.Seeding;

public static partial class DevSeedHook
{
    public static async Task SeedDevelopmentDataAsync(IServiceProvider services)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment())
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeed");
        LogSeedStarted(logger);

        await Task.CompletedTask;

        LogSeedCompleted(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed hook started")]
    private static partial void LogSeedStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed hook completed (no business data yet)")]
    private static partial void LogSeedCompleted(ILogger logger);
}
