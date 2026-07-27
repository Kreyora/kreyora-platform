using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Persistence;

public static partial class MigrationRunner
{
    public static async Task ApplyMigrationsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        var pending = await context.Database.GetPendingMigrationsAsync();
        var pendingList = pending.ToList();

        if (pendingList.Count == 0)
        {
            LogNoMigrations(logger);
            return;
        }

        LogApplyingMigrations(logger, pendingList.Count);
        await context.Database.MigrateAsync();
        LogMigrationsApplied(logger, pendingList.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No pending migrations")]
    private static partial void LogNoMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} pending migration(s)")]
    private static partial void LogApplyingMigrations(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully applied {Count} migration(s)")]
    private static partial void LogMigrationsApplied(ILogger logger, int count);
}
