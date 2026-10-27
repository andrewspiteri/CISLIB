using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRunnerCompletionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnEmptyRunnerStopsAndReleasesItsTimerWhenStarted(bool start)
    {
        var clock = new ControlledClock();
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        Exception? primary = null;
        try
        {
            if (start)
            {
                await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
                await clock.Timer.Changed.Task.WaitAsync(Deadline);
            }
            await Task.Run(() => runner.StopAsync()).WaitAsync(Deadline);
            await Task.Run(() => runner.StopAsync()).WaitAsync(Deadline);
            Assert.AreEqual(start ? 1 : 0, clock.Creations);
            Assert.AreEqual(start ? 1 : 0, clock.Timer.Disposals);
            Assert.IsFalse(service.CaptureSnapshot().Application.IsStopping);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.Timer.Release();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopPublishesTheActualTimerDisposalOutcome(bool failDisposal)
    {
        var clock = new ControlledClock(holdDisposal: true, failDisposal: failDisposal);
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        InvalidOperationException? expectedFailure = null;
        Exception? primary = null;
        Task? stop = null;
        try
        {
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            await clock.Timer.Changed.Task.WaitAsync(Deadline);
            stop = Task.Run(() => runner.StopAsync());
            await clock.Timer.DisposalEntered.Task.WaitAsync(Deadline);
            Assert.IsFalse(stop.IsCompleted, "Stop includes the timer disposal that is still held by its owner.");
            clock.Timer.Release();
            if (failDisposal)
            {
                expectedFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stop).WaitAsync(Deadline);
            }
            else { await stop.WaitAsync(Deadline); }
            Assert.AreEqual(1, clock.Timer.Disposals);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.Timer.Release();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                DrainRunnerAsync, () => service.DisposeAsync().AsTask());
        }

        async Task DrainRunnerAsync()
        {
            try { await runner.DisposeAsync(); }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expectedFailure)) { }
        }
    }

    [TestMethod]
    public async Task ManualCollectionPublishesOneCompletedLeaseAndItsPolicyOutcome()
    {
        var clock = new ControlledClock();
        var service = new HealthcheckService(new(), timeProvider: clock);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var runner = new HealthCollectorRunner(service);
        var owner = new ControlledOwner();
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        Exception? primary = null;
        try
        {
            registration = runner.Register(monitor, owner, new());
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            caller = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync());
            await Task.WhenAny(owner.Entered.Task, caller).WaitAsync(Deadline);
            Assert.IsFalse(caller.IsCompleted, "The collection cannot settle before its held evaluation enters.");
            await owner.Entered.Task.WaitAsync(Deadline);
            owner.Release();
            HealthCollectionAttempt attempt = await caller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.IsNull(attempt.Diagnostic);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.AreEqual(1L, service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.Release();
            clock.Timer.Release();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, caller is null ? [] : [caller],
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetirementCompletesTheRegistrationAfterItsRunningLeaseSettles(bool stopRunner)
    {
        var clock = new ControlledClock();
        var service = new HealthcheckService(new(), timeProvider: clock);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var runner = new HealthCollectorRunner(service);
        var owner = new ControlledOwner();
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
            Assert.IsFalse(caller.IsCompleted, "The collection cannot settle before its held evaluation enters.");
            await owner.Entered.Task.WaitAsync(Deadline);
            retirement = stopRunner ? Task.Run(() => runner.StopAsync())
                : Task.Run(() => registration.DisposeAsync().AsTask());
            await owner.CancellationObserved.Task.WaitAsync(Deadline);
            Assert.IsFalse(retirement.IsCompleted, "Retirement retains the evaluation's actual owner lease.");
            Assert.IsFalse(registration.WaitForDisposalAsync().IsCompleted);
            owner.Release();
            await Task.WhenAll(caller, retirement).WaitAsync(Deadline);
            await registration.WaitForDisposalAsync().WaitAsync(Deadline);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Cleanups);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.Release();
            clock.Timer.Release();
            Task[] observed = [.. caller is null ? Array.Empty<Task>() : [caller], .. retirement is null ? Array.Empty<Task>() : [retirement]];
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, observed,
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    private sealed class ControlledClock(bool holdDisposal = false, bool failDisposal = false) : TimeProvider
    {
        private int creations;
        internal ControlledTimer Timer { get; } = new(holdDisposal, failDisposal);
        internal int Creations => Volatile.Read(ref creations);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { Interlocked.Increment(ref creations); return Timer; }
    }

    private sealed class ControlledTimer(bool holdDisposal, bool failDisposal) : ITimer
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposals;
        internal int Disposals => Volatile.Read(ref disposals);
        internal TaskCompletionSource Changed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => released.TrySetResult();
        public bool Change(TimeSpan dueTime, TimeSpan period) { Changed.TrySetResult(); return true; }
        public void Dispose() => throw new InvalidOperationException("This timer requires its asynchronous disposal path.");
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            DisposalEntered.TrySetResult();
            if (holdDisposal) { await released.Task; }
            if (failDisposal) { throw new TimerDisposalException(); }
        }
    }

    private sealed class ControlledOwner : IHealthCollector
    {
        private readonly TaskCompletionSource<HealthCollectionResult> released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int acquisitions, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => released.TrySetResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        public bool TryAcquire(out IHealthCollectionLease? lease)
        { Interlocked.Increment(ref acquisitions); lease = new Lease(this); return true; }
        private sealed class Lease(ControlledOwner owner) : IHealthCollectionLease
        {
            private CancellationTokenRegistration cancellation;
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                cancellation = lifetimeCancellation.Register(() => owner.CancellationObserved.TrySetResult());
                owner.Entered.TrySetResult();
                return owner.released.Task;
            }
            public ValueTask DisposeAsync()
            { cancellation.Dispose(); Interlocked.Increment(ref owner.cleanups); return ValueTask.CompletedTask; }
        }
    }

    private sealed class TimerDisposalException : Exception;
}

internal static class HealthCollectorTestCleanup
{
    internal static Task<T> EnterAsync<T>(TimeSpan deadline, Func<T> operation) => Task.Run(operation).WaitAsync(deadline);

    internal static async Task DrainAsync(TimeSpan deadline, Exception? primary, IEnumerable<Task> observedCallers, params Func<Task>[] operations)
    {
        Task[] actual = operations.Select(operation => Task.Run(operation)).Concat(observedCallers).Distinct().ToArray();
        Task all = Task.WhenAll(actual);
        Exception? waitFailure = null;
        try { await all.WaitAsync(deadline); }
        catch (Exception error) { waitFailure = error; }
        var errors = new List<Exception>();
        foreach (Task task in actual)
        {
            if (task.Exception is { } fault)
            {
                foreach (Exception error in fault.Flatten().InnerExceptions)
                { if (!errors.Any(known => ReferenceEquals(known, error))) { errors.Add(error); } }
            }
            else if (task.IsCanceled) { errors.Add(new TaskCanceledException(task)); }
        }
        if (waitFailure is TimeoutException || waitFailure is not null && errors.Count == 0) { errors.Add(waitFailure); }
        if (errors.Count == 0) { return; }
        if (primary is not null && !errors.Any(error => ReferenceEquals(error, primary))) { errors.Insert(0, primary); }
        throw new AggregateException("The collector test or actual cleanup failed; incomplete cleanup was not credited as settlement.", errors);
    }
}
