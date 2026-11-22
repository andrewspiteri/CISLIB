using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Jobs;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Continuous;

[TestClass]
public sealed class ContinuousJobRunnerTests
{
    [TestMethod]
    public async Task DrainBudgetChangesBetweenIterationsWithoutRewritingTheRunningIterationBudget()
    {
        await using var fixture = new JobRuntimeTestSupport();
        int calls = 0;
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Control.Callback = async (context, _) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                Assert.AreEqual(TimeSpan.FromSeconds(7), context.Settings.MaximumDrain);
                secondEntered.TrySetResult();
                await fixture.Control.WorkRelease.Task;
            }

            return new(ContinuousIterationOutcome.Idle);
        };
        ManualCoordinationLease lease = fixture.EnqueueGrant();
        await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition");
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Runtime.CaptureSnapshot().Any(item => item.State == JobLocalState.Idle));
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(1, settings: id =>
            new("wallet-service", id, new(JobExecutionMode.Continuous, maximumDrain: TimeSpan.FromSeconds(7)), enabled: true))));
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(2, settings: id =>
            new("wallet-service", id, new(JobExecutionMode.Continuous, maximumDrain: TimeSpan.FromSeconds(9)), enabled: true))));
        Task stop = fixture.Runner.StopAsync("wallet-service", "wallet.compaction", "partition");
        Assert.AreEqual(TimeSpan.FromSeconds(7), lease.DrainBudget);
        fixture.Control.WorkRelease.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task RemovingAJobWhileAStoreReplyIsPendingIsNormalDrainRatherThanAFalseDutyFault()
    {
        var held = new HeldUnavailableService();
        await using var fixture = new JobRuntimeTestSupport(coordinationService: held);
        try
        {
            Assert.AreEqual(ContinuousJobStartOutcome.Started,
                await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(new(1, enabled: true)));
            held.Release.TrySetResult();
            await JobRuntimeTestSupport.UntilAsync(() => fixture.Runtime.CaptureStatus().DutyReservations == 0);
            Assert.AreEqual(JobLocalState.Released, fixture.Runtime.CaptureSnapshot().Single().State);
            Assert.AreEqual(0, fixture.Control.Invocations.Count);
        }
        finally { held.Release.TrySetResult(); }
    }

    [TestMethod]
    public async Task UnknownCommitAndCleanupFailureRemainTwoIndependentFactsWithoutAReplay()
    {
        await using var fixture = new JobRuntimeTestSupport();
        fixture.Control.ThrowCleanup = true;
        var observer = new JobTestObserver();
        fixture.Runtime.AttachObserver(observer);
        var unknown = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested, true, true, true);
        fixture.Control.Callback = (_, _) => Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Indeterminate, completion: unknown));
        fixture.EnqueueGrant();
        Assert.AreEqual(ContinuousJobStartOutcome.Started,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Runtime.CaptureStatus().DutyReservations == 0);
        Assert.AreEqual("jobs.business_indeterminate_scope_cleanup_failed", fixture.Runtime.CaptureSnapshot().Single().LastOutcome);
        Assert.AreEqual("jobs.scope_cleanup_failed", fixture.Runtime.CaptureStatus().LastCleanupDiagnostic);
        Assert.AreEqual("indeterminate", observer.Completed.Single().Outcome);
        Assert.AreEqual(1, fixture.Control.Invocations.Count);
    }

    [TestMethod]
    public async Task IdleRetainsOwnershipButReleasesCapacityAndEachIterationGetsANewOwnedScope()
    {
        await using var fixture = new JobRuntimeTestSupport(["first", "second"], new(maximumInvocations: 1));
        using var firstCaller = new CancellationTokenSource();
        ManualCoordinationLease first = fixture.EnqueueGrant("first");
        Assert.AreEqual(ContinuousJobStartOutcome.Started, await fixture.Runner.StartAsync("wallet-service", "first", "partition", firstCaller.Token));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Runtime.CaptureSnapshot().Any(item => item.State == JobLocalState.Idle));
        firstCaller.Cancel();
        ManualCoordinationLease second = fixture.EnqueueGrant("second");
        Assert.AreEqual(ContinuousJobStartOutcome.Started, await fixture.Runner.StartAsync("wallet-service", "second", "partition"));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Control.Invocations.Count == 2 && fixture.Runtime.CaptureStatus().ActiveContinuousInvocations == 0);
        Assert.AreEqual(CoordinationLeaseState.Owned, first.State);
        Assert.AreEqual(CoordinationLeaseState.Owned, second.State);
        Assert.AreEqual(2, fixture.Runtime.CaptureStatus().ContinuousDuties);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().InvocationReservations);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Control.Invocations.Count == 4 && fixture.Runtime.CaptureStatus().ActiveContinuousInvocations == 0);
        Assert.AreEqual(2, fixture.Coordination.Calls.Count, "Idle iterations retain their original grants rather than reacquiring.");
        Assert.AreEqual(8, fixture.Control.CreatedScopes, "Each duty has an initial eligibility scope, acquisition scope and two distinct iteration scopes.");
        Assert.AreEqual(fixture.Control.CreatedScopes, fixture.Control.DisposedScopes);
    }

    [TestMethod]
    public async Task MoreWorkYieldsTheSharedSlotToAnAlreadyWaitingEligibleDuty()
    {
        await using var fixture = new JobRuntimeTestSupport(["first", "second"], new(maximumInvocations: 1));
        int firstCalls = 0;
        fixture.Control.Callback = async (context, _) =>
        {
            if (context.Definition.JobId == "first" && Interlocked.Increment(ref firstCalls) == 1)
            {
                await fixture.Control.WorkRelease.Task;
                return new(ContinuousIterationOutcome.MoreWork);
            }

            return new(ContinuousIterationOutcome.Idle);
        };
        fixture.EnqueueGrant("first");
        Assert.AreEqual(ContinuousJobStartOutcome.Started, await fixture.Runner.StartAsync("wallet-service", "first", "partition"));
        await fixture.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.EnqueueGrant("second");
        Assert.AreEqual(ContinuousJobStartOutcome.Started, await fixture.Runner.StartAsync("wallet-service", "second", "partition"));
        Assert.AreEqual(1, fixture.Control.Invocations.Count);
        fixture.Control.WorkRelease.TrySetResult();

        await JobRuntimeTestSupport.UntilAsync(() => fixture.Control.Invocations.Count >= 3);
        CollectionAssert.AreEqual(new[] { "first", "second", "first" }, fixture.Control.Invocations.Take(3).ToArray());
    }

    [TestMethod]
    public async Task RegisteredExecutionPermissionsAreCheckedBeforeAnyAcquisition()
    {
        await using var fixture = new JobRuntimeTestSupport();
        fixture.Control.Authorized = false;
        Assert.AreEqual(ContinuousJobStartOutcome.Unauthorized,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        Assert.AreEqual(0, fixture.Coordination.Calls.Count);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
        CollectionAssert.AreEqual(new[] { "jobs.execute" }, fixture.Control.AuthorizationCapabilities.Single());
        Assert.AreEqual(fixture.Control.CreatedScopes, fixture.Control.DisposedScopes);
    }

    [TestMethod]
    public async Task UnknownBusinessCommitFaultsOnlyThatDutyAndIsNeverAutomaticallyReplayed()
    {
        await using var fixture = new JobRuntimeTestSupport(["first", "second"]);
        var unknown = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested, true, true, true);
        fixture.Control.Callback = (context, _) => Task.FromResult(context.Definition.JobId == "first"
            ? new ContinuousIterationResult(ContinuousIterationOutcome.Indeterminate, completion: unknown)
            : new ContinuousIterationResult(ContinuousIterationOutcome.Idle));
        fixture.EnqueueGrant("first");
        Assert.AreEqual(ContinuousJobStartOutcome.Started, await fixture.Runner.StartAsync("wallet-service", "first", "partition"));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Runtime.CaptureStatus().DutyReservations == 0);
        Assert.AreEqual("jobs.business_indeterminate", fixture.Runtime.CaptureSnapshot().Single().LastOutcome);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, fixture.Coordination.Calls.Count);
        Assert.AreEqual(1, fixture.Control.Invocations.Count);

        fixture.EnqueueGrant("second");
        Assert.AreEqual(ContinuousJobStartOutcome.Started, await fixture.Runner.StartAsync("wallet-service", "second", "partition"));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Control.Invocations.Contains("second"));
    }

    [TestMethod]
    public async Task ACallbackIgnoringLossRetainsItsKeySlotAndScopeUntilActualCompletion()
    {
        await using var fixture = new JobRuntimeTestSupport();
        fixture.Control.Callback = async (_, _) =>
        {
            await fixture.Control.WorkRelease.Task;
            return new(ContinuousIterationOutcome.Idle);
        };
        ManualCoordinationLease lease = fixture.EnqueueGrant();
        Assert.AreEqual(ContinuousJobStartOutcome.Started,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        await fixture.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await lease.LoseAsync(CoordinationLossReason.ShutdownDeadlineExceeded);
        Task stop = fixture.Runner.StopAsync("wallet-service", "wallet.compaction", "partition");
        Assert.IsFalse(stop.IsCompleted);
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveContinuousInvocations);
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().InvocationReservations);
        Assert.AreEqual(ContinuousJobStartOutcome.AlreadyRunning,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        Assert.AreEqual(2, fixture.Control.DisposedScopes, "The entered callback still owns the third scope.");

        fixture.Control.WorkRelease.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().ActiveContinuousInvocations);
        Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
        Assert.AreEqual(3, fixture.Control.DisposedScopes);
    }

    [TestMethod]
    public async Task KnownCommittedIterationSurvivesLossAndCleanupFaultWhileActualCleanupKeepsTheSlot()
    {
        await using var fixture = new JobRuntimeTestSupport();
        fixture.Control.HoldCleanup = true;
        fixture.Control.ThrowCleanup = true;
        var observer = new JobTestObserver();
        fixture.Runtime.AttachObserver(observer);
        var committed = new TransactionCompletion(TransactionOutcome.Committed, TransactionEndReason.CommitRequested, true, false, false);
        fixture.Control.Callback = async (_, _) =>
        {
            await fixture.Control.WorkRelease.Task;
            return new(ContinuousIterationOutcome.MoreWork, completion: committed);
        };
        ManualCoordinationLease lease = fixture.EnqueueGrant();
        Assert.AreEqual(ContinuousJobStartOutcome.Started,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        await fixture.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await lease.LoseAsync(CoordinationLossReason.RenewalDeadlineExceeded);
        fixture.Control.WorkRelease.TrySetResult();
        await fixture.Control.ScopeCleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveContinuousInvocations);
        Assert.AreEqual(0, observer.Completed.Count);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Task stop = fixture.Runner.StopAsync("wallet-service", "wallet.compaction", "partition");
        Assert.IsFalse(stop.IsCompleted);
        fixture.Control.ScopeCleanupRelease.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual("success", observer.Completed.Single().Outcome);
        Assert.AreEqual(1.0, observer.Completed.Single().Seconds);
        Assert.AreEqual("jobs.scope_cleanup_failed", fixture.Runtime.CaptureStatus().LastCleanupDiagnostic);
        Assert.AreEqual("jobs.committed_scope_cleanup_failed", fixture.Runtime.CaptureSnapshot().Single().LastOutcome);
        Assert.AreEqual(1, fixture.Control.Invocations.Count);
    }

    [TestMethod]
    public async Task OnlyKnownNotCommittedAcquisitionFailuresRetryAndIndeterminateStopsWithoutBusinessWork()
    {
        await using var fixture = new JobRuntimeTestSupport();
        fixture.Coordination.Enqueue(new(CoordinationAcquireOutcome.Unavailable));
        fixture.Coordination.Enqueue(new(CoordinationAcquireOutcome.Indeterminate));
        Assert.AreEqual(ContinuousJobStartOutcome.Started,
            await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition"));
        Assert.AreEqual(1, fixture.Coordination.Calls.Count);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await JobRuntimeTestSupport.UntilAsync(() => fixture.Runtime.CaptureStatus().DutyReservations == 0);
        Assert.AreEqual(2, fixture.Coordination.Calls.Count);
        Assert.AreEqual(0, fixture.Control.Invocations.Count);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.AreEqual(2, fixture.Coordination.Calls.Count);
        Assert.AreEqual("jobs.acquisition_indeterminate", fixture.Runtime.CaptureSnapshot().Single().LastOutcome);
    }

    private sealed class HeldUnavailableService : ICoordinationService
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ManagedCoordinationResult> AcquireAsync(CoordinationRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task;
            return new(CoordinationAcquireOutcome.Busy);
        }
    }
}
