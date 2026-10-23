using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorPartialStartStopTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopIncludesAConcurrentTimerFactoryFailureWithoutRetainingAnUnsettledResource(bool timerFactoryFails)
    {
        using var clock = new GatedTimerProvider(timerFactoryFails);
        var service = new HealthcheckService(new(), new() { Name = "partial-start-stop", InstanceId = Guid.NewGuid() }, clock);
        var runner = new HealthCollectorRunner(service);
        Task? start = null;
        Task? stop = null;
        Exception? primary = null;
        try
        {
            start = Task.Run(() => runner.StartAsync());
            await clock.Entered.Task.WaitAsync(Deadline);
            stop = runner.StopAsync();
            Assert.IsFalse(stop.IsCompleted, "Stop must include the actual timer creation that is still gated.");

            clock.Release();
            if (timerFactoryFails)
            {
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => start).WaitAsync(Deadline);
            }
            else { await start.WaitAsync(Deadline); }

            // The real Start call has returned after the factory settled. No
            // evaluation, registration, or unreleased resource exists here.
            // Allow asynchronous completion too; do not invent a synchronous
            // public StopAsync completion requirement.
            await stop.WaitAsync(Deadline);
            Assert.AreEqual(1, clock.Creations);
            Assert.AreEqual(timerFactoryFails ? 0 : 1, clock.TimerDisposals);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.Release();
            // Request every real drain before observing any one of them. A
            // pending Start must not skip service or runner cleanup, and a
            // deadline cannot certify that the factory or stop barrier joined.
            Task startup = ObserveStartAsync();
            Task collector = DrainRunnerAsync();
            Task state = DrainServiceAsync();
            Task drains = Task.WhenAll(startup, collector, state);
            try { await drains.WaitAsync(Deadline); }
            catch (Exception cleanup) when (primary is not null)
            { throw new AggregateException("The receiving body failed and actual cleanup did not complete successfully.", primary, drains.Exception ?? cleanup); }
            catch (Exception cleanup) when (cleanup is not TimeoutException && drains.Exception is not null)
            { throw drains.Exception; }

            async Task ObserveStartAsync()
            {
                if (start is null) { return; }
                try { await start; }
                catch (InvalidOperationException) when (timerFactoryFails) { }
            }
            async Task DrainRunnerAsync() => await runner.DisposeAsync();
            async Task DrainServiceAsync() => await service.DisposeAsync();
        }
    }

    private sealed class GatedTimerProvider(bool fails) : TimeProvider, IDisposable
    {
        private readonly ManualResetEventSlim release = new();
        private int creations, timerDisposals;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Creations => Volatile.Read(ref creations);
        internal int TimerDisposals => Volatile.Read(ref timerDisposals);
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref creations);
            Entered.TrySetResult();
            release.Wait();
            if (fails) { throw new ControlledTimerFactoryException(); }
            return new Timer(this);
        }
        internal void Release() => release.Set();
        public void Dispose() => release.Dispose();
        private sealed class Timer(GatedTimerProvider owner) : ITimer
        {
            private int disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { Interlocked.Increment(ref owner.timerDisposals); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class ControlledTimerFactoryException : Exception;
}
