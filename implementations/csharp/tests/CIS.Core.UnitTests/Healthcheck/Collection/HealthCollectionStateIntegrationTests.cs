using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionStateIntegrationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task RetirementCallbacksKeepOperationMonitorAndServiceDrainsPendingAfterConcurrentComplete()
    {
        await using var scope = new Scope();
        HealthCollectionTarget target = scope.Attach();
        HealthCollectionReservation reservation = scope.Reserve(target);
        Assert.IsTrue(scope.State.TryEnter(reservation));
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        bool callbackReleased = false;
        using CancellationTokenRegistration callback = target.Retirement.Register(() =>
        {
            callbackEntered.TrySetResult();
            callbackReleased = releaseCallback.Wait(Deadline);
        });
        Task withdrawal = Task.Run(scope.Monitor.Withdraw);
        Task settlement = scope.State.WaitForSettlementAsync(reservation);
        Task? serviceDrain = null;
        try
        {
            await callbackEntered.Task.WaitAsync(Deadline);
            await Task.Run(() => scope.Complete(reservation)).WaitAsync(Deadline);

            Assert.AreSame(settlement, scope.State.WaitForSettlementAsync(reservation));
            Assert.IsFalse(settlement.IsCompleted, "Complete must retain the actual slot while retirement callbacks run.");
            Assert.IsFalse(scope.Monitor.WaitForDisposalAsync().IsCompleted);
            serviceDrain = scope.Service.DisposeAsync().AsTask();
            Assert.IsFalse(serviceDrain.IsCompleted);
            Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        }
        finally
        {
            releaseCallback.Set();
            scope.Complete(reservation);
            await withdrawal.WaitAsync(Deadline);
        }

        Assert.IsTrue(callbackReleased);
        Assert.IsNotNull(serviceDrain);
        await Task.WhenAll(settlement, scope.Monitor.WaitForDisposalAsync(), serviceDrain).WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.State.TryReserve(target).Disposition);
    }

    [TestMethod]
    public async Task IdleAttachmentRetirementCallbacksAlsoHoldMonitorAndServiceDrains()
    {
        await using var scope = new Scope();
        HealthCollectionTarget target = scope.Attach();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        bool callbackReleased = false;
        using CancellationTokenRegistration callback = target.Retirement.Register(() =>
        {
            callbackEntered.TrySetResult();
            callbackReleased = releaseCallback.Wait(Deadline);
        });
        Task withdrawal = Task.Run(scope.Monitor.Withdraw);
        Task? serviceDrain = null;
        try
        {
            await callbackEntered.Task.WaitAsync(Deadline);
            Assert.IsTrue(target.Retirement.IsCancellationRequested);
            Assert.IsFalse(scope.Monitor.WaitForDisposalAsync().IsCompleted);
            Assert.IsFalse(scope.State.WithdrawCollection(target).IsCompleted);
            serviceDrain = scope.Service.DisposeAsync().AsTask();
            Assert.IsFalse(serviceDrain.IsCompleted, "An idle attachment's cancellation callback remains actual unsettled work.");
        }
        finally
        {
            releaseCallback.Set();
            await withdrawal.WaitAsync(Deadline);
        }

        Assert.IsTrue(callbackReleased);
        Assert.IsNotNull(serviceDrain);
        await Task.WhenAll(scope.Monitor.WaitForDisposalAsync(), serviceDrain,
            scope.State.WithdrawCollection(target)).WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task StoppingObserversRunOutsideTheStateGateAndSignalOnlyOnce()
    {
        await using var scope = new Scope();
        int calls = 0;
        bool snapshotCompleted = false;
        bool snapshotStopping = false;
        using CancellationTokenRegistration callback = scope.State.Stopping.Register(() =>
        {
            Interlocked.Increment(ref calls);
            Task read = Task.Run(() => { snapshotStopping = scope.Service.CaptureSnapshot().Application.IsStopping; });
            snapshotCompleted = read.Wait(Deadline);
        });

        scope.Service.BeginStopping();
        scope.Service.BeginStopping();

        Assert.IsTrue(snapshotCompleted, "A stopping observer must be able to capture state on another thread.");
        Assert.IsTrue(snapshotStopping);
        Assert.IsTrue(scope.State.Stopping.IsCancellationRequested);
        Assert.AreEqual(1, calls);
        scope.Monitor.Flag();
        Assert.AreEqual(MonitorState.Unhealthy, scope.Service.CaptureSnapshot().Monitors.Single().State);
    }

    [TestMethod]
    public async Task WithdrawnAttachmentCannotBeReplacedUntilItsActualOperationSettles()
    {
        await using var scope = new Scope();
        HealthCollectionTarget previous = scope.Attach();
        HealthCollectionReservation oldOperation = scope.Reserve(previous);
        Assert.IsTrue(scope.State.TryEnter(oldOperation));

        Task drain = scope.State.WithdrawCollection(previous);

        Assert.IsTrue(previous.Retirement.IsCancellationRequested);
        Assert.IsFalse(drain.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => scope.Attach());
        scope.Monitor.Flag();
        Assert.AreEqual(MonitorState.Unhealthy, scope.Service.CaptureSnapshot().Monitors.Single().State);
        scope.Complete(oldOperation);
        await Task.WhenAll(drain, scope.State.WaitForSettlementAsync(oldOperation)).WaitAsync(Deadline);

        HealthCollectionTarget replacement = scope.Attach();
        Assert.IsTrue(replacement.Generation > previous.Generation);
        Assert.AreNotEqual(previous.CollectionRegistrationId, replacement.CollectionRegistrationId);
        Assert.IsFalse(replacement.Retirement.IsCancellationRequested);
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.State.TryReserve(previous).Disposition);
        HealthCollectionReservation newOperation = scope.Reserve(replacement);
        Assert.IsTrue(scope.State.TryEnter(newOperation));
        Assert.AreEqual(HealthCollectionDisposition.Committed,
            scope.State.CommitResult(newOperation, HealthCollectionResult.Condition(false)).Disposition);
        scope.Complete(newOperation);
        await scope.State.WaitForSettlementAsync(newOperation).WaitAsync(Deadline);
        Assert.AreEqual(MonitorState.Healthy, scope.Service.CaptureSnapshot().Monitors.Single().State);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<HealthCollectionReservation> reservations = [];
        private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

        internal Scope()
        {
            Service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity
            {
                Name = "Collection state integration tests",
                InstanceId = Guid.NewGuid()
            }, clock);
            State = ((IHealthStateAccess)Service).CollectionState;
            Monitor = Service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        }

        internal HealthcheckService Service { get; }
        internal IHealthCollectionState State { get; }
        internal IConditionMonitor Monitor { get; }

        internal HealthCollectionTarget Attach() => State.RegisterCollection(Monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());

        internal HealthCollectionReservation Reserve(HealthCollectionTarget target)
        {
            HealthCollectionReservationResult decision = State.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, decision.Disposition);
            Assert.IsNotNull(decision.Reservation);
            reservations.Add(decision.Reservation);
            return decision.Reservation;
        }

        internal void Complete(HealthCollectionReservation reservation) =>
            State.Complete(reservation, new HealthCollectionCompletion(HealthCollectionCompletionReason.Settled, clock.GetUtcNow()));

        public async ValueTask DisposeAsync()
        {
            foreach (HealthCollectionReservation reservation in reservations) { Complete(reservation); }
            await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
            GC.KeepAlive(Monitor);
        }
    }
}
