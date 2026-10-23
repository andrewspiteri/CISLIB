using System.Threading.Channels;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorPeriodicTimerProgressQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompletionWhileTimerChangesArePendingSchedulesTheEarliestRealPeriodicEligibility(bool includeLaterOwner)
    {
        await using var scope = new Scope(includeLaterOwner);
        ChangePause startupPause = scope.Clock.PauseNextChange();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupPause.Entered.WaitAsync(Deadline);
        TimerRequest startupRequest = await scope.Clock.NextChangeAsync();
        Assert.IsTrue(startupRequest.Changed);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, startupRequest.DueTime);

        // Pause the actual dispatcher outside Core's gate. Activation records a
        // wake on that dispatcher, so its next Change follows real admission.
        ChangePause shortOccupiedPause = scope.Clock.PauseNextChange();
        scope.ShortRegistration.ActivatePeriodic();
        startupPause.Release();
        Invocation shortFirst = await scope.ShortOwner.NextAsync();
        await shortOccupiedPause.Entered.WaitAsync(Deadline);
        TimerRequest shortOccupiedRequest = await scope.Clock.NextChangeAsync();
        Assert.IsTrue(shortOccupiedRequest.Changed);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, shortOccupiedRequest.DueTime);
        Assert.AreEqual(1, scope.ShortOwner.Acquisitions);
        Assert.AreEqual(0, scope.ShortOwner.Cleanups);

        // The join records WakeRequested while the sole dispatcher is paused.
        // Awaiting it proves real cleanup/settlement, Due publication and actual
        // slot release; the completion's later Wake cannot cause a lost wake.
        Task<HealthCollectionAttempt> shortJoined = scope.ShortRegistration.CollectAsync();
        Assert.IsFalse(shortJoined.IsCompleted);
        shortFirst.Release();
        AssertCommitted(await shortJoined.WaitAsync(Deadline));
        Assert.AreEqual(1, scope.ShortOwner.Cleanups);
        Assert.AreEqual(1L, scope.ShortSnapshot().Outcomes!.Successful);

        // Both rows observe every next request, including Infinite. The two-owner
        // row pauses this actual five-second Change before admitting its later
        // owner, so no stale occupied-slot request can be mistaken for the final
        // idle scheduling decision.
        ChangePause? shortEligibilityPause = includeLaterOwner ? scope.Clock.PauseNextChange() : null;
        Task<TimerRequest> afterShortChange = scope.Clock.NextChangeAsync();
        shortOccupiedPause.Release();
        TimerRequest afterShortRequest = await afterShortChange;
        Assert.IsTrue(afterShortRequest.Changed);
        Assert.AreEqual(Interval, afterShortRequest.DueTime,
            "A settled periodic owner must install its completion-plus-interval eligibility on the real timer.");
        Assert.AreEqual(Timeout.InfiniteTimeSpan, afterShortRequest.Period);

        if (includeLaterOwner)
        {
            Assert.IsNotNull(shortEligibilityPause);
            await shortEligibilityPause.Entered.WaitAsync(Deadline);
            IHealthCollectionRegistration laterRegistration = scope.LaterRegistration!;
            laterRegistration.ActivatePeriodic();
            ChangePause laterOccupiedPause = scope.Clock.PauseNextChange();
            shortEligibilityPause.Release();
            Invocation laterFirst = await scope.LaterOwner.NextAsync();
            await laterOccupiedPause.Entered.WaitAsync(Deadline);
            TimerRequest laterOccupiedRequest = await scope.Clock.NextChangeAsync();
            Assert.IsTrue(laterOccupiedRequest.Changed);
            Assert.AreEqual(Timeout.InfiniteTimeSpan, laterOccupiedRequest.DueTime);
            Assert.AreEqual(1, scope.LaterOwner.Acquisitions);
            Assert.AreEqual(0, scope.LaterOwner.Cleanups);

            Task<HealthCollectionAttempt> laterJoined = laterRegistration.CollectAsync();
            Assert.IsFalse(laterJoined.IsCompleted);
            laterFirst.Release();
            AssertCommitted(await laterJoined.WaitAsync(Deadline));
            Assert.AreEqual(1, scope.LaterOwner.Cleanups);
            Assert.AreEqual(1L, scope.LaterSnapshot().Outcomes!.Successful);

            // The long owner's actual completion leaves two eligible future due
            // times at the same unchanged monotonic instant: five and twenty.
            // Read the following Change without filtering for either value.
            Task<TimerRequest> afterBothChange = scope.Clock.NextChangeAsync();
            laterOccupiedPause.Release();
            TimerRequest afterBothRequest = await afterBothChange;
            Assert.IsTrue(afterBothRequest.Changed);
            Assert.AreEqual(Interval, afterBothRequest.DueTime,
                "An idle dispatcher must install the earlier real eligibility, so a twenty-second owner cannot delay the five-second owner.");
            Assert.AreEqual(Timeout.InfiniteTimeSpan, afterBothRequest.Period);
        }

        Assert.AreEqual(0L, scope.Clock.GetTimestamp());
        scope.Clock.Advance(Interval);
        Invocation shortSecond = await scope.ShortOwner.NextAsync();
        Assert.AreEqual(2, scope.ShortOwner.Acquisitions);
        Assert.AreEqual(includeLaterOwner ? 1 : 0, scope.LaterOwner.Acquisitions);
        Task<HealthCollectionAttempt> shortSecondJoined = scope.ShortRegistration.CollectAsync();
        shortSecond.Release();
        AssertCommitted(await shortSecondJoined.WaitAsync(Deadline));
        Assert.AreEqual(2, scope.ShortOwner.Cleanups);
        Assert.AreEqual(2L, scope.ShortSnapshot().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.ShortSnapshot().Outcomes!.Failed);
        if (includeLaterOwner)
        {
            Assert.AreEqual(1L, scope.LaterSnapshot().Outcomes!.Successful);
            Assert.AreEqual(0L, scope.LaterSnapshot().Outcomes!.Failed);
        }
    }

    private static void AssertCommitted(HealthCollectionAttempt attempt)
    {
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
    }

    private sealed class Scope : IAsyncDisposable
    {
        internal Scope(bool includeLaterOwner)
        {
            Service = new(new() { MaxConcurrentCollections = 1 },
                new HealthApplicationIdentity { Name = "periodic timer progress", InstanceId = Guid.NewGuid() }, Clock);
            Runner = new(Service);
            ShortMonitor = CreateMonitor("retained five-second owner");
            ShortRegistration = Runner.Register(ShortMonitor, ShortOwner, new() { Interval = Interval });
            if (includeLaterOwner)
            {
                LaterMonitor = CreateMonitor("retained twenty-second owner");
                LaterRegistration = Runner.Register(LaterMonitor, LaterOwner, new() { Interval = TimeSpan.FromSeconds(20) });
            }
        }

        internal PausedTimerClock Clock { get; } = new();
        internal GatedOwner ShortOwner { get; } = new();
        internal GatedOwner LaterOwner { get; } = new();
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal IConsecutiveFailureMonitor ShortMonitor { get; }
        internal IConsecutiveFailureMonitor? LaterMonitor { get; }
        internal IHealthCollectionRegistration ShortRegistration { get; }
        internal IHealthCollectionRegistration? LaterRegistration { get; }
        internal HealthMonitorSnapshot ShortSnapshot() => Snapshot(ShortMonitor);
        internal HealthMonitorSnapshot LaterSnapshot() => Snapshot(LaterMonitor!);
        private HealthMonitorSnapshot Snapshot(IHealthMonitor monitor) => Service.CaptureSnapshot().Monitors
            .Single(item => item.MonitorId == monitor.MonitorId);
        private IConsecutiveFailureMonitor CreateMonitor(string name) => Service.CreateConsecutiveFailures(new()
        {
            Name = name,
            IsCritical = true,
            FailureThreshold = 1
        });

        public async ValueTask DisposeAsync()
        {
            // Release every real supplied dependency gate before terminal drain,
            // even when the direct five-versus-twenty-or-Infinite assertion fails.
            Clock.ReleaseAllPauses();
            ShortOwner.ReleaseAll();
            LaterOwner.ReleaseAll();
            try { await Runner.DisposeAsync().AsTask().WaitAsync(Deadline); }
            finally
            {
                try { await Service.DisposeAsync().AsTask().WaitAsync(Deadline); }
                finally
                {
                    Clock.DisposePauses();
                    GC.KeepAlive(ShortRegistration);
                    GC.KeepAlive(LaterRegistration);
                    GC.KeepAlive(ShortMonitor);
                    GC.KeepAlive(LaterMonitor);
                }
            }
        }
    }

    private sealed record TimerRequest(TimeSpan DueTime, TimeSpan Period, bool Changed);

    private sealed class PausedTimerClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly ManualTimeProvider inner = new(Epoch);
        private readonly Channel<TimerRequest> changes = Channel.CreateUnbounded<TimerRequest>();
        private readonly List<ChangePause> pauses = [];
        private ChangePause? nextPause;

        public override long TimestampFrequency => inner.TimestampFrequency;
        public override long GetTimestamp() => inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new ObservedTimer(this, inner.CreateTimer(callback, state, dueTime, period));
        internal void Advance(TimeSpan elapsed) => inner.Advance(elapsed);
        internal Task<TimerRequest> NextChangeAsync() => changes.Reader.ReadAsync().AsTask().WaitAsync(Deadline);

        internal ChangePause PauseNextChange()
        {
            lock (gate)
            {
                if (nextPause is not null) { throw new InvalidOperationException("The previous timer pause has not been entered."); }
                var pause = new ChangePause();
                pauses.Add(pause);
                nextPause = pause;
                return pause;
            }
        }

        private void Observe(TimerRequest request)
        {
            ChangePause? pause;
            lock (gate) { pause = nextPause; nextPause = null; }
            changes.Writer.TryWrite(request);
            pause?.EnterAndWait();
        }

        internal void ReleaseAllPauses()
        {
            lock (gate) { foreach (ChangePause pause in pauses) { pause.Release(); } }
        }

        internal void DisposePauses()
        {
            lock (gate) { foreach (ChangePause pause in pauses) { pause.Dispose(); } }
        }

        private sealed class ObservedTimer(PausedTimerClock owner, ITimer inner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = inner.Change(dueTime, period);
                owner.Observe(new(dueTime, period, changed));
                return changed;
            }

            public void Dispose() => inner.Dispose();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private sealed class ChangePause : IDisposable
    {
        private readonly ManualResetEventSlim release = new();
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Entered => entered.Task;
        internal void Release() => release.Set();
        internal void EnterAndWait()
        {
            entered.TrySetResult();
            release.Wait();
        }
        public void Dispose() => release.Dispose();
    }

    private sealed class Invocation
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Released => release.Task;
        internal void Release() => release.TrySetResult();
    }

    private sealed record OwnerProgress(Invocation? Invocation);

    private sealed class GatedOwner : IHealthCollector
    {
        private readonly object gate = new();
        private readonly List<Invocation> invocations = [];
        private readonly Channel<OwnerProgress> entered = Channel.CreateUnbounded<OwnerProgress>();
        private bool releasing;
        private int acquisitions, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Cleanups => Volatile.Read(ref cleanups);

        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease(this);
            return true;
        }

        internal async Task<Invocation> NextAsync()
        {
            OwnerProgress observed = await entered.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
            Assert.IsNotNull(observed.Invocation, "The actual owner lease was disposed before its expected periodic callback entered.");
            return observed.Invocation;
        }
        internal void ReleaseAll()
        {
            lock (gate)
            {
                releasing = true;
                foreach (Invocation invocation in invocations) { invocation.Release(); }
            }
        }

        private async Task<HealthCollectionResult> EvaluateAsync(CancellationToken cancellation)
        {
            var invocation = new Invocation();
            lock (gate)
            {
                invocations.Add(invocation);
                if (releasing) { invocation.Release(); }
            }
            entered.Writer.TryWrite(new(invocation));
            await invocation.Released;
            cancellation.ThrowIfCancellationRequested();
            return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
        }

        private sealed class Lease(GatedOwner owner) : IHealthCollectionLease
        {
            private bool entered;
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                entered = true;
                return owner.EvaluateAsync(lifetimeCancellation);
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.cleanups);
                if (!entered) { owner.ReportSkippedEvaluation(); }
                return ValueTask.CompletedTask;
            }
        }
        private void ReportSkippedEvaluation() => entered.Writer.TryWrite(new(null));
    }
}
