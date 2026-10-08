using System.Text;
using Hangfire;
using Kreyora.Application.Assistant;
using Kreyora.Application.Audit;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant.Orchestration;

/// <summary>Owner/admin read of the redacted turn log, newest first (M09-S06 Q9).</summary>
public sealed class AssistantTurnLogQuery(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider) : IAssistantTurnLogQuery
{
    public async Task<AssistantUsageItem> UsageAsync(int days, CancellationToken cancellationToken = default)
    {
        tenantContext.RequireCurrent();
        days = Math.Clamp(days, 1, 30);
        var now = timeProvider.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = new DateTimeOffset(today.AddDays(1 - days).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await dbContext.AssistantTurns.AsNoTracking().Where(t => t.StartedAt >= from)
            .Select(t => new { t.StartedAt, t.IsPlayground, t.Outcome, t.ModelCallCount, t.InputTokens, t.OutputTokens, t.EstimatedCostMicroUsd })
            .ToListAsync(cancellationToken);

        var daily = Enumerable.Range(0, days).Select(offset => today.AddDays(offset - days + 1)).Select(date =>
        {
            var day = rows.Where(r => DateOnly.FromDateTime(r.StartedAt.UtcDateTime) == date).ToList();
            var customer = day.Where(r => !r.IsPlayground).ToList();
            return new AssistantUsageDay(date, customer.Count,
                customer.Count(r => r.Outcome == AssistantTurnOutcome.Replied),
                customer.Count(r => r.Outcome is AssistantTurnOutcome.Escalated or AssistantTurnOutcome.Fallback),
                customer.Count(r => r.Outcome is AssistantTurnOutcome.Skipped or AssistantTurnOutcome.Superseded),
                customer.Count(r => r.Outcome == AssistantTurnOutcome.Blocked),
                day.Sum(r => r.ModelCallCount), day.Sum(r => r.InputTokens), day.Sum(r => r.OutputTokens),
                day.Sum(r => r.EstimatedCostMicroUsd) / 1_000_000m, day.Count(r => r.IsPlayground));
        }).ToList();

        var options = aiOptions.CurrentValue;
        var turnsToday = rows.Count(r => DateOnly.FromDateTime(r.StartedAt.UtcDateTime) == today && r.ModelCallCount > 0);
        return new AssistantUsageItem(days, daily, turnsToday, options.Orchestration.MaxTurnsPerTenantPerDay, options.Enabled,
            new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }

    public async Task<CursorPage<AssistantTurnItem>> ListAsync(string? conversationId, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        tenantContext.RequireCurrent();
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.AssistantTurns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(conversationId)) query = query.Where(t => t.ConversationId == conversationId);
        if (Decode(cursor) is { } after) query = query.Where(t => t.StartedAt < after.StartedAt || (t.StartedAt == after.StartedAt && t.Id.CompareTo(after.Id) < 0));
        var rows = await query.OrderByDescending(t => t.StartedAt).ThenByDescending(t => t.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var items = rows.Take(pageSize).Select(t => new AssistantTurnItem(t.Id, t.ConversationId, t.IsPlayground, t.Outcome, t.ReasonCode, t.StartedAt, t.FinishedAt,
            t.PolicyVersion, t.PromptVersion, t.RegistryVersion, t.ModelCalls, t.ToolSteps, t.Citations, t.ValidationCodes, t.InputTokens, t.OutputTokens,
            t.EstimatedCostMicroUsd / 1_000_000m, t.OutboundMessageId)).ToList();
        var last = rows.Count > pageSize ? items[^1] : null;
        return new CursorPage<AssistantTurnItem>(items, last is null ? null : Encode(last.StartedAt, last.Id));
    }

    private static string Encode(DateTimeOffset startedAt, string id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{startedAt.UtcTicks}|{id}"));

    private static (DateTimeOffset StartedAt, string Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            return parts.Length == 2 && long.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var ticks)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), parts[1])
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Deletes turn log rows older than <c>Ai:Orchestration:TurnLogRetentionDays</c> (daily; all tenants).</summary>
public sealed partial class AssistantTurnPurgeJob(IServiceScopeFactory scopeFactory, IOptionsMonitor<AiOptions> aiOptions, ITimeProvider timeProvider, ILogger<AssistantTurnPurgeJob> logger)
{
    public const string RecurringJobId = "assistant-turn-purge";

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Count} assistant turn log rows older than {Days} days")]
    private static partial void LogPurged(ILogger logger, int count, int days);

    public static void RegisterRecurring(IRecurringJobManager manager) =>
        manager.AddOrUpdate<AssistantTurnPurgeJob>(RecurringJobId, job => job.PurgeAsync(), "17 3 * * *");

    public async Task<int> PurgeAsync()
    {
        var days = aiOptions.CurrentValue.Orchestration.TurnLogRetentionDays;
        var cutoff = timeProvider.UtcNow.AddDays(-days);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Platform maintenance by age only: no tenant data is read, so it runs across tenants.
        var count = await db.AssistantTurns.IgnoreQueryFilters().Where(t => t.StartedAt < cutoff).ExecuteDeleteAsync();
        LogPurged(logger, count, days);
        return count;
    }
}
