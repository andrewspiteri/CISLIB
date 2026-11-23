using CIS.Coordination;
using CIS.Jobs;
using CIS.Tests.UnitTests.Jobs.Scheduled;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs;

[TestClass]
public sealed class JobPartitionHealthTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task AnotherPartitionCannotHideFailureAndRecoveryWaitsForTheSameResourcesActualCleanup()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        TaskCompletionSource<ScheduledJobResult> healthyCallback = fixture.Hold(new ScheduledJobResult(ScheduledJobOutcome.Completed));
        fixture.Handler = (context, _, _) => context.ResourceId == "partition-A"
            ? Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Failed)) : healthyCallback.Task;
        await fixture.Runtime.StartAsync();
        Task<ScheduledJobResult> healthy = fixture.Dispatcher.DispatchAsync(fixture.Run("partition-B"));
        await fixture.CallbackStarted.Task.WaitAsync(Deadline);

        Assert.AreEqual(ScheduledJobOutcome.Failed,
            (await fixture.Dispatcher.DispatchAsync(fixture.Run("partition-A")).WaitAsync(Deadline)).Outcome);
        Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy);
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveScheduledInvocations);
        Assert.IsTrue(fixture.Runtime.CaptureSnapshot().Any(snapshot => snapshot.Target.ResourceId == "partition-A"
            && snapshot.State == JobLocalState.Faulted));

        healthyCallback.TrySetResult(new(ScheduledJobOutcome.Completed));
        Assert.AreEqual(ScheduledJobOutcome.Completed, (await healthy.WaitAsync(Deadline)).Outcome);
        Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy,
            "Another resource's successful completion must not overwrite the unresolved failure.");
        Assert.IsTrue(fixture.Runtime.CaptureSnapshot().Any(snapshot => snapshot.Target.ResourceId == "partition-A"
            && snapshot.State == JobLocalState.Faulted));

        TaskCompletionSource cleanup = fixture.Hold();
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool recoveringCallbackReturned = false;
        fixture.Handler = (_, _, _) =>
        {
            recoveringCallbackReturned = true;
            return Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Completed));
        };
        fixture.Cleanup = _ =>
        {
            if (recoveringCallbackReturned)
            {
                cleanupEntered.TrySetResult();
                return new(cleanup.Task);
            }

            return ValueTask.CompletedTask;
        };
        Task<ScheduledJobResult> recovery = fixture.Dispatcher.DispatchAsync(fixture.Run("partition-A", "recovery"));
        await cleanupEntered.Task.WaitAsync(Deadline);
        Assert.IsFalse(recovery.IsCompleted);
        Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy,
            "A replacement callback does not recover the resource before its actual scope cleanup settles.");
        cleanup.TrySetResult();
        Assert.AreEqual(ScheduledJobOutcome.Completed, (await recovery.WaitAsync(Deadline)).Outcome);
        Assert.IsFalse(fixture.Runtime.CaptureHealth().Unhealthy);
    }

    [TestMethod]
    public async Task BusyAdmissionAndFailedRecoveryCleanupCannotClearAnExistingResourceFailure()
    {
        await using var fixture = new ScheduledDispatcherFixture();
        fixture.Handler = (_, _, _) => Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Failed));
        await fixture.Runtime.StartAsync();
        Assert.AreEqual(ScheduledJobOutcome.Failed, (await fixture.Dispatcher.DispatchAsync(fixture.Run())).Outcome);

        fixture.Acquire = (_, _) => Task.FromResult(new ManagedCoordinationResult(CoordinationAcquireOutcome.Busy));
        Assert.AreEqual(ScheduledJobOutcome.Busy, (await fixture.Dispatcher.DispatchAsync(fixture.Run(occurrence: "busy"))).Outcome);
        Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy);

        fixture.Acquire = null;
        bool callbackReturned = false;
        fixture.Handler = (_, _, _) =>
        {
            callbackReturned = true;
            return Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Completed));
        };
        fixture.Cleanup = _ => callbackReturned
            ? throw new InvalidOperationException("Controlled work-scope cleanup failure.") : ValueTask.CompletedTask;
        Assert.AreEqual(ScheduledJobOutcome.Completed,
            (await fixture.Dispatcher.DispatchAsync(fixture.Run(occurrence: "failed-cleanup"))).Outcome,
            "Cleanup diagnostics preserve the handler's original completion result.");
        Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy);

        fixture.Cleanup = null;
        Assert.AreEqual(ScheduledJobOutcome.Completed,
            (await fixture.Dispatcher.DispatchAsync(fixture.Run(occurrence: "recovered"))).Outcome);
        Assert.IsFalse(fixture.Runtime.CaptureHealth().Unhealthy);
    }
}
