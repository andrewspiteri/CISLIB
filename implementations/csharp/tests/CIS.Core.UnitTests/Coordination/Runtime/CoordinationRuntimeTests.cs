using System.Collections.Concurrent;
using CIS.CommonTesting;
using CIS.Coordination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Coordination;

[TestClass]
public sealed class CoordinationRuntimeTests
{
    [TestMethod]
    public async Task ConstructionIsPassiveStartupIsOnceAndFailedStartupNeverAdmits()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        await using (var runtime = new CoordinationRuntime(provider, Options(), time))
        {
            Assert.AreEqual(0, provider.Validations);
            Assert.AreEqual(CoordinationRuntimeState.Created, runtime.GetSnapshot().State);
            await runtime.StartAsync();
            await runtime.StartAsync();
            Assert.AreEqual(1, provider.Validations);
        }

        var failed = new ControlledProvider(time) { ValidationFailure = new InvalidOperationException("uninstalled") };
        await using var unavailable = new CoordinationRuntime(failed, Options(), time);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => unavailable.StartAsync());
        Assert.AreEqual(CoordinationAcquireOutcome.Unavailable, (await unavailable.AcquireAuthorizedAsync(Acquisition())).Outcome);
        Assert.AreEqual(0, failed.Acquires);
        Assert.IsFalse(unavailable.GetSnapshot().AuthorityAvailable);
    }

    [TestMethod]
    public async Task CancelledStartupNeverValidatesOrActivatesAfterIgnoredValidationCancellation()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        await using (var notStarted = new CoordinationRuntime(provider, Options(), time))
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => notStarted.StartAsync(preCancelled.Token));
            Assert.AreEqual(0, provider.Validations);
            Assert.AreEqual(CoordinationRuntimeState.Created, notStarted.GetSnapshot().State);
        }

        var validation = Source();
        provider.Validation = _ => validation.Task;
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => validation.TrySetResult());
        using var caller = new CancellationTokenSource();
        Task starting = runtime.StartAsync(caller.Token);
        await UntilAsync(() => provider.Validations == 1);
        caller.Cancel();
        validation.TrySetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => starting);
        Assert.AreNotEqual(CoordinationRuntimeState.Running, runtime.GetSnapshot().State);
        Assert.AreEqual(CoordinationAcquireOutcome.Unavailable, (await runtime.AcquireAuthorizedAsync(Acquisition())).Outcome);
        Assert.AreEqual(0, provider.Acquires);
        await runtime.StopAsync();
    }

    [TestMethod]
    public async Task SnapshotNeverStartsRenewalOrCancellationAndCountsAllOwnedHandlesBeyondPage()
    {
        var time = new PausableSchedulerClock();
        var provider = new ControlledProvider(time);
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        await runtime.StartAsync();
        ICoordinationLease first = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        await UntilAsync(() => runtime.GetSnapshot().AcquisitionRequests == 0);
        ICoordinationLease second = (await runtime.AcquireAuthorizedAsync(Acquisition("other"))).Lease!;
        time.PauseScheduler = true;
        time.Advance(TimeSpan.FromSeconds(9));
        CoordinationRuntimeSnapshot page = runtime.GetSnapshot(take: 1);
        Assert.HasCount(1, page.Entries);
        Assert.AreEqual(2, page.OwnedLeases);
        Assert.AreEqual(0, provider.Renewals);
        Assert.IsFalse(first.OwnershipLost.IsCancellationRequested);
        Assert.AreEqual(CoordinationLeaseState.Lost, first.State);
        Assert.AreEqual(CoordinationLeaseState.Lost, second.State);
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    [TestMethod]
    public async Task AcknowledgementUsesRequestStartAndClampsRemoteValidity()
    {
        var time = new PausableSchedulerClock();
        var provider = new ControlledProvider(time);
        var acquired = Source<CoordinationAcquireResult>();
        provider.Acquire = (_, _) => acquired.Task;
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => acquired.TrySetResult(new(CoordinationAcquireOutcome.Unavailable)));
        await runtime.StartAsync();
        CoordinationAcquisition identity = Acquisition();
        Task<ManagedCoordinationResult> acquiring = runtime.AcquireAuthorizedAsync(identity);
        await provider.AcquireStarted.Task.WaitAsync(TestTimeout);
        time.Advance(TimeSpan.FromMilliseconds(500));
        acquired.SetResult(new(CoordinationAcquireOutcome.Acquired, provider.Grant(identity), TimeSpan.FromHours(1)));
        ICoordinationLease lease = (await acquiring).Lease!;
        time.PauseScheduler = true;
        time.SetUtcNow(time.GetUtcNow().AddYears(-10));
        time.Advance(TimeSpan.FromMilliseconds(7500));
        Assert.AreEqual(CoordinationLeaseState.Lost, lease.State);
        Assert.IsTrue(lease.OwnershipLost.IsCancellationRequested);
        Assert.AreEqual(0, provider.Renewals);
        await lease.DisposeAsync();
    }

    [TestMethod]
    public async Task IndeterminateAcquireRecoversOnlyExactReadWithoutMutationReplay()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        CoordinationAcquisition identity = Acquisition();
        provider.Acquire = (_, _) => Task.FromResult(new CoordinationAcquireResult(CoordinationAcquireOutcome.Indeterminate));
        provider.Resolve = (_, _) => Task.FromResult(new CoordinationObservation(CoordinationObservationOutcome.FoundLive,
            provider.Grant(identity), TimeSpan.FromSeconds(7)));
        CoordinationOptions options = Options();
        options.MaximumConcurrentAcquisitions = 2;
        await using var runtime = new CoordinationRuntime(provider, options, time);
        using var drain = new DrainOnExit(runtime, time);
        await runtime.StartAsync();
        ManagedCoordinationResult result = await runtime.AcquireAuthorizedAsync(identity);
        Assert.AreEqual(CoordinationAcquireOutcome.Acquired, result.Outcome);
        Assert.AreEqual(1, provider.Acquires);
        Assert.AreEqual(1, provider.Resolutions);
        Assert.AreEqual(identity, result.Lease!.Grant.Acquisition);
        await result.Lease.DisposeAsync();
    }

    [TestMethod]
    public async Task MissingObservationDoesNotAuthorizeReplayOrErasePendingMutation()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        var pending = Source<CoordinationAcquireResult>();
        provider.Acquire = (_, _) => pending.Task;
        provider.Resolve = (_, _) => Task.FromResult(new CoordinationObservation(CoordinationObservationOutcome.NotObserved));
        CoordinationOptions options = Options();
        options.MaximumConcurrentAcquisitions = 2;
        await using var runtime = new CoordinationRuntime(provider, options, time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => pending.TrySetResult(new(CoordinationAcquireOutcome.Unavailable)));
        await runtime.StartAsync();
        CoordinationAcquisition identity = Acquisition();
        Task<ManagedCoordinationResult> attempt = runtime.AcquireAuthorizedAsync(identity);
        await provider.AcquireStarted.Task.WaitAsync(TestTimeout);
        time.Advance(options.RequestTimeout);
        await provider.ResolveStarted.Task.WaitAsync(TestTimeout);
        await UntilAsync(() =>
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            return attempt.IsCompleted;
        });

        Assert.AreEqual(CoordinationAcquireOutcome.Indeterminate, (await attempt.WaitAsync(TestTimeout)).Outcome);
        Assert.AreEqual(1, provider.Acquires);
        Assert.IsTrue(provider.Resolutions > 0);
        Assert.AreEqual(1, runtime.GetSnapshot().AcquisitionRequests);
        Assert.AreEqual(1, runtime.GetSnapshot().ManagedReservations);
        pending.SetResult(new(CoordinationAcquireOutcome.Acquired, provider.Grant(identity), TimeSpan.FromSeconds(10)));
        await provider.ReleaseStarted.Task.WaitAsync(TestTimeout);
        await runtime.StopAsync();
        Assert.AreEqual(0, provider.Renewals);
        Assert.AreEqual(0, runtime.GetSnapshot().ManagedReservations);
    }

    [TestMethod]
    public async Task CancelledAcquisitionRetainsActualCapacityAndLateGrantIsOnlyConditionallyReleased()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        var pending = Source<CoordinationAcquireResult>();
        provider.Acquire = (_, _) => pending.Task;
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => pending.TrySetResult(new(CoordinationAcquireOutcome.Unavailable)));
        await runtime.StartAsync();
        using var caller = new CancellationTokenSource();
        CoordinationAcquisition identity = Acquisition();
        Task<ManagedCoordinationResult> attempt = runtime.AcquireAuthorizedAsync(identity, caller.Token);
        await provider.AcquireStarted.Task.WaitAsync(TestTimeout);
        caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await attempt);
        Assert.AreEqual(1, runtime.GetSnapshot().AcquisitionRequests);
        Assert.AreEqual(1, runtime.GetSnapshot().ManagedReservations);
        Assert.AreEqual(CoordinationAcquireOutcome.CapacityExceeded,
            (await runtime.AcquireAuthorizedAsync(Acquisition("other"))).Outcome);
        Task stopping = runtime.StopAsync();
        Assert.IsFalse(stopping.IsCompleted);
        CoordinationGrant orphan = provider.Grant(identity);
        pending.SetResult(new(CoordinationAcquireOutcome.Acquired, orphan, TimeSpan.FromSeconds(10)));
        await provider.ReleaseStarted.Task.WaitAsync(TestTimeout);
        await stopping.WaitAsync(TestTimeout);
        Assert.AreEqual(orphan, provider.Released.Single());
        Assert.AreEqual(0, provider.Renewals);
    }

    [TestMethod]
    public async Task RenewalAckAfterPriorStopLosesEvenWhenExpirySchedulerIsPaused()
    {
        var time = new PausableSchedulerClock();
        var provider = new ControlledProvider(time);
        provider.Acquire = (request, _) => Task.FromResult(new CoordinationAcquireResult(CoordinationAcquireOutcome.Acquired,
            provider.Grant(request.Acquisition), TimeSpan.FromMilliseconds(4500)));
        var renewal = Source<CoordinationMutationResult>();
        provider.Renew = (_, _, _) => renewal.Task;
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => renewal.TrySetResult(new(CoordinationMutationOutcome.Indeterminate)));
        await runtime.StartAsync();
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        time.Advance(TimeSpan.FromSeconds(2));
        await provider.RenewStarted.Task.WaitAsync(TestTimeout);
        time.PauseAllTimers = true;
        time.Advance(TimeSpan.FromMilliseconds(600));
        renewal.SetResult(new(CoordinationMutationOutcome.Succeeded, provider.Refreshed(lease.Grant), TimeSpan.FromSeconds(10)));
        CoordinationLeaseTermination terminal = await lease.Termination.WaitAsync(TestTimeout);
        Assert.AreEqual(CoordinationLeaseState.Lost, terminal.State);
        Assert.AreEqual(CoordinationLossReason.RenewalDeadlineExceeded, terminal.LossReason);
        Assert.AreEqual(CoordinationLeaseState.Lost, lease.State);
        Assert.AreEqual(CoordinationMutationOutcome.Unavailable, (await lease.ReleaseAsync()).Outcome);
        await lease.DisposeAsync();
    }

    [TestMethod]
    public async Task UnconfirmedRenewalNeverExtendsDeadlineAndOneUnfinishedCallPreventsAnother()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        var renewal = Source<CoordinationMutationResult>();
        provider.Renew = (_, _, _) => renewal.Task;
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => renewal.TrySetResult(new(CoordinationMutationOutcome.Indeterminate)));
        await runtime.StartAsync();
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        CoordinationGrant original = lease.Grant;
        time.Advance(TimeSpan.FromSeconds(2));
        await provider.RenewStarted.Task.WaitAsync(TestTimeout);
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.AreEqual(CoordinationLeaseState.Lost, lease.State);
        Assert.AreEqual(1, provider.Renewals);
        Assert.AreEqual(1, runtime.GetSnapshot().RenewalRequests);
        Task stopping = runtime.StopAsync();
        Assert.IsFalse(stopping.IsCompleted);
        renewal.SetResult(new(CoordinationMutationOutcome.Succeeded, provider.Refreshed(original), TimeSpan.FromSeconds(10)));
        await stopping.WaitAsync(TestTimeout);
        Assert.AreEqual(original, lease.Grant);
        Assert.AreEqual(CoordinationLeaseState.Lost, lease.State);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidRenewalAcknowledgementNeverChangesExactIdentityOrConfirmedStop(bool missingGrant)
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        provider.Renew = (grant, _, _) => Task.FromResult(missingGrant
            ? new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded)
            : new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded,
                new CoordinationGrant(provider.AuthorityId, grant.Acquisition, grant.Generation + 1, time.GetUtcNow().AddSeconds(10)),
                TimeSpan.FromSeconds(10)));
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        await runtime.StartAsync();
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        CoordinationGrant original = lease.Grant;
        time.Advance(TimeSpan.FromSeconds(2));
        await provider.RenewStarted.Task.WaitAsync(TestTimeout);
        await DrainContinuations();
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.AreEqual(CoordinationLeaseState.Lost, lease.State);
        Assert.AreEqual(original, lease.Grant);
        await lease.DisposeAsync();
    }

    [TestMethod]
    public async Task BusyPreservesObservedOwnerAndExplicitRejectionCausesImmediatePermanentLoss()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        var observed = new CoordinationOwnerObservation("other", "process", 9, time.GetUtcNow().AddSeconds(10), time.GetUtcNow());
        provider.Acquire = (_, _) => Task.FromResult(new CoordinationAcquireResult(CoordinationAcquireOutcome.Busy, observedOwner: observed));
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        await runtime.StartAsync();
        ManagedCoordinationResult busy = await runtime.AcquireAuthorizedAsync(Acquisition());
        Assert.AreSame(observed, busy.ObservedOwner);
        Assert.IsNull(busy.Lease);
        await UntilAsync(() => runtime.GetSnapshot().AcquisitionRequests == 0);
        provider.Acquire = null;
        provider.Renew = (_, _, _) => Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.NotCurrent,
            rejectionReason: CoordinationRejectionReason.Superseded));
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.AreEqual(CoordinationLossReason.Superseded, (await lease.Termination.WaitAsync(TestTimeout)).LossReason);
        await lease.DisposeAsync();
    }

    [TestMethod]
    public async Task UncertainReleaseIsTerminalOnceAndStopWaitsForRealRequestSettlement()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        var release = Source<CoordinationMutationResult>();
        provider.Release = (_, _) => release.Task;
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => release.TrySetResult(new(CoordinationMutationOutcome.Indeterminate)));
        await runtime.StartAsync();
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        int lost = 0;
        using CancellationTokenRegistration registration = lease.OwnershipLost.Register(() => Interlocked.Increment(ref lost));
        Task<CoordinationMutationResult> releasing = lease.ReleaseAsync();
        await provider.ReleaseStarted.Task.WaitAsync(TestTimeout);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(CoordinationMutationOutcome.Indeterminate, (await releasing.WaitAsync(TestTimeout)).Outcome);
        Assert.AreEqual(CoordinationLeaseState.Released, lease.State);
        Assert.AreSame(releasing, lease.ReleaseAsync());
        Assert.AreEqual(CoordinationLeaseState.Released, (await lease.Termination).State);
        Task stopping = runtime.StopAsync();
        Assert.IsFalse(stopping.IsCompleted);
        release.SetResult(new(CoordinationMutationOutcome.Succeeded));
        await stopping.WaitAsync(TestTimeout);
        Assert.AreEqual(1, lost);
        Assert.AreEqual(1, provider.Releases);
    }

    [TestMethod]
    public async Task AcquisitionPressureCannotConsumeIndependentRenewalCapacity()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        await runtime.StartAsync();
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        await UntilAsync(() => runtime.GetSnapshot().AcquisitionRequests == 0);
        var pending = Source<CoordinationAcquireResult>();
        provider.Acquire = (_, _) => pending.Task;
        using var caller = new CancellationTokenSource();
        using var unblock = new CleanupAction(() => pending.TrySetResult(new(CoordinationAcquireOutcome.Unavailable)));
        Task<ManagedCoordinationResult> pressured = runtime.AcquireAuthorizedAsync(Acquisition("other"), caller.Token);
        await UntilAsync(() => provider.Acquires == 2);
        time.Advance(TimeSpan.FromSeconds(2));
        await provider.RenewStarted.Task.WaitAsync(TestTimeout);
        Assert.AreEqual(1, provider.Renewals);
        Assert.AreEqual(CoordinationLeaseState.Owned, lease.State);
        caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pressured);
        pending.SetResult(new(CoordinationAcquireOutcome.Unavailable));
        await UntilAsync(() => runtime.GetSnapshot().AcquisitionRequests == 0);
        await lease.DisposeAsync();
    }

    [TestMethod]
    public async Task StuckCancellationCallbackRetainsCapacityWithoutStarvingAnotherLeaseRenewal()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        await runtime.StartAsync();
        ICoordinationLease first = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        await UntilAsync(() => runtime.GetSnapshot().AcquisitionRequests == 0);
        ICoordinationLease second = (await runtime.AcquireAuthorizedAsync(Acquisition("other"))).Lease!;
        var callbackStarted = Source();
        var finishCallback = Source();
        using CancellationTokenRegistration registration = first.OwnershipLost.Register(() =>
        {
            callbackStarted.TrySetResult();
            // Deliberately uncooperative consumer registration; test owns its actual unblock/join.
            finishCallback.Task.GetAwaiter().GetResult();
        });
        using var unblock = new CleanupAction(() => finishCallback.TrySetResult());
        first.BeginDraining(TimeSpan.FromMilliseconds(500));
        time.Advance(TimeSpan.FromMilliseconds(500));
        await callbackStarted.Task.WaitAsync(TestTimeout);
        Assert.AreEqual(CoordinationLeaseState.Lost, first.State);
        Assert.AreEqual(2, runtime.GetSnapshot().ManagedReservations);
        time.Advance(TimeSpan.FromMilliseconds(1500));
        await provider.RenewStarted.Task.WaitAsync(TestTimeout);
        Assert.AreEqual(second.Grant.Acquisition, provider.Renewed.Single().Acquisition);
        finishCallback.TrySetResult();
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    [TestMethod]
    public async Task ShutdownClosesAdmissionRenewsDuringBoundedDrainThenLosesAndJoins()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        await runtime.StartAsync();
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        runtime.BeginStopping();
        Task stopping = runtime.StopAsync();
        Assert.AreEqual(CoordinationLeaseState.Draining, lease.State);
        Assert.AreEqual(CoordinationAcquireOutcome.Unavailable, (await runtime.AcquireAuthorizedAsync(Acquisition("other"))).Outcome);
        time.Advance(TimeSpan.FromSeconds(2));
        await provider.RenewStarted.Task.WaitAsync(TestTimeout);
        await UntilAsync(() => runtime.GetSnapshot().RenewalRequests == 0);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(CoordinationLossReason.ShutdownDeadlineExceeded, (await lease.Termination.WaitAsync(TestTimeout)).LossReason);
        await stopping.WaitAsync(TestTimeout);
        Assert.AreEqual(CoordinationRuntimeState.Stopped, runtime.GetSnapshot().State);
        Assert.AreEqual(0, provider.Releases);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runtime.StartAsync());
    }

    [TestMethod]
    public async Task OperationObserverIsCountedUntilSettlementAndDelayedTransitionsKeepTheirOriginalState()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        var observer = new ControlledObserver();
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => { observer.FinishOperations.TrySetResult(); observer.FinishTransitions.TrySetResult(); });
        runtime.SetObserver(observer);
        await runtime.StartAsync();
        ICoordinationLease lease = (await runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        await observer.AcquireObservationStarted.Task.WaitAsync(TestTimeout);
        lease.BeginDraining(TimeSpan.FromMilliseconds(500));
        time.Advance(TimeSpan.FromMilliseconds(500));
        await lease.Termination.WaitAsync(TestTimeout);
        Task stopping = runtime.StopAsync();
        Assert.IsFalse(stopping.IsCompleted);
        Assert.IsTrue(runtime.GetSnapshot().UnfinishedNotifications > 0);
        observer.FinishOperations.TrySetResult();
        observer.FinishTransitions.TrySetResult();
        await stopping.WaitAsync(TestTimeout);
        CollectionAssert.AreEquivalent(new[] { CoordinationLeaseState.Owned, CoordinationLeaseState.Draining, CoordinationLeaseState.Lost },
            observer.Transitions.ToArray());
    }

    [TestMethod]
    public async Task PhysicalDurationIsRecordedOnlyAtActualSettlementAfterLocalTimeout()
    {
        var time = Clock();
        var provider = new ControlledProvider(time);
        var pending = Source<CoordinationAcquireResult>();
        provider.Acquire = (_, _) => pending.Task;
        var observer = new RecordingObserver();
        await using var runtime = new CoordinationRuntime(provider, Options(), time);
        using var drain = new DrainOnExit(runtime, time);
        using var unblock = new CleanupAction(() => pending.TrySetResult(new(CoordinationAcquireOutcome.Unavailable)));
        runtime.SetObserver(observer);
        await runtime.StartAsync();
        using var caller = new CancellationTokenSource();
        Task<ManagedCoordinationResult> acquiring = runtime.AcquireAuthorizedAsync(Acquisition(), caller.Token);
        await provider.AcquireStarted.Task.WaitAsync(TestTimeout);
        time.Advance(TimeSpan.FromSeconds(2));
        caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await acquiring);
        Assert.IsFalse(observer.AcquireObserved.Task.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        pending.SetResult(new(CoordinationAcquireOutcome.Unavailable));
        await observer.AcquireObserved.Task.WaitAsync(TestTimeout);
        await runtime.StopAsync();
        Assert.AreEqual(TimeSpan.FromSeconds(3), observer.AcquireDuration);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));

    private static CoordinationOptions Options() => new()
    {
        LeaseDuration = TimeSpan.FromSeconds(10),
        RenewalInterval = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(1),
        SafetyMargin = TimeSpan.FromSeconds(2),
        MaximumGracefulDrain = TimeSpan.FromSeconds(3),
        AcquisitionRecoveryTimeout = TimeSpan.FromSeconds(3),
        RecoveryObservationDelay = TimeSpan.FromMilliseconds(100),
        RenewalRetryDelay = TimeSpan.FromMilliseconds(200),
        MaximumManagedLeases = 2,
        MaximumConcurrentAcquisitions = 1,
        MaximumConcurrentRenewals = 1
    };

    private static CoordinationAcquisition Acquisition(string resource = "default") =>
        new(new("test", "wallet", "compaction", resource), "wallet-service", "process", Guid.NewGuid());

    private static TaskCompletionSource<T> Source<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource Source() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task DrainContinuations()
    {
        for (int index = 0; index < 20; index++)
        {
            await Task.Yield();
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class CleanupAction(Action cleanup) : IDisposable
    {
        public void Dispose() => cleanup();
    }

    private sealed class DrainOnExit(CoordinationRuntime runtime, ManualTimeProvider clock) : IDisposable
    {
        public void Dispose()
        {
            if (clock is PausableSchedulerClock paused)
            {
                paused.PauseScheduler = false;
                paused.PauseAllTimers = false;
            }

            runtime.BeginStopping();
            clock.Advance(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class ControlledProvider(TimeProvider time) : ICoordinationProvider
    {
        internal int Validations;
        internal int Acquires;
        internal int Resolutions;
        internal int Renewals;
        internal int Releases;
        internal Exception? ValidationFailure;
        internal Func<CancellationToken, Task>? Validation;
        internal Func<CoordinationAcquireRequest, CancellationToken, Task<CoordinationAcquireResult>>? Acquire;
        internal Func<CoordinationAcquisition, CancellationToken, Task<CoordinationObservation>>? Resolve;
        internal Func<CoordinationGrant, TimeSpan, CancellationToken, Task<CoordinationMutationResult>>? Renew;
        internal Func<CoordinationGrant, CancellationToken, Task<CoordinationMutationResult>>? Release;
        internal readonly TaskCompletionSource AcquireStarted = Source();
        internal readonly TaskCompletionSource ResolveStarted = Source();
        internal readonly TaskCompletionSource RenewStarted = Source();
        internal readonly TaskCompletionSource ReleaseStarted = Source();
        internal readonly ConcurrentQueue<CoordinationGrant> Released = new();
        internal readonly ConcurrentQueue<CoordinationGrant> Renewed = new();

        public Guid AuthorityId { get; } = Guid.NewGuid();

        public Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Validations);
            return ValidationFailure is null ? Validation?.Invoke(cancellationToken) ?? Task.CompletedTask : Task.FromException(ValidationFailure);
        }

        public Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Acquires);
            AcquireStarted.TrySetResult();
            return Acquire?.Invoke(request, cancellationToken) ?? Task.FromResult(new CoordinationAcquireResult(
                CoordinationAcquireOutcome.Acquired, Grant(request.Acquisition), TimeSpan.FromSeconds(10)));
        }

        public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Renewals);
            Renewed.Enqueue(grant);
            RenewStarted.TrySetResult();
            return Renew?.Invoke(grant, leaseDuration, cancellationToken) ?? Task.FromResult(new CoordinationMutationResult(
                CoordinationMutationOutcome.Succeeded, Refreshed(grant), TimeSpan.FromSeconds(10)));
        }

        public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Releases);
            Released.Enqueue(grant);
            ReleaseStarted.TrySetResult();
            return Release?.Invoke(grant, cancellationToken) ?? Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded));
        }

        public Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Resolutions);
            ResolveStarted.TrySetResult();
            return Resolve?.Invoke(acquisition, cancellationToken)
                ?? Task.FromResult(new CoordinationObservation(CoordinationObservationOutcome.NotObserved));
        }

        internal CoordinationGrant Grant(CoordinationAcquisition acquisition) => new(AuthorityId, acquisition, 1, time.GetUtcNow().AddSeconds(10));

        internal CoordinationGrant Refreshed(CoordinationGrant grant) => new(AuthorityId, grant.Acquisition, grant.Generation, time.GetUtcNow().AddSeconds(10));
    }

    private sealed class PausableSchedulerClock() : ManualTimeProvider(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero))
    {
        internal bool PauseScheduler;
        internal bool PauseAllTimers;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            return base.CreateTimer(value =>
            {
                if (!PauseAllTimers && (!PauseScheduler || period <= TimeSpan.Zero))
                {
                    callback(value);
                }
            }, state, dueTime, period);
        }
    }

    private sealed class RecordingObserver : ICoordinationRuntimeObserver
    {
        internal readonly TaskCompletionSource AcquireObserved = Source();
        internal TimeSpan AcquireDuration;

        public void OperationCompleted(CoordinationRuntimeOperation operation, CoordinationRuntimeOperationOutcome outcome, TimeSpan duration)
        {
            if (operation == CoordinationRuntimeOperation.Acquire)
            {
                AcquireDuration = duration;
                AcquireObserved.TrySetResult();
            }
        }

        public void Transition(CoordinationLeaseSnapshot snapshot)
        {
        }
    }

    private sealed class ControlledObserver : ICoordinationRuntimeObserver
    {
        internal readonly TaskCompletionSource AcquireObservationStarted = Source();
        internal readonly TaskCompletionSource FinishOperations = Source();
        internal readonly TaskCompletionSource FinishTransitions = Source();
        internal readonly ConcurrentQueue<CoordinationLeaseState> Transitions = new();

        public void OperationCompleted(CoordinationRuntimeOperation operation, CoordinationRuntimeOperationOutcome outcome, TimeSpan duration)
        {
            if (operation == CoordinationRuntimeOperation.Acquire)
            {
                AcquireObservationStarted.TrySetResult();
                FinishOperations.Task.GetAwaiter().GetResult();
            }
        }

        public void Transition(CoordinationLeaseSnapshot snapshot)
        {
            FinishTransitions.Task.GetAwaiter().GetResult();
            Transitions.Enqueue(snapshot.State);
        }
    }
}
