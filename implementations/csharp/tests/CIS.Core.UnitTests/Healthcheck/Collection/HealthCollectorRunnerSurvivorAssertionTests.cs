using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRunnerSurvivorAssertionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public async Task RunnerDisposalDoesNotRequireDispatchOnTheCallingSynchronizationContext()
    {
        var context = new HeldSynchronizationContext();
        var clock = new ControlledClock(holdTimerDisposal: true);
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        Task? disposal = null;
        Exception? primary = null;
        try
        {
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            await clock.Timer.Changed.Task.WaitAsync(Deadline);
            disposal = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => InContext(context, () => runner.DisposeAsync().AsTask()));
            await clock.Timer.DisposalEntered.Task.WaitAsync(Deadline);
            Assert.IsFalse(disposal.IsCompleted, "The actual timer owner has not yet released disposal.");

            clock.Timer.Release();
            await AssertIndependentCompletionAsync(disposal, context);
            Assert.AreEqual(1, clock.Timer.Disposals);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.Timer.Release();
            context.Release();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, disposal is null ? [] : [disposal],
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
        }
    }

    [TestMethod]
    public async Task RegistrationRetirementDoesNotRequireDispatchOnTheCallingSynchronizationContext()
    {
        var context = new HeldSynchronizationContext();
        var clock = new ControlledClock();
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new HeldOwner();
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        Task? retirement = null;
        Exception? primary = null;
        try
        {
            registration = runner.Register(monitor, owner, new());
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            caller = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync());
            await Task.WhenAny(owner.Entered.Task, caller).WaitAsync(Deadline);
            Assert.IsFalse(caller.IsCompleted, "The actual evaluation is held by its owner.");
            await owner.Entered.Task.WaitAsync(Deadline);
            retirement = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => InContext(context, () => registration.DisposeAsync().AsTask()));
            await owner.CancellationObserved.Task.WaitAsync(Deadline);
            Assert.IsFalse(retirement.IsCompleted, "Retirement includes the actual held owner lease.");

            owner.Release();
            await AssertIndependentCompletionAsync(retirement, context);
            Assert.AreEqual(HealthCollectionDisposition.Retired, (await caller.WaitAsync(Deadline)).Disposition);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.AreEqual(0L, service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.Release();
            context.Release();
            Task[] observed = [.. caller is null ? Array.Empty<Task>() : [caller], .. retirement is null ? Array.Empty<Task>() : [retirement]];
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, observed,
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AComponentSynchronizationContextCannotCaptureEvaluationOrLeaseCleanup(bool duringCleanup)
    {
        var context = new HeldSynchronizationContext();
        var service = new HealthcheckService(new(), timeProvider: new ControlledClock());
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new ContextOwner(context, duringCleanup);
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        Exception? primary = null;
        try
        {
            registration = runner.Register(monitor, owner, new());
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            caller = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync());
            await Task.WhenAny(owner.Entered.Task, caller).WaitAsync(Deadline);
            Assert.IsFalse(caller.IsCompleted, "The owner still holds the actual evaluation task.");
            await owner.Entered.Task.WaitAsync(Deadline);
            owner.ReleaseEvaluation();
            if (duringCleanup)
            {
                await owner.CleanupEntered.Task.WaitAsync(Deadline);
                Assert.IsFalse(caller.IsCompleted, "The owner still holds the actual lease cleanup task.");
                owner.ReleaseCleanup();
            }

            await AssertIndependentCompletionAsync(caller, context);
            HealthCollectionAttempt attempt = await caller;
            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.IsNull(attempt.Diagnostic);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.AreEqual(1L, service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.ReleaseEvaluation();
            owner.ReleaseCleanup();
            context.Release();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, caller is null ? [] : [caller],
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task AnUnavailableOwnerRetiresTheRegistrationBeforeAnotherManualRequestCanAcquireIt()
    {
        var service = new HealthcheckService(new(), timeProvider: new ControlledClock());
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new HeldOwner(unavailable: true);
        IHealthCollectionRegistration? registration = null;
        var callers = new List<Task>();
        Exception? primary = null;
        try
        {
            registration = runner.Register(monitor, owner, new());
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            Task<HealthCollectionAttempt> first = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync());
            callers.Add(first);
            HealthCollectionAttempt unavailable = await first.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.OwnerUnavailable, unavailable.Disposition);
            Assert.IsFalse(unavailable.EvaluationFailed);
            Assert.IsNull(unavailable.Diagnostic);

            Task<HealthCollectionAttempt> second = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync());
            callers.Add(second);
            HealthCollectionAttempt retired = await second.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, retired.Disposition,
                "An unavailable owner is terminal for its registration and must not be acquired again.");
            Assert.IsFalse(retired.EvaluationFailed);
            Assert.IsNull(retired.Diagnostic);
            await registration.WaitForDisposalAsync().WaitAsync(Deadline);
            await monitor.WaitForDisposalAsync().WaitAsync(Deadline);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(0, owner.Cleanups);
            Assert.ThrowsExactly<ObjectDisposedException>(monitor.ReportSuccess);
            HealthSnapshot snapshot = service.CaptureSnapshot();
            Assert.AreEqual(0, snapshot.MonitorCounts.Active);
            Assert.AreEqual(0, snapshot.Monitors.Count);
            Assert.IsFalse(snapshot.Application.IsStopping);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, callers,
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TheFirstActualResourceFailureSurvivesASecondFailureDuringShutdown(bool disposalFailsFirst)
    {
        var changeFailure = new InvalidOperationException("The supplied clock could not change its timer.");
        var disposalFailure = new InvalidOperationException("The supplied clock could not dispose its timer.");
        var clock = new FaultOrderingClock(changeFailure, disposalFailure, disposalFailsFirst);
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        Task? stop = null;
        InvalidOperationException? terminal = null;
        Exception? primary = null;
        try
        {
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            await clock.Timer.ChangeEntered.Task.WaitAsync(Deadline);
            if (disposalFailsFirst)
            {
                stop = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());
                await clock.Timer.DisposalEntered.Task.WaitAsync(Deadline);
                Assert.IsFalse(stop.IsCompleted, "The actual timer Change still owns dispatcher work.");
                clock.Timer.ReleaseChange();
            }
            else
            {
                // The failing Change initiates stop itself before the public caller joins.
                await clock.Timer.DisposalEntered.Task.WaitAsync(Deadline);
                stop = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());
            }

            terminal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stop).WaitAsync(Deadline);
            Exception first = disposalFailsFirst ? disposalFailure : changeFailure;
            Assert.AreEqual(first.Message, terminal.Message,
                "The first actual resource failure must remain the terminal diagnostic when shutdown also fails.");
            Assert.AreEqual(1, clock.Timer.ChangeFailures);
            Assert.AreEqual(1, clock.Timer.DisposalFailures);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.Timer.ReleaseChange();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary,
                stop is null ? [] : [ObserveTerminalAsync(stop)],
                () => ObserveTerminalAsync(runner.DisposeAsync().AsTask()), () => service.DisposeAsync().AsTask());
        }

        async Task ObserveTerminalAsync(Task task)
        {
            try { await task; }
            catch (InvalidOperationException error) when (ReferenceEquals(error, terminal)) { }
        }
    }

    private static Task InContext(SynchronizationContext context, Func<Task> operation)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(context); return operation(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task AssertIndependentCompletionAsync(Task actual, HeldSynchronizationContext context)
    {
        await Task.WhenAny(actual, context.Posted.Task).WaitAsync(Deadline);
        Assert.IsFalse(context.Posted.Task.IsCompleted,
            "An owner drain must complete without posting its continuation to the waiting caller's synchronization context.");
        await actual.WaitAsync(Deadline);
    }

    private sealed class HeldSynchronizationContext : SynchronizationContext
    {
        private readonly object gate = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();
        private bool released;
        internal TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (gate)
            {
                Posted.TrySetResult();
                if (!released) { pending.Enqueue((callback, state)); return; }
            }
            Queue((callback, state));
        }
        internal void Release()
        {
            lock (gate)
            {
                released = true;
                while (pending.TryDequeue(out var item)) { Queue(item); }
            }
        }
        private static void Queue((SendOrPostCallback Callback, object? State) item)
            => ThreadPool.UnsafeQueueUserWorkItem(static work => work.Callback(work.State), item, preferLocal: false);
    }

    private sealed class ControlledClock(bool holdTimerDisposal = false) : TimeProvider
    {
        internal HeldTimer Timer { get; } = new(holdTimerDisposal);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Timer;
    }

    private sealed class HeldTimer(bool holdDisposal) : ITimer
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposals;
        internal TaskCompletionSource Changed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals => Volatile.Read(ref disposals);
        internal void Release() => released.TrySetResult();
        public bool Change(TimeSpan dueTime, TimeSpan period) { Changed.TrySetResult(); return true; }
        public void Dispose() => throw new InvalidOperationException("The supplied timer requires asynchronous disposal.");
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            DisposalEntered.TrySetResult();
            return holdDisposal ? new(released.Task) : ValueTask.CompletedTask;
        }
    }

    private sealed class HeldOwner(bool unavailable = false) : IHealthCollector
    {
        private readonly TaskCompletionSource<HealthCollectionResult> released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int acquisitions, cleanups;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal void Release() => released.TrySetResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = unavailable ? null : new Lease(this);
            return !unavailable;
        }
        private sealed class Lease(HeldOwner owner) : IHealthCollectionLease
        {
            private CancellationTokenRegistration cancellation;
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                cancellation = lifetimeCancellation.Register(() => owner.CancellationObserved.TrySetResult());
                owner.Entered.TrySetResult();
                return owner.released.Task;
            }
            public ValueTask DisposeAsync()
            {
                cancellation.Dispose();
                Interlocked.Increment(ref owner.cleanups);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FaultOrderingClock(Exception changeFailure, Exception disposalFailure, bool holdChange) : TimeProvider
    {
        internal FaultOrderingTimer Timer { get; } = new(changeFailure, disposalFailure, holdChange);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Timer;
    }

    private sealed class ContextOwner(HeldSynchronizationContext context, bool duringCleanup) : IHealthCollector
    {
        private HeldSynchronizationContext Context { get; } = context;
        private bool DuringCleanup { get; } = duringCleanup;
        private readonly TaskCompletionSource<HealthCollectionResult> evaluation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int cleanups;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal void ReleaseEvaluation() => evaluation.TrySetResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        internal void ReleaseCleanup() => cleanup.TrySetResult();
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(this); return true; }
        private sealed class Lease(ContextOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                // A component may install its own context while producing work.
                // Core must not require that component to dispatch its continuation.
                if (!owner.DuringCleanup) { SynchronizationContext.SetSynchronizationContext(owner.Context); }
                owner.Entered.TrySetResult();
                return owner.evaluation.Task;
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.cleanups);
                if (owner.DuringCleanup) { SynchronizationContext.SetSynchronizationContext(owner.Context); }
                owner.CleanupEntered.TrySetResult();
                return owner.DuringCleanup ? new(owner.cleanup.Task) : ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FaultOrderingTimer(Exception changeFailure, Exception disposalFailure, bool holdChange) : ITimer
    {
        private readonly ManualResetEventSlim releaseChange = new(!holdChange);
        private int changeFailures, disposalFailures;
        internal TaskCompletionSource ChangeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ChangeFailures => Volatile.Read(ref changeFailures);
        internal int DisposalFailures => Volatile.Read(ref disposalFailures);
        internal void ReleaseChange() => releaseChange.Set();
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ChangeEntered.TrySetResult();
            if (!releaseChange.Wait(Deadline)) { throw new TimeoutException("The actual timer Change was not released by its owner."); }
            Interlocked.Increment(ref changeFailures);
            throw changeFailure;
        }
        public void Dispose() => throw new InvalidOperationException("The supplied timer requires asynchronous disposal.");
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposalFailures);
            DisposalEntered.TrySetResult();
            return ValueTask.FromException(disposalFailure);
        }
    }
}
