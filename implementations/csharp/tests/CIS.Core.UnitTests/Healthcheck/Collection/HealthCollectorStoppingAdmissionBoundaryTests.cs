using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorStoppingAdmissionBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TheStoppingSignalClosesCollectorAdmissionBeforeItsObserversRun(bool periodic)
    {
        var service = new HealthcheckService(new(), Identity());
        var runner = new HealthCollectorRunner(service);
        IConditionMonitor monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
        var owner = new Owner();
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? returnedCaller = null;
        Exception? admissionError = null;
        bool admitted = false;
        int calls = 0;
        CancellationTokenRegistration observer = default;
        try
        {
            registration = runner.Register(monitor, owner, new() { Interval = TimeSpan.FromSeconds(1) });
            await runner.StartAsync().WaitAsync(Deadline);
            IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
            // Registered after the runner's observer: this real cancellation callback
            // receives the terminal state before the runner's own observer closes its entries.
            observer = state.Stopping.Register(() =>
            {
                calls++;
                try
                {
                    if (periodic) { registration.ActivatePeriodic(); }
                    else { returnedCaller = registration.CollectAsync(); }
                    admitted = true;
                }
                catch (Exception exception) { admissionError = exception; }
            });

            service.BeginStopping();

            Assert.AreEqual(1, calls);
            Assert.IsTrue(state.Stopping.IsCancellationRequested);
            Assert.IsTrue(service.CaptureSnapshot().Application.IsStopping);
            Assert.IsFalse(admitted, "A stopping observer must not admit a new manual or periodic collection.");
            Assert.IsNotNull(admissionError);
            Assert.AreEqual(typeof(InvalidOperationException), admissionError.GetType());
            Assert.IsNull(returnedCaller);
            Assert.AreEqual(0, owner.Acquisitions);
        }
        finally
        {
            observer.Dispose();
            try
            {
                await runner.StopAsync().WaitAsync(Deadline);
                // A faulty admission may have produced a real caller/lease: join it
                // after terminal runner stop rather than abandoning it on an assertion.
                if (returnedCaller is not null) { await returnedCaller.WaitAsync(Deadline); }
            }
            finally
            {
                try
                {
                    if (registration is not null) { await registration.DisposeAsync().AsTask().WaitAsync(Deadline); }
                    await runner.DisposeAsync().AsTask().WaitAsync(Deadline);
                }
                finally
                {
                    monitor.Withdraw();
                    await service.DisposeAsync().AsTask().WaitAsync(Deadline);
                    GC.KeepAlive(owner);
                }
            }
        }
    }

    [TestMethod]
    public async Task RepeatedStopCannotAcknowledgeATimerStillBeingDisposedAfterConcurrentStart()
    {
        var clock = new Clock();
        var service = new HealthcheckService(new(), Identity(), clock);
        var runner = new HealthCollectorRunner(service);
        Task? firstStop = null;
        clock.DuringCreation = () => firstStop = runner.StopAsync();
        try
        {
            await runner.StartAsync().WaitAsync(Deadline);
            await clock.Timer.DisposalEntered.Task.WaitAsync(Deadline);

            Assert.IsNotNull(firstStop);
            Assert.IsFalse(firstStop.IsCompleted);
            Assert.AreEqual(1, clock.Timer.Disposals);
            Assert.IsFalse(clock.Timer.CleanupFinished.Task.IsCompleted);

            Task repeatedStop = runner.StopAsync();

            Assert.IsFalse(repeatedStop.IsCompleted,
                "Repeated stop must retain the barrier for the actual timer disposal that is still held.");
            Assert.IsFalse(clock.Timer.CleanupFinished.Task.IsCompleted);
        }
        finally
        {
            clock.Timer.Release();
            try
            {
                // This milestone belongs to the owned timer, independently of a
                // possibly premature runner-completion signal.
                if (clock.Timer.Disposals != 0) { await clock.Timer.CleanupFinished.Task.WaitAsync(Deadline); }
                await runner.StopAsync().WaitAsync(Deadline);
                await runner.DisposeAsync().AsTask().WaitAsync(Deadline);
            }
            finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
        }
    }

    private static HealthApplicationIdentity Identity() => new() { Name = "Stopping collector admission", InstanceId = Guid.NewGuid() };

    private sealed class Owner : IHealthCollector
    {
        private int acquisitions;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease();
            return true;
        }
        private sealed class Lease : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
                => Task.FromResult(HealthCollectionResult.Condition(false));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal Action? DuringCreation { get; set; }
        internal ControlledTimer Timer { get; } = new();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 23, 3, 0, 0, TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DuringCreation?.Invoke();
            return Timer;
        }
    }

    private sealed class ControlledTimer : ITimer
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposals;
        internal int Disposals => Volatile.Read(ref disposals);
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() => Release();
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            DisposalEntered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            CleanupFinished.TrySetResult();
        }
        internal void Release() => release.TrySetResult();
    }
}
