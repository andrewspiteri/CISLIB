using System.Diagnostics.Metrics;
using CIS.Coordination;
using CIS.Healthcheck;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using CIS.Metrics;
using CIS.Tests.UnitTests.Jobs.Scheduled;
using CIS.Tests.UnitTests.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs;

[TestClass]
public sealed class JobInstrumentationTests
{
    [TestMethod]
    public async Task OnlyKnownCompletedDurationsAreRecordedAndLateAttachmentDoesNotReplayWork()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        using var meter = new Meter("JobDurationObservations");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var instrumentation = new JobInstrumentation(new JobsComposition(), fixture.Runtime, fixture.Clock);
        instrumentation.InvocationCompleted(JobExecutionMode.Scheduled, "success", 1);
        instrumentation.AttachMetrics(metrics);
        instrumentation.InvocationCompleted(JobExecutionMode.Continuous, "success", null);
        instrumentation.InvocationStarted(JobExecutionMode.Scheduled);
        Assert.IsEmpty(listener.Points("cis.jobs.invocation.duration"));
        foreach (JobExecutionMode mode in Enum.GetValues<JobExecutionMode>())
        {
            foreach (string outcome in new[] { "success", "failed", "cancelled", "ownership_lost", "indeterminate" })
            {
                instrumentation.InvocationCompleted(mode, outcome, 2.25);
            }
        }

        MetricsPoint[] points = listener.Points("cis.jobs.invocation.duration");
        Assert.HasCount(10, points);
        Assert.IsTrue(points.All(point => (double)point.Value == 2.25 && point.Tags.Length == 2));
        CollectionAssert.AreEquivalent(new[] { "continuous", "scheduled" },
            points.Select(point => (string)point.Tags.Single(tag => tag.Key == "mode").Value!).Distinct().ToArray());
        Assert.AreEqual(3, metrics.Definitions.Count);
        Assert.AreEqual(0, fixture.Acquisitions);
        Assert.AreEqual(0, fixture.Calls);
    }

    [TestMethod]
    public async Task AttachingDuringActualCallbackDoesNotRetroactivelyRecordItsDuration()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        var callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Completed));
        fixture.Handler = (_, _, _) => callback.Task;
        using var meter = new Meter("LateJobObservationAttachment");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var instrumentation = new JobInstrumentation(new JobsComposition(), fixture.Runtime, fixture.Clock);
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> first = fixture.Dispatcher.DispatchAsync(fixture.Run());
        try
        {
            await fixture.CallbackStarted.Task.WaitAsync(MetricsTestSupport.Deadline);
            instrumentation.AttachMetrics(metrics);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            callback.TrySetResult(new(ScheduledJobOutcome.Completed));
            Assert.AreEqual(ScheduledJobOutcome.Completed, (await first.WaitAsync(MetricsTestSupport.Deadline)).Outcome);
            Assert.AreEqual(0, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
            Assert.IsEmpty(listener.Points("cis.jobs.invocation.duration"));

            fixture.Handler = (_, _, _) =>
            {
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(500));
                return Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Completed));
            };
            Assert.AreEqual(ScheduledJobOutcome.Completed,
                (await fixture.Dispatcher.DispatchAsync(fixture.Run(occurrence: "after-attachment"))).Outcome);
            MetricsPoint completed = listener.Points("cis.jobs.invocation.duration").Single();
            Assert.AreEqual(0.5d, (double)completed.Value);
            MetricsTestSupport.AssertTags(completed, new("mode", "scheduled"), new("outcome", "success"));
        }
        finally
        {
            callback.TrySetResult(new(ScheduledJobOutcome.Completed));
            await first.WaitAsync(MetricsTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task PendingGaugeReadsActualBoundedAdmissionWithoutInvokingSourceAgain()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        var admission = fixture.Hold(new JobAdmissionValidation(false));
        fixture.AdmissionSource.Validate = (_, _) => new(admission.Task);
        using var meter = new Meter("JobPendingObservations");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var instrumentation = new JobInstrumentation(new JobsComposition(), fixture.Runtime, fixture.Clock);
        instrumentation.AttachMetrics(metrics);
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(fixture.Run());
        try
        {
            await fixture.AdmissionSource.Started.Task.WaitAsync(MetricsTestSupport.Deadline);
            listener.Collect();
            Assert.AreEqual(1d, (double)listener.Points("cis.jobs.pending.dispatches").Last().Value);
            Assert.IsTrue(listener.Points("cis.jobs.pending.dispatches").Last().Tags.Length == 0);
            Assert.IsTrue(listener.Points("cis.jobs.active.invocations").All(point => (double)point.Value == 0));
            Assert.AreEqual(1, fixture.AdmissionSource.Calls);
            Assert.AreEqual(0, fixture.Acquisitions);
            Assert.IsEmpty(listener.Points("cis.jobs.invocation.duration"));
        }
        finally
        {
            admission.TrySetResult(new(false));
            await dispatch.WaitAsync(MetricsTestSupport.Deadline);
        }

        listener.Collect();
        Assert.AreEqual(0d, (double)listener.Points("cis.jobs.pending.dispatches").Last().Value);
        Assert.IsEmpty(listener.Points("cis.jobs.invocation.duration"));
    }

    [TestMethod]
    public async Task LostOwnershipDuringUncooperativeCallbackIsUnhealthyAndStillActuallyActive()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        var callback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Indeterminate));
        fixture.Handler = (_, _, _) => callback.Task;
        using var meter = new Meter("LostActiveJobObservations");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var health = new HealthcheckService(new(), new()
        {
            Name = "job-test",
            InstanceId = Guid.NewGuid()
        }, fixture.Clock);
        await using var instrumentation = new JobInstrumentation(new JobsComposition(), fixture.Runtime, fixture.Clock);
        instrumentation.AttachMetrics(metrics);
        await fixture.Runtime.StartAsync();
        instrumentation.ActivateHealth(health, true);
        await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Healthy);
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(fixture.Run());
        try
        {
            await fixture.CallbackStarted.Task.WaitAsync(MetricsTestSupport.Deadline);
            fixture.Clock.Advance(TimeSpan.FromSeconds(2));
            await fixture.Leases.Single().LoseAsync(CoordinationLossReason.Superseded);
            await UntilAsync(() => fixture.Runtime.CaptureSnapshot().Single().State == JobLocalState.Lost);
            await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Unhealthy);
            Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy);
            Assert.AreEqual("jobs.required_duty_unhealthy", health.CaptureSnapshot().Monitors.Single().Diagnostic!.Code);
            Assert.IsTrue(fixture.CallbackToken.IsCancellationRequested);
            Assert.IsFalse(dispatch.IsCompleted);
            int acquisitions = fixture.Acquisitions;
            for (int index = 0; index < 20; index++)
            {
                listener.Collect();
                instrumentation.RefreshHealth();
                _ = health.CaptureSnapshot();
            }

            Assert.AreEqual(1d, Active(listener, "scheduled"));
            Assert.AreEqual(0d, Active(listener, "continuous"));
            Assert.AreEqual(acquisitions, fixture.Acquisitions);
            Assert.AreEqual(1, fixture.Calls);
            Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
            Assert.IsEmpty(listener.Points("cis.jobs.invocation.duration"));
        }
        finally
        {
            callback.TrySetResult(new(ScheduledJobOutcome.Indeterminate));
            await dispatch.WaitAsync(MetricsTestSupport.Deadline);
        }

        Assert.AreEqual(ScheduledJobOutcome.Indeterminate, (await dispatch).Outcome);
        listener.Collect();
        Assert.AreEqual(0d, Active(listener, "scheduled"));
        MetricsPoint completed = listener.Points("cis.jobs.invocation.duration").Single();
        Assert.AreEqual(2d, (double)completed.Value);
        MetricsTestSupport.AssertTags(completed, new("mode", "scheduled"), new("outcome", "indeterminate"));
    }

    [TestMethod]
    public async Task CompletedHandlerRemainsActiveAndHasNoDurationUntilActualScopeCleanupSettles()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        var cleanup = fixture.Hold();
        fixture.Cleanup = number => number == 2 ? new(cleanup.Task) : ValueTask.CompletedTask;
        using var meter = new Meter("JobCleanupObservations");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var instrumentation = new JobInstrumentation(new JobsComposition(), fixture.Runtime, fixture.Clock);
        instrumentation.AttachMetrics(metrics);
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> dispatch = fixture.Dispatcher.DispatchAsync(fixture.Run());
        try
        {
            await fixture.WorkCleanupStarted.Task.WaitAsync(MetricsTestSupport.Deadline);
            listener.Collect();
            Assert.AreEqual(1d, Active(listener, "scheduled"));
            Assert.IsFalse(dispatch.IsCompleted);
            Assert.IsEmpty(listener.Points("cis.jobs.invocation.duration"));
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1250));
        }
        finally
        {
            cleanup.TrySetResult();
            await dispatch.WaitAsync(MetricsTestSupport.Deadline);
        }

        MetricsPoint completed = listener.Points("cis.jobs.invocation.duration").Single();
        Assert.AreEqual(1.25d, (double)completed.Value);
        MetricsTestSupport.AssertTags(completed, new("mode", "scheduled"), new("outcome", "success"));
        listener.Collect();
        Assert.AreEqual(0d, Active(listener, "scheduled"));
    }

    [TestMethod]
    public async Task NewRejectedConfigurationFlagsHealthUntilACompleteLaterRevisionIsAccepted()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        await using var health = new HealthcheckService(new(), new()
        {
            Name = "job-configuration-test",
            InstanceId = Guid.NewGuid()
        }, fixture.Clock);
        await using var instrumentation = new JobInstrumentation(new JobsComposition(), fixture.Runtime, fixture.Clock);
        await fixture.Runtime.StartAsync();
        instrumentation.ActivateHealth(health, true);
        await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Healthy);

        fixture.Runtime.RejectConfiguration(2, "jobs.configuration.invalid");
        await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Unhealthy);
        Assert.AreEqual(1L, fixture.Runtime.Configuration.Revision);
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(3)));
        await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Healthy);
        Assert.AreEqual(3L, fixture.Runtime.Configuration.Revision);
        Assert.AreEqual(0, fixture.Acquisitions);
    }

    private static double Active(MetricsTestListener listener, string mode)
        => (double)listener.Points("cis.jobs.active.invocations").Last(point => point.Tags.Single().Value as string == mode).Value;

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(MetricsTestSupport.Deadline);
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }
}
