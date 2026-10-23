using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorIntervalValidationBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(42_949_672_950_000L)]
    [DataRow(long.MaxValue)]
    public async Task InvalidIntervalsRejectTheOptionsBeforeReadingAnUnavailableClock(long ticks)
    {
        await using var scope = new Scope();
        scope.Clock.FailReads = true;
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            scope.Runner.Register(scope.Monitor, scope.Owner, new() { Interval = TimeSpan.FromTicks(ticks) }));

        Assert.AreEqual("options", error.ParamName);
        Assert.AreEqual(0, scope.Clock.Reads, "An invalid interval cannot become a clock dependency failure before its argument is rejected.");
        Assert.AreEqual(0, scope.Owner.Acquisitions);
        HealthSnapshot after = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.IsNull(after.Monitors.Single().Collection);
    }

    [TestMethod]
    public async Task AValidIntervalActuallyReachesTheUnavailableClockBeforeAttaching()
    {
        await using var scope = new Scope();
        scope.Clock.FailReads = true;
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<ClockUnavailableException>(() =>
            scope.Runner.Register(scope.Monitor, scope.Owner, new() { Interval = TimeSpan.FromTicks(1) }));

        Assert.AreEqual(1, scope.Clock.Reads);
        Assert.AreEqual(0, scope.Owner.Acquisitions);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection);
    }

    [TestMethod]
    [DataRow(1L)]
    [DataRow(42_949_672_940_000L)]
    public async Task PositiveRepresentableIntervalEndpointsAttachWithoutStartingTheOwner(long ticks)
    {
        await using var scope = new Scope();
        IHealthCollectionRegistration registration = scope.Runner.Register(scope.Monitor, scope.Owner,
            new() { Interval = TimeSpan.FromTicks(ticks) });
        try
        {
            Assert.AreEqual(1, scope.Clock.Reads);
            Assert.AreEqual(0, scope.Owner.Acquisitions);
            Assert.IsNotNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection);
        }
        finally { await registration.DisposeAsync().AsTask().WaitAsync(Deadline); }
    }

    [TestMethod]
    public async Task TimerCreationFailureClosesRegistrationBeforeAnyExplicitStop()
    {
        await using var scope = new Scope();
        scope.Clock.FailTimerCreation = true;
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StartAsync()).WaitAsync(Deadline);

        // No StopAsync or disposal has run yet. Start's failure must close
        // admission itself, rather than require the caller to repair its state.
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Runner.Register(scope.Monitor, scope.Owner, new()));
        Assert.AreEqual(1, scope.Clock.TimerCreations);
        Assert.AreEqual(0, scope.Owner.Acquisitions);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection);
    }

    private sealed class Scope : IAsyncDisposable
    {
        internal Scope()
        {
            Service = new(new(), new HealthApplicationIdentity { Name = "collection interval receiving boundary", InstanceId = Guid.NewGuid() }, Clock);
            Monitor = Service.CreateCondition(new() { IsCritical = false, InitiallyFlagged = false });
            Runner = new(Service);
        }
        internal FaultingClock Clock { get; } = new();
        internal InertOwner Owner { get; } = new();
        internal HealthcheckService Service { get; }
        internal IConditionMonitor Monitor { get; }
        internal HealthCollectorRunner Runner { get; }
        public async ValueTask DisposeAsync()
        {
            try { await Runner.DisposeAsync().AsTask().WaitAsync(Deadline); }
            finally
            {
                await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
                GC.KeepAlive(Monitor);
            }
        }
    }

    private sealed class FaultingClock : TimeProvider
    {
        private int reads, timerCreations;
        internal bool FailReads { get; set; }
        internal bool FailTimerCreation { get; set; }
        internal int Reads => Volatile.Read(ref reads);
        internal int TimerCreations => Volatile.Read(ref timerCreations);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            Interlocked.Increment(ref reads);
            if (FailReads) { throw new ClockUnavailableException(); }
            return 0;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref timerCreations);
            if (FailTimerCreation) { throw new ClockUnavailableException(); }
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class InertOwner : IHealthCollector
    {
        private int acquisitions;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = null;
            return false;
        }
    }

    private sealed class ClockUnavailableException : Exception;
}
