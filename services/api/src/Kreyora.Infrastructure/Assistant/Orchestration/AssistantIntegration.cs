using Hangfire;
using Kreyora.Application.Assistant;
using Kreyora.Application.Audit;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Common;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Conversations;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant.Orchestration;

// M09-S07 (ADR-022): inbound → turn trigger, retry-safe turn job, sweeper for lost triggers, native-reply takeover.

/// <summary>
/// Collects new customer messages and Instagram-app echoes during one webhook run; schedules after commit. Only shops
/// the assistant may serve (platform switch on, entitled) get jobs: other shops keep M08 behaviour and cost nothing.
/// </summary>
public sealed class AssistantInboundHook(IAssistantTurnScheduler scheduler, IAssistantEntitlementQuery entitlements, IOptionsMonitor<AiOptions> aiOptions) : IAssistantInboundHook
{
    private readonly List<(string TenantId, string ConversationId, string MessageId)> customerMessages = [];
    private readonly List<(string TenantId, string MessageId)> nativeReplies = [];

    public void CustomerMessageReceived(string tenantId, string conversationId, string messageId) => customerMessages.Add((tenantId, conversationId, messageId));

    public void NativeReplyReceived(string tenantId, string messageId) => nativeReplies.Add((tenantId, messageId));

    public void Flush()
    {
        var options = aiOptions.CurrentValue;
        if (options.Enabled)
        {
            foreach (var (tenantId, conversationId, messageId) in customerMessages.Where(m => entitlements.IsEntitled(m.TenantId)))
            {
                scheduler.ScheduleTurn(tenantId, conversationId, messageId, TimeSpan.FromSeconds(options.Orchestration.DebounceSeconds));
            }

            foreach (var (tenantId, messageId) in nativeReplies.Where(m => entitlements.IsEntitled(m.TenantId)))
            {
                scheduler.ScheduleNativeReplyCheck(tenantId, messageId, TimeSpan.FromSeconds(options.Orchestration.NativeReplyCheckSeconds));
            }
        }

        Discard();
    }

    public void Discard()
    {
        customerMessages.Clear();
        nativeReplies.Clear();
    }
}

/// <summary>Delayed Hangfire jobs; a no-op without Hangfire, and never throws into the caller.</summary>
public sealed partial class HangfireAssistantTurnScheduler(ILogger<HangfireAssistantTurnScheduler> logger, IBackgroundJobClient? jobs = null) : IAssistantTurnScheduler
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not schedule assistant work {Work}; the sweeper will pick it up")]
    private static partial void LogScheduleFailed(ILogger logger, Exception ex, string work);

    public void ScheduleTurn(string tenantId, string conversationId, string messageId, TimeSpan delay, int attempt = 0) =>
        Try("turn", () => jobs?.Schedule<AssistantTurnJob>(job => job.RunAsync(tenantId, conversationId, messageId, attempt), delay));

    public void ScheduleNativeReplyCheck(string tenantId, string messageId, TimeSpan delay) =>
        Try("native-reply-check", () => jobs?.Schedule<NativeReplyCheckJob>(job => job.RunAsync(tenantId, messageId), delay));

    private void Try(string work, Action schedule)
    {
        try
        {
            schedule();
        }
        catch (Exception ex)
        {
            LogScheduleFailed(logger, ex, work);
        }
    }
}

/// <summary>
/// Runs one assistant turn as the system inside the trigger's tenant (from the job envelope, re-checked by the job
/// runner). Busy results are re-scheduled with backoff; crashes go to Hangfire's retry, which replays the turn by key.
/// </summary>
public sealed partial class AssistantTurnJob(IServiceScopeFactory scopeFactory, IOptionsMonitor<AiOptions> aiOptions, ILogger<AssistantTurnJob> logger)
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant turn job for tenant {TenantId} could not run: tenant unavailable")]
    private static partial void LogTenantUnavailable(ILogger logger, string tenantId);

    public async Task<AssistantTurnResult?> RunAsync(string tenantId, string conversationId, string messageId, int attempt)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        AssistantTurnResult? result = null;
        try
        {
            await services.GetRequiredService<ITenantJobRunner>().RunAsync(new TenantJobEnvelope(tenantId, "assistant-turn", "{}"), async cancellationToken =>
            {
                result = await services.GetRequiredService<IAssistantTurnService>().RunAsync(conversationId, messageId, cancellationToken);
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("inactive or unavailable", StringComparison.Ordinal))
        {
            LogTenantUnavailable(logger, tenantId);
            return null;
        }

        var maxRetries = aiOptions.CurrentValue.Orchestration.MaxBusyRetries;
        if (result is { Outcome: AssistantTurnOutcome.Skipped, ReasonCode: AssistantTurnReasons.ConversationBusy or AssistantTurnReasons.TenantBusy } && attempt < maxRetries)
        {
            services.GetRequiredService<IAssistantTurnScheduler>().ScheduleTurn(tenantId, conversationId, messageId, TimeSpan.FromSeconds(5 * Math.Pow(2, attempt)), attempt + 1);
        }

        return result;
    }
}

/// <summary>
/// Every minute (M09-S07 Q2): customer messages from the last <c>TriggerSweepMinutes</c> that are still unanswered — no
/// turn, the newest in their AI-owned conversation, received after the last release — are scheduled. Covers lost
/// triggers and restarts; the turn key makes a double schedule harmless.
/// </summary>
public sealed partial class AssistantTurnSweepJob(
    IServiceScopeFactory scopeFactory,
    IAssistantEntitlementQuery entitlements,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider,
    ILogger<AssistantTurnSweepJob> logger)
{
    public const string RecurringJobId = "assistant-turn-sweep";
    public const int MaxPerSweep = 200;

    [LoggerMessage(Level = LogLevel.Information, Message = "Assistant sweep scheduled {Count} unanswered customer messages")]
    private static partial void LogSwept(ILogger logger, int count);

    public static void RegisterRecurring(IRecurringJobManager manager) =>
        manager.AddOrUpdate<AssistantTurnSweepJob>(RecurringJobId, job => job.SweepAsync(), "* * * * *");

    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task<int> SweepAsync()
    {
        var options = aiOptions.CurrentValue;
        if (!options.Enabled) return 0;
        // The allowlist goes into SQL so shops without the assistant (most of them) cannot crowd out the batch.
        var allowlist = options.Entitlements.Mode == AiEntitlementMode.Allowlist ? options.Entitlements.AllowedTenantIds.ToList() : null;
        if (allowlist is { Count: 0 }) return 0;
        var now = timeProvider.UtcNow;
        var windowStart = now.AddMinutes(-options.Orchestration.TriggerSweepMinutes);
        var until = now.AddSeconds(-(options.Orchestration.DebounceSeconds + 60)); // leave the normal trigger time to act

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Platform maintenance across tenants: reads IDs only; each scheduled job re-checks everything in its tenant.
        var candidates = await (
            from message in db.Messages.IgnoreQueryFilters().AsNoTracking()
            join conversation in db.Conversations.IgnoreQueryFilters().AsNoTracking() on new { message.TenantId, Id = message.ConversationId } equals new { conversation.TenantId, conversation.Id }
            join tenant in db.Tenants.AsNoTracking() on message.TenantId equals tenant.Id
            where message.Direction == MessageDirection.Inbound && message.Origin == MessageOrigin.Customer
                && message.ReceivedAt >= windowStart && message.ReceivedAt <= until
                && tenant.Status == TenantStatus.Active
                && (allowlist == null || allowlist.Contains(message.TenantId))
                && db.AssistantPolicies.IgnoreQueryFilters().Any(p => p.TenantId == message.TenantId && p.Enabled)
                && conversation.AutomationMode == AutomationMode.Automated && conversation.Status != ConversationStatus.Spam
                && (conversation.AutomationResumedAt == null || message.ReceivedAt > conversation.AutomationResumedAt)
                && !db.Messages.IgnoreQueryFilters().Any(n => n.TenantId == message.TenantId && n.ConversationId == message.ConversationId &&
                    n.Direction == MessageDirection.Inbound && n.Origin == MessageOrigin.Customer && n.ReceivedAt > message.ReceivedAt)
                && !db.AssistantTurns.IgnoreQueryFilters().Any(t => t.TenantId == message.TenantId && t.TriggerMessageId == message.Id)
            orderby message.ReceivedAt
            select new { message.TenantId, message.ConversationId, message.Id })
            .Take(MaxPerSweep).ToListAsync();

        var scheduler = scope.ServiceProvider.GetRequiredService<IAssistantTurnScheduler>();
        var scheduled = 0;
        foreach (var candidate in candidates.Where(c => entitlements.IsEntitled(c.TenantId)))
        {
            scheduler.ScheduleTurn(candidate.TenantId, candidate.ConversationId, candidate.Id, TimeSpan.Zero);
            scheduled++;
        }

        if (scheduled > 0) LogSwept(logger, scheduled);
        return scheduled;
    }
}

/// <summary>Runs the native-reply check in the echo's tenant.</summary>
public sealed class NativeReplyCheckJob(IServiceScopeFactory scopeFactory)
{
    public async Task<bool> RunAsync(string tenantId, string messageId)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var tookOver = false;
        await services.GetRequiredService<ITenantJobRunner>().RunAsync(new TenantJobEnvelope(tenantId, "assistant-native-reply-check", "{}"), async cancellationToken =>
        {
            tookOver = await services.GetRequiredService<INativeReplyTakeoverService>().CheckAsync(messageId, cancellationToken);
        });
        return tookOver;
    }
}

/// <summary>
/// M09-S07 Q6 (ADR-022): an Instagram-app echo that is still unmatched after the check delay is the seller replying from
/// the app, so a person owns the conversation now. Our own sends are excluded twice: the delivery reconciler removes their
/// echo rows, and any outbound message with the same provider message ID counts as ours.
/// </summary>
public sealed class NativeReplyTakeoverService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAuditEventService auditEvents,
    ITimeProvider timeProvider) : INativeReplyTakeoverService
{
    public async Task<bool> CheckAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var echo = await dbContext.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId && m.Origin == MessageOrigin.ProviderNative, cancellationToken);
        if (echo is null) return false; // reconciled as one of our own sends, or redacted/removed

        var ours = echo.ProviderMessageId is not null && await dbContext.OutboundMessages.AnyAsync(o => o.TenantId == tenantId && o.ConnectionId == echo.ConnectionId &&
            o.ProviderMessageId == echo.ProviderMessageId, cancellationToken);
        if (ours) return false;

        for (var attempt = 1; ; attempt++)
        {
            var conversation = await dbContext.Conversations.SingleOrDefaultAsync(c => c.Id == echo.ConversationId && c.TenantId == tenantId, cancellationToken);
            if (conversation is null || !conversation.TakeOver()) return false; // already owned by a person
            try
            {
                var suppression = await ConversationTakeover.SuppressAutomationAsync(dbContext, conversation, timeProvider.UtcNow, cancellationToken);
                await auditEvents.AppendAsync(ConversationReplyService.TakeoverAudit(conversation.Id, "native_app_reply", suppression) with { ActorKind = CommerceActorKind.CommerceSystem },
                    cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
