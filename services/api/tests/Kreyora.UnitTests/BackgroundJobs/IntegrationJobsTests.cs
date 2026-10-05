using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Kreyora.Application.Integrations;
using Kreyora.Infrastructure.BackgroundJobs;
using Kreyora.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kreyora.UnitTests.BackgroundJobs;

/// <summary>M08-S06: integration sweepers are scheduled and immediate work is enqueued (Hangfire stubs only).</summary>
public sealed class IntegrationJobsTests
{
    [Fact]
    public void RegisterRecurring_SchedulesWebhookProcessingAndOutboundDeliverySweepersMinutely()
    {
        var manager = new RecordingRecurringManager();

        IntegrationJobRegistration.RegisterRecurring(manager);

        Assert.Equal(typeof(WebhookProcessingJob), manager.Jobs[IntegrationJobRegistration.WebhookProcessingJobId].Job.Type);
        Assert.Equal(typeof(OutboundDeliveryJob), manager.Jobs[IntegrationJobRegistration.OutboundDeliveryJobId].Job.Type);
        Assert.All(manager.Jobs.Values, j => Assert.Equal(Cron.Minutely(), j.Cron));
    }

    [Fact]
    public void Scheduler_EnqueuesTheExactWebhookEventAndOutboundMessage()
    {
        var client = new RecordingJobClient();
        var scheduler = Scheduler(client);

        scheduler.ScheduleWebhookProcessing("evt_1");
        scheduler.ScheduleOutboundDelivery("out_1");

        Assert.Collection(client.Jobs,
            job =>
            {
                Assert.Equal(typeof(IWebhookProcessingService), job.Type);
                Assert.Equal("evt_1", job.Args[0]);
            },
            job =>
            {
                Assert.Equal(typeof(IOutboundMessageService), job.Type);
                Assert.Equal("out_1", job.Args[0]);
            });
    }

    [Fact]
    public void Scheduler_WithoutHangfire_IsANoOp_AndEnqueueFailuresAreSwallowed()
    {
        var noServer = new HangfireIntegrationWorkScheduler(new ServiceCollection().BuildServiceProvider(), NullLogger<HangfireIntegrationWorkScheduler>.Instance);
        noServer.ScheduleWebhookProcessing("evt_1");

        var failing = Scheduler(new RecordingJobClient { Fail = true });
        failing.ScheduleOutboundDelivery("out_1");
    }

    private static HangfireIntegrationWorkScheduler Scheduler(IBackgroundJobClient client) =>
        new(new ServiceCollection().AddSingleton(client).BuildServiceProvider(), NullLogger<HangfireIntegrationWorkScheduler>.Instance);

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public bool Fail { get; init; }
        public List<Job> Jobs { get; } = [];

        public string Create(Job job, IState state)
        {
            if (Fail)
            {
                throw new InvalidOperationException("storage unavailable");
            }

            Jobs.Add(job);
            return Jobs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class RecordingRecurringManager : IRecurringJobManager
    {
        public Dictionary<string, (Job Job, string Cron)> Jobs { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) =>
            Jobs[recurringJobId] = (job, cronExpression);

        public void Trigger(string recurringJobId) { }

        public void RemoveIfExists(string recurringJobId) => Jobs.Remove(recurringJobId);
    }
}
