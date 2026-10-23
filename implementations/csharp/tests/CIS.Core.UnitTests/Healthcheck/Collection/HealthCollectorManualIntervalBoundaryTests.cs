using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorManualIntervalBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 23, 4, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AnUnactivatedPeriodicIntervalCannotRetireManualCollectionWhenAFutureDueTimeWouldOverflow()
    {
        await using var scope = new Scope();
        IHealthCollectionRegistration registration = scope.Runner.Register(scope.Monitor, scope.Owner,
            new() { Interval = TimeSpan.FromTicks(1) });
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        // An interval configures possible recurrence. This owner never activates
        // it: both attempts below are actual manual requests after runner start.
        Task<HealthCollectionAttempt> firstCaller = registration.CollectAsync();
        await WaitForCallbackAsync(scope.Owner.First.Entered.Task, firstCaller);
        scope.Owner.First.ReleaseEvaluation();
        await scope.Owner.First.CleanupEntered.Task.WaitAsync(Deadline);
        Assert.IsFalse(firstCaller.IsCompleted,
            "A held owner lease remains actual work even after its evaluation returns.");
        scope.Owner.First.ReleaseCleanup();
        AssertCommitted(await firstCaller.WaitAsync(Deadline));
        Assert.AreEqual(long.MaxValue, scope.Clock.GetTimestamp());
        Assert.AreEqual(1, scope.Owner.Cleanups);

        // First completion has joined cleanup and removed its shared operation.
        // Race the second real callback against its caller's terminal result so
        // wrongful retirement fails on Retired rather than a missing-entry timeout.
        Task<HealthCollectionAttempt> secondCaller = registration.CollectAsync();
        await WaitForCallbackAsync(scope.Owner.Second.Entered.Task, secondCaller);
        Assert.AreEqual(2, scope.Owner.Acquisitions);
        scope.Owner.Second.ReleaseEvaluation();
        await scope.Owner.Second.CleanupEntered.Task.WaitAsync(Deadline);
        scope.Owner.Second.ReleaseCleanup();
        AssertCommitted(await secondCaller.WaitAsync(Deadline));

        Assert.AreEqual(2, scope.Owner.Cleanups);
        HealthMonitorSnapshot snapshot = scope.Service.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(2L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        Assert.IsNull(snapshot.Collection!.LastError);
        GC.KeepAlive(registration);
    }

    private static async Task WaitForCallbackAsync(Task entered, Task<HealthCollectionAttempt> caller)
    {
        await Task.WhenAny(entered, caller).WaitAsync(Deadline);
        if (caller.IsCompleted)
        {
            HealthCollectionAttempt returned = await caller;
            Assert.AreEqual(HealthCollectionDisposition.Committed, returned.Disposition,
                "An unactivated interval must not retire the next requested manual collection.");
            Assert.Fail("A collection cannot settle before its held owner callback has been released.");
        }
        await entered.WaitAsync(Deadline);
    }

    private static void AssertCommitted(HealthCollectionAttempt attempt)
    {
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
    }

    private sealed class Scope : IAsyncDisposable
    {
        internal Scope()
        {
            Service = new(new(), new() { Name = "manual interval boundary", InstanceId = Guid.NewGuid() }, Clock);
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            Owner = new(Clock);
            Runner = new(Service);
        }

        internal Clock Clock { get; } = new();
        internal GatedOwner Owner { get; }
        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal HealthCollectorRunner Runner { get; }

        public async ValueTask DisposeAsync()
        {
            Owner.First.ReleaseEvaluation();
            Owner.First.ReleaseCleanup();
            Owner.Second.ReleaseEvaluation();
            Owner.Second.ReleaseCleanup();
            try { await Runner.DisposeAsync().AsTask().WaitAsync(Deadline); }
            finally
            {
                await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
                GC.KeepAlive(Monitor);
            }
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        public override DateTimeOffset GetUtcNow() => Epoch;
        internal void ReachLastTimestamp() => Interlocked.Exchange(ref timestamp, long.MaxValue);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new InertTimer();

        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Invocation
    {
        private readonly TaskCompletionSource evaluationRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cleanupRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task EvaluationReleased => evaluationRelease.Task;
        internal Task CleanupReleased => cleanupRelease.Task;
        internal void ReleaseEvaluation() => evaluationRelease.TrySetResult();
        internal void ReleaseCleanup() => cleanupRelease.TrySetResult();
    }

    private sealed class GatedOwner(Clock clock) : IHealthCollector
    {
        private int acquisitions, cleanups;
        internal Invocation First { get; } = new();
        internal Invocation Second { get; } = new();
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Cleanups => Volatile.Read(ref cleanups);

        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            int ordinal = Interlocked.Increment(ref acquisitions);
            if (ordinal > 2) { lease = null; return false; }
            lease = new Lease(this, clock, ordinal == 1 ? First : Second, ordinal == 1);
            return true;
        }

        private sealed class Lease(GatedOwner owner, Clock clock, Invocation invocation, bool first) : IHealthCollectionLease
        {
            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                invocation.Entered.TrySetResult();
                await invocation.EvaluationReleased;
                lifetimeCancellation.ThrowIfCancellationRequested();
                return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
            }

            public async ValueTask DisposeAsync()
            {
                invocation.CleanupEntered.TrySetResult();
                await invocation.CleanupReleased;
                // Only the first real owner cleanup advances the valid injected
                // monotonic clock. There is no timer activation or clock failure.
                if (first) { clock.ReachLastTimestamp(); }
                Interlocked.Increment(ref owner.cleanups);
            }
        }
    }
}
