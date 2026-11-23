using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Runtime;

[TestClass]
public sealed class JobRuntimeAdmissionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CallbackEntryRechecksReducedHostOrReassignedCategoryCapacityAfterPreparation(bool categoryChange)
    {
        await using var fixture = new JobRuntimeTestSupport(["first", "second"], new(maximumInvocations: 2));
        JobCategorySettings[] categories = [new("wallet-service", "maintenance", maximumInvocations: categoryChange ? 1 : 2),
            new("wallet-service", "other", maximumInvocations: 1)];
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(1, categories: categories, settings: id =>
            new("wallet-service", id, new(JobExecutionMode.Continuous), enabled: true,
                categoryId: categoryChange && id == "second" ? "other" : "maintenance"))));
        await fixture.Runtime.StartAsync();
        await using JobDutyReservation first = fixture.Runtime.TryReserveContinuous(new("wallet-service", "first", "partition", JobExecutionMode.Continuous)).Duty!;
        await using JobDutyReservation second = fixture.Runtime.TryReserveContinuous(new("wallet-service", "second", "partition", JobExecutionMode.Continuous)).Duty!;
        await using ManualCoordinationLease firstLease = fixture.Grant("first");
        await using ManualCoordinationLease secondLease = fixture.Grant("second");
        Assert.IsTrue(fixture.Runtime.AttachLease(first, firstLease));
        Assert.IsTrue(fixture.Runtime.AttachLease(second, secondLease));
        using JobInvocationReservation firstInvocation = fixture.Runtime.TryReserveIteration(first)!;
        using JobInvocationReservation prepared = fixture.Runtime.TryReserveIteration(second)!;
        Assert.IsTrue(fixture.Runtime.TryEnterInvocation(firstInvocation));
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(2,
            limits: new(maximumInvocations: categoryChange ? 2 : 1), categories: categories)));

        Assert.IsFalse(fixture.Runtime.TryEnterInvocation(prepared));
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveContinuousInvocations);
        Assert.AreEqual(2, fixture.Runtime.CaptureStatus().InvocationReservations);
        firstInvocation.Complete("success");
        Assert.IsTrue(fixture.Runtime.TryEnterInvocation(prepared));
        prepared.Complete("success");
    }

    [TestMethod]
    public void DrainSettingsFitTheActualManagedLeaseBoundaryWithoutShorteningIdleOrTimeoutSettings()
    {
        Assert.AreEqual(TimeSpan.FromDays(1), new JobExecutionSettings(JobExecutionMode.Continuous,
            maximumDrain: TimeSpan.FromDays(1)).MaximumDrain);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new JobExecutionSettings(JobExecutionMode.Continuous,
            maximumDrain: TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)));
        var settings = new JobExecutionSettings(JobExecutionMode.Continuous, idleDelay: TimeSpan.FromDays(49), timeout: TimeSpan.FromDays(49));
        Assert.AreEqual(TimeSpan.FromDays(49), settings.IdleDelay);
        Assert.AreEqual(TimeSpan.FromDays(49), settings.Timeout);
    }

    [TestMethod]
    public async Task CategoryReassignmentAndReducedLimitsCountAlreadyAdmittedActualInvocations()
    {
        await using var fixture = new JobRuntimeTestSupport(["first", "second", "third"], new(maximumInvocations: 3));
        JobCategorySettings[] categories = [new("wallet-service", "maintenance", maximumInvocations: 1), new("wallet-service", "other", maximumInvocations: 1)];
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(1, categories: categories)));
        await fixture.Runtime.StartAsync();
        await using JobDutyReservation first = fixture.Runtime.TryReserveContinuous(new("wallet-service", "first", "partition", JobExecutionMode.Continuous)).Duty!;
        await using JobDutyReservation second = fixture.Runtime.TryReserveContinuous(new("wallet-service", "second", "partition", JobExecutionMode.Continuous)).Duty!;
        await using JobDutyReservation third = fixture.Runtime.TryReserveContinuous(new("wallet-service", "third", "partition", JobExecutionMode.Continuous)).Duty!;
        await using ManualCoordinationLease firstLease = fixture.Grant("first");
        await using ManualCoordinationLease thirdLease = fixture.Grant("third");
        Assert.IsTrue(fixture.Runtime.AttachLease(first, firstLease));
        Assert.IsTrue(fixture.Runtime.AttachLease(third, thirdLease));
        using JobInvocationReservation firstInvocation = fixture.Runtime.TryReserveIteration(first)!;
        Assert.IsTrue(fixture.Runtime.TryEnterInvocation(firstInvocation));
        Assert.IsNull(fixture.Runtime.TryReserveIteration(second));

        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(2, categories: categories, settings: id =>
            new("wallet-service", id, new(JobExecutionMode.Continuous), enabled: true, categoryId: id == "third" ? "maintenance" : "other"))));
        Assert.IsNull(fixture.Runtime.TryReserveIteration(second), "The running first invocation moved into current category accounting.");
        using JobInvocationReservation thirdInvocation = fixture.Runtime.TryReserveIteration(third)!;
        Assert.IsTrue(fixture.Runtime.TryEnterInvocation(thirdInvocation));
        Assert.AreEqual(2, fixture.Runtime.CaptureStatus().ActiveContinuousInvocations);

        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(3, limits: new(maximumInvocations: 1), categories: categories)));
        Assert.IsNull(fixture.Runtime.TryReserveIteration(second));
        firstInvocation.Complete("success");
        thirdInvocation.Complete("success");
    }

    [TestMethod]
    public async Task DutyAndStopCompletionWaitForActualCancellationCallbacksBeforeReleasingTheKey()
    {
        await using var fixture = new JobRuntimeTestSupport();
        await fixture.Runtime.StartAsync();
        await using JobDutyReservation duty = fixture.Runtime.TryReserveContinuous(new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous)).Duty!;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration callback = duty.StopRequested.Register(() =>
        {
            entered.TrySetResult();
            release.Wait();
        });
        try
        {
            duty.RequestDrain();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task disposal = duty.DisposeAsync().AsTask();
            Task stop = fixture.Runtime.StopAsync();
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(duty.Completion.IsCompleted);
            Assert.AreEqual(1, fixture.Runtime.CaptureStatus().DutyReservations);
            release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(10));
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public async Task InvalidWholeRevisionAndLateInitialCapturePreserveTheLastAcceptedConfiguration()
    {
        await using var fixture = new JobRuntimeTestSupport();
        JobConfigurationSnapshot accepted = fixture.Configuration(1);
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(accepted));
        Assert.IsFalse(fixture.Runtime.InstallInitialConfiguration(fixture.Configuration(0)));
        var invalid = new JobConfigurationSnapshot(2, true, jobs:
            [new("wallet-service", "unknown", new(JobExecutionMode.Continuous), enabled: true)]);
        Assert.IsFalse(fixture.Runtime.TryApplyConfiguration(invalid));
        Assert.AreSame(accepted, fixture.Runtime.Configuration);
        Assert.AreEqual(2L, fixture.Runtime.RejectedConfiguration!.Revision);
        await fixture.Runtime.StartAsync();
        await fixture.Runtime.StopAsync();
        Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Runtime.StartAsync());
    }

    [TestMethod]
    public async Task DiagnosticClockFailureDoesNotLeakAConfirmedLeaseOrFabricateAnOwnerTimestamp()
    {
        await using var fixture = new JobRuntimeTestSupport(runtimeClock: new ThrowingUtcClock());
        await fixture.Runtime.StartAsync();
        await using JobDutyReservation duty = fixture.Runtime.TryReserveContinuous(new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous)).Duty!;
        await using ManualCoordinationLease lease = fixture.Grant();
        Assert.IsTrue(fixture.Runtime.AttachLease(duty, lease));
        Assert.AreSame(lease, duty.Lease);
        Assert.IsNull(fixture.Runtime.CaptureSnapshot()[0].ObservedOwner);
    }

    [TestMethod]
    public async Task MismatchedGrantForAnotherResourceIsRejectedWithoutReleasingThatOtherGrant()
    {
        await using var fixture = new JobRuntimeTestSupport();
        await fixture.Runtime.StartAsync();
        await using JobDutyReservation duty = fixture.Runtime.TryReserveContinuous(new("wallet-service", "wallet.compaction", "partition", JobExecutionMode.Continuous)).Duty!;
        await using ManualCoordinationLease foreign = fixture.Grant(resource: "another-resource");
        fixture.Coordination.Enqueue(new(CoordinationAcquireOutcome.Acquired, foreign));
        ManagedCoordinationResult result = await fixture.Runtime.AcquireAsync(duty, default);
        Assert.AreEqual(CoordinationAcquireOutcome.InvalidRequest, result.Outcome);
        Assert.AreEqual(CoordinationLeaseState.Owned, foreign.State);
        Assert.IsNull(duty.Lease);
    }

    [TestMethod]
    public async Task LatestFaultObservationIsBoundedPerDefinitionAndRetainedAcrossHundredsOfJobs()
    {
        string[] jobs = Enumerable.Range(0, 200).Select(index => "job" + index.ToString("D3")).ToArray();
        await using var fixture = new JobRuntimeTestSupport(jobs);
        await fixture.Runtime.StartAsync();
        foreach (string job in jobs)
        {
            await using JobDutyReservation duty = fixture.Runtime.TryReserveContinuous(new("wallet-service", job, "partition", JobExecutionMode.Continuous)).Duty!;
            duty.SetState(JobLocalState.Faulted, "jobs.controlled_fault");
        }

        JobRuntimeSnapshot[] snapshots = fixture.Runtime.CaptureSnapshot().Concat(fixture.Runtime.CaptureSnapshot(100)).ToArray();
        Assert.AreEqual(200, snapshots.Length);
        Assert.IsTrue(snapshots.All(snapshot => snapshot.State == JobLocalState.Faulted));
        Assert.AreEqual(200, snapshots.Select(snapshot => snapshot.Target.JobId).Distinct().Count());
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
    }

    [TestMethod]
    public async Task ResourceFailureBoundDefersOnlyNewResourcesOfThatJobAndPreservesSameResourceRecovery()
    {
        await using var fixture = new JobRuntimeTestSupport(["failed", "unrelated"],
            new(maximumContinuousDuties: 1, maximumInvocations: 1));
        await fixture.Runtime.StartAsync();
        foreach (string resource in new[] { "partition-A", "partition-B" })
        {
            await using JobDutyReservation duty = fixture.Runtime.TryReserveContinuous(
                new("wallet-service", "failed", resource, JobExecutionMode.Continuous)).Duty!;
            duty.SetState(JobLocalState.Faulted, "jobs.controlled_fault");
        }

        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(1,
            limits: new(maximumContinuousDuties: 8, maximumInvocations: 8))));
        Assert.AreEqual(JobAdmissionOutcome.Deferred, fixture.Runtime.TryReserveContinuous(
            new("wallet-service", "failed", "partition-C", JobExecutionMode.Continuous)).Outcome,
            "Revision changes do not expand or evict the catalog's fixed resource diagnostic bound.");
        await using (JobDutyReservation unrelated = fixture.Runtime.TryReserveContinuous(
            new("wallet-service", "unrelated", "partition-C", JobExecutionMode.Continuous)).Duty!)
        {
            Assert.IsNotNull(unrelated);
        }

        await using (JobDutyReservation recovered = fixture.Runtime.TryReserveContinuous(
            new("wallet-service", "failed", "partition-A", JobExecutionMode.Continuous)).Duty!)
        {
            Assert.IsNotNull(recovered);
            Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy);
            await using ManualCoordinationLease lease = fixture.Grant("failed", "partition-A");
            Assert.IsTrue(fixture.Runtime.AttachLease(recovered, lease));
            using JobInvocationReservation invocation = fixture.Runtime.TryReserveIteration(recovered)!;
            Assert.IsTrue(fixture.Runtime.TryEnterInvocation(invocation));
            invocation.Complete("success");
        }

        await using JobDutyReservation next = fixture.Runtime.TryReserveContinuous(
            new("wallet-service", "failed", "partition-C", JobExecutionMode.Continuous)).Duty!;
        Assert.IsNotNull(next);
        Assert.IsTrue(fixture.Runtime.CaptureHealth().Unhealthy, "Partition-B remains unresolved after partition-A recovers.");
        Assert.HasCount(1, fixture.Runtime.CaptureSnapshot().Where(snapshot => snapshot.Target.JobId == "failed"
            && snapshot.State == JobLocalState.Faulted).ToArray());
    }

    private sealed class ThrowingUtcClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Controlled diagnostic clock failure.");
    }

}
