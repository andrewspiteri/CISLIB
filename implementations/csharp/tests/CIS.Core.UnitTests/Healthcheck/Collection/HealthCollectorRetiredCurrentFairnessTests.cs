using System.Threading.Channels;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRetiredCurrentFairnessTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task RemovingTheRunningRegistrationPreservesTheNextWaitingOwnersTurn()
    {
        var service = new HealthcheckService(new() { MaxConcurrentCollections = 1 },
            new HealthApplicationIdentity { Name = "retired current collection fairness", InstanceId = Guid.NewGuid() }, new AdvancingClock());
        var clock = (AdvancingClock)service.Clock;
        var runner = new HealthCollectorRunner(service);
        var entered = Channel.CreateUnbounded<OwnerProgress>(new UnboundedChannelOptions { SingleReader = true });
        var monitors = new List<IConditionMonitor>();
        var owners = new List<GatedOwner>();
        var registrations = new List<IHealthCollectionRegistration>();
        try
        {
            foreach (string name in new[] { "A", "B", "C", "D" })
            {
                IConditionMonitor monitor = service.CreateCondition(new() { IsCritical = false, InitiallyFlagged = false, Name = name });
                var owner = new GatedOwner(name, entered.Writer);
                monitors.Add(monitor);
                owners.Add(owner);
                registrations.Add(runner.Register(monitor, owner, new() { Interval = TimeSpan.FromTicks(1) }));
            }
            Task<TimeSpan> startupChange = clock.NextChangeAsync();
            await runner.StartAsync().WaitAsync(Deadline);
            await startupChange;
            Assert.AreEqual(0, owners.Sum(owner => owner.Acquisitions),
                "Actual startup must leave every unactivated periodic owner inert.");
            Task<TimeSpan> activatedChange = clock.NextChangeAsync();
            registrations[0].ActivatePeriodic();
            await activatedChange;
            Assert.AreEqual(1, owners[0].Acquisitions,
                "Actual activation must acquire A before installing its next idle timer request.");
            Invocation current = await NextInvocationAsync(entered.Reader);
            Assert.AreEqual("A", current.Name);
            for (int index = 1; index < registrations.Count; index++) { registrations[index].ActivatePeriodic(); }

            // A owns the real capacity slot. Removing its earlier list position
            // must retain the scan's next logical owner, B, while A drains.
            registrations[0].Withdraw();
            Task retirement = registrations[0].WaitForDisposalAsync();
            Assert.IsFalse(retirement.IsCompleted, "The executing owner's lease still belongs to the withdrawn registration.");
            Assert.AreEqual(0, owners[1].Acquisitions);
            current.Release.TrySetResult();
            await retirement.WaitAsync(Deadline);

            foreach (string expected in new[] { "B", "C", "D" })
            {
                Invocation next = await NextInvocationAsync(entered.Reader);
                Assert.AreEqual(expected, next.Name, "Removing the current list position cannot let a later queued owner jump the next waiting owner.");
                next.Release.TrySetResult();
            }
            Assert.AreEqual(1, owners[0].Acquisitions);
            Assert.AreEqual(1, owners[0].LeaseDisposals);
            Assert.IsTrue(service.CaptureSnapshot().Monitors.Any(monitor => monitor.MonitorId == monitors[0].MonitorId),
                "Retiring an external collection registration preserves its monitor.");
        }
        finally
        {
            try
            {
                Task stopped = runner.StopAsync();
                foreach (GatedOwner owner in owners) { owner.ReleaseAll(); }
                await stopped.WaitAsync(Deadline);
                foreach (IHealthCollectionRegistration registration in registrations) { await registration.DisposeAsync().AsTask().WaitAsync(Deadline); }
                await runner.DisposeAsync().AsTask().WaitAsync(Deadline);
            }
            finally
            {
                foreach (GatedOwner owner in owners) { owner.ReleaseAll(); }
                await service.DisposeAsync().AsTask().WaitAsync(Deadline);
                GC.KeepAlive(monitors);
            }
        }
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private long timestamp;
        private TaskCompletionSource<TimeSpan>? nextChange;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Increment(ref timestamp);
        internal Task<TimeSpan> NextChangeAsync()
        {
            var next = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref nextChange, next, null) is not null)
            { throw new InvalidOperationException("The previous actual timer Change observation is still armed."); }
            return next.Task.WaitAsync(Deadline);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new ObservedTimer(base.CreateTimer(callback, state, dueTime, period), this);
        private sealed class ObservedTimer(ITimer inner, AdvancingClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = inner.Change(dueTime, period);
                if (changed) { Interlocked.Exchange(ref clock.nextChange, null)?.TrySetResult(dueTime); }
                return changed;
            }
            public void Dispose() => inner.Dispose();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private static async Task<Invocation> NextInvocationAsync(ChannelReader<OwnerProgress> progress)
    {
        OwnerProgress observed = await progress.ReadAsync().AsTask().WaitAsync(Deadline);
        Assert.IsNotNull(observed.Invocation, "The actual owner lease was disposed before its expected collection callback entered.");
        return observed.Invocation;
    }

    private sealed record OwnerProgress(Invocation? Invocation);

    private sealed class Invocation(string name)
    {
        internal string Name { get; } = name;
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedOwner(string name, ChannelWriter<OwnerProgress> entered) : IHealthCollector
    {
        private readonly object gate = new();
        private readonly List<Invocation> invocations = [];
        private int acquisitions, disposals;
        private bool releasing;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int LeaseDisposals => Volatile.Read(ref disposals);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease(this);
            return true;
        }
        internal void ReleaseAll()
        {
            lock (gate)
            {
                releasing = true;
                foreach (Invocation invocation in invocations) { invocation.Release.TrySetResult(); }
            }
        }
        private async Task<HealthCollectionResult> EvaluateAsync()
        {
            var invocation = new Invocation(name);
            lock (gate)
            {
                invocations.Add(invocation);
                if (releasing) { invocation.Release.TrySetResult(); }
            }
            if (!entered.TryWrite(new(invocation))) { throw new InvalidOperationException("The actual collection observation channel is closed."); }
            await invocation.Release.Task;
            return HealthCollectionResult.Condition(false);
        }
        private sealed class Lease(GatedOwner owner) : IHealthCollectionLease
        {
            private bool entered;
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                entered = true;
                return owner.EvaluateAsync();
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.disposals);
                if (!entered) { owner.ReportSkippedEvaluation(); }
                return ValueTask.CompletedTask;
            }
        }

        private void ReportSkippedEvaluation() => entered.TryWrite(new(null));
    }
}
