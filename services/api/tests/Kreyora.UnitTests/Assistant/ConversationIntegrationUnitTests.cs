using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Kreyora.Application.Assistant;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant.Orchestration;
using Kreyora.UnitTests.Ai;

namespace Kreyora.UnitTests.Assistant;

/// <summary>M09-S07 (ADR-022): entitlement allowlist, the inbound hook (after-commit scheduling, gating), the Hangfire scheduler, options.</summary>
public sealed class ConversationIntegrationUnitTests
{
    // ---- entitlement ----

    [Fact]
    public void TheAllowlist_EntitlesOnlyListedShops_AndChangesApplyWithoutRestart()
    {
        var options = new AiOptions();
        options.Entitlements.AllowedTenantIds.Add("tenant-a");
        var monitor = new StaticOptionsMonitor<AiOptions>(options);
        var entitlements = new AssistantEntitlementQuery(monitor);

        Assert.True(entitlements.IsEntitled("tenant-a"));
        Assert.False(entitlements.IsEntitled("tenant-b"));
        Assert.False(entitlements.IsEntitled("TENANT-A")); // IDs match exactly

        var allTenants = new AiOptions();
        allTenants.Entitlements.Mode = AiEntitlementMode.AllTenants;
        monitor.CurrentValue = allTenants;
        Assert.True(entitlements.IsEntitled("tenant-b"));
    }

    [Fact]
    public void TheDefault_IsAnEmptyAllowlist_NobodyIsEntitled()
    {
        var entitlements = new AssistantEntitlementQuery(new StaticOptionsMonitor<AiOptions>(new AiOptions()));

        Assert.Equal(AiEntitlementMode.Allowlist, new AiOptions().Entitlements.Mode);
        Assert.False(entitlements.IsEntitled("any-tenant"));
    }

    // ---- inbound hook ----

    [Fact]
    public void TheHook_SchedulesOnlyOnFlush_WithTheConfiguredDelays_ThenForgets()
    {
        var scheduler = new RecordingScheduler();
        var hook = Hook(scheduler, ["tenant-a"]);

        hook.CustomerMessageReceived("tenant-a", "conv-1", "msg-1");
        hook.NativeReplyReceived("tenant-a", "echo-1");
        Assert.Empty(scheduler.Calls); // nothing before the webhook transaction commits

        hook.Flush();
        hook.Flush(); // a second flush has nothing left

        Assert.Equal(["turn tenant-a conv-1 msg-1 00:00:04 0", "check tenant-a echo-1 00:00:30"], scheduler.Calls);
    }

    [Fact]
    public void ADiscardedRun_SchedulesNothing()
    {
        var scheduler = new RecordingScheduler();
        var hook = Hook(scheduler, ["tenant-a"]);
        hook.CustomerMessageReceived("tenant-a", "conv-1", "msg-1");
        hook.NativeReplyReceived("tenant-a", "echo-1");

        hook.Discard(); // the webhook transaction rolled back
        hook.Flush();

        Assert.Empty(scheduler.Calls);
    }

    [Fact]
    public void TheHook_SkipsShopsNotEntitled_AndEverythingWhenTheAssistantIsOff()
    {
        var scheduler = new RecordingScheduler();
        var hook = Hook(scheduler, ["tenant-a"]);
        hook.CustomerMessageReceived("tenant-b", "conv-2", "msg-2");
        hook.NativeReplyReceived("tenant-b", "echo-2");
        hook.Flush();
        Assert.Empty(scheduler.Calls);

        var off = Hook(scheduler, ["tenant-a"], enabled: false);
        off.CustomerMessageReceived("tenant-a", "conv-1", "msg-1");
        off.NativeReplyReceived("tenant-a", "echo-1");
        off.Flush();
        Assert.Empty(scheduler.Calls);
    }

    // ---- Hangfire scheduler ----

    [Fact]
    public void TheScheduler_CreatesDelayedJobs_WithTheTenantAndAttempt()
    {
        var jobs = new RecordingJobClient();
        var scheduler = new HangfireAssistantTurnScheduler(new CapturingLogger<HangfireAssistantTurnScheduler>(), jobs);

        scheduler.ScheduleTurn("tenant-a", "conv-1", "msg-1", TimeSpan.FromSeconds(4), 2);
        scheduler.ScheduleNativeReplyCheck("tenant-a", "echo-1", TimeSpan.FromSeconds(30));

        Assert.Collection(jobs.Created,
            turn =>
            {
                Assert.Equal((typeof(AssistantTurnJob), nameof(AssistantTurnJob.RunAsync)), (turn.Job.Type, turn.Job.Method.Name));
                Assert.Equal(["tenant-a", "conv-1", "msg-1", 2], turn.Job.Args);
                Assert.InRange(((ScheduledState)turn.State).EnqueueAt - DateTime.UtcNow, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            },
            check =>
            {
                Assert.Equal(typeof(NativeReplyCheckJob), check.Job.Type);
                Assert.Equal(["tenant-a", "echo-1"], check.Job.Args);
            });
    }

    [Fact]
    public void ASchedulingFailure_IsLogged_AndNeverBreaksTheWebhook()
    {
        var logger = new CapturingLogger<HangfireAssistantTurnScheduler>();
        var scheduler = new HangfireAssistantTurnScheduler(logger, new RecordingJobClient { Fail = true });

        scheduler.ScheduleTurn("tenant-a", "conv-1", "msg-1", TimeSpan.Zero);
        new HangfireAssistantTurnScheduler(logger).ScheduleTurn("tenant-a", "conv-1", "msg-1", TimeSpan.Zero); // no Hangfire: a no-op

        var line = Assert.Single(logger.Lines);
        Assert.Contains("sweeper will pick it up", line, StringComparison.Ordinal);
        Assert.DoesNotContain("msg-1", line, StringComparison.Ordinal);
    }

    // ---- options ----

    [Fact]
    public void IntegrationOptions_AreValidated()
    {
        var options = new AiOptions();
        options.Orchestration.DebounceSeconds = 61;
        options.Orchestration.NativeReplyCheckSeconds = 1;
        options.Orchestration.TriggerSweepMinutes = 1;
        options.Orchestration.MaxBusyRetries = 11;
        options.Entitlements.Mode = (AiEntitlementMode)9;

        var errors = AiOptionsValidator.Errors(options).ToList();

        foreach (var field in new[] { "DebounceSeconds", "NativeReplyCheckSeconds", "TriggerSweepMinutes", "MaxBusyRetries", "Entitlements:Mode" })
        {
            Assert.Contains(errors, e => e.Contains(field, StringComparison.Ordinal));
        }
    }

    // ---- helpers ----

    private static AssistantInboundHook Hook(RecordingScheduler scheduler, string[] allowed, bool enabled = true)
    {
        var options = new AiOptions { Enabled = enabled };
        options.Entitlements.AllowedTenantIds.AddRange(allowed);
        var monitor = new StaticOptionsMonitor<AiOptions>(options);
        return new AssistantInboundHook(scheduler, new AssistantEntitlementQuery(monitor), monitor);
    }

    private sealed class RecordingScheduler : IAssistantTurnScheduler
    {
        public List<string> Calls { get; } = [];

        public void ScheduleTurn(string tenantId, string conversationId, string messageId, TimeSpan delay, int attempt = 0) =>
            Calls.Add($"turn {tenantId} {conversationId} {messageId} {delay:c} {attempt}");

        public void ScheduleNativeReplyCheck(string tenantId, string messageId, TimeSpan delay) =>
            Calls.Add($"check {tenantId} {messageId} {delay:c}");
    }

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public bool Fail { get; init; }

        public List<(Job Job, IState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            if (Fail) throw new InvalidOperationException("Job storage is unavailable.");
            Created.Add((job, state));
            return Created.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
