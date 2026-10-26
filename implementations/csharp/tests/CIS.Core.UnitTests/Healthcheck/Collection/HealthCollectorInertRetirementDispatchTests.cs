using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorInertRetirementDispatchTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task RetiringAnUnstartedRegistrationDoesNotDispatchTheCollectorPump()
    {
        var clock = new DispatchClock();
        var owner = new InertOwner();
        HealthcheckService? service = null;
        IConsecutiveFailureMonitor? monitor = null;
        HealthCollectorRunner? runner = null;
        IHealthCollectionRegistration? registration = null;
        int timestampsBeforeRetirement = 0;
        Exception? primary = null;
        var cleanupFailures = new List<Exception>();
        var bodyDrains = new List<Task>();
        Task ObserveBody(Task actual)
        {
            bodyDrains.Add(actual);
            return actual.WaitAsync(Deadline);
        }
        try
        {
            service = new(new(), new() { Name = "inert retirement dispatcher", InstanceId = Guid.NewGuid() }, clock);
            monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            runner = new(service);
            // Interval=null has no registration-time monotonic sample. No Start,
            // ActivatePeriodic or Collect call is made anywhere in this test.
            registration = runner.Register(monitor, owner, new());
            timestampsBeforeRetirement = Volatile.Read(ref clock.TimestampCalls);

            // Dispose invokes the real Withdraw synchronously before returning
            // its actual entry-retirement task. A wrongly queued Pump sets the
            // runner's pumpScheduled barrier before this call returns.
            await ObserveBody(registration.DisposeAsync().AsTask());
            await ObserveBody(registration.WaitForDisposalAsync());
            await ObserveBody(runner.StopAsync());
            await ObserveBody(runner.DisposeAsync().AsTask());
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // Initiate every present real public cleanup independently, even
            // after an earlier synchronous throw or failed/pending sibling.
            var actualDrains = new List<Task>(bodyDrains);
            void Begin(Func<Task> operation)
            {
                try { actualDrains.Add(operation()); }
                catch (Exception error) { Record(error); }
            }
            if (registration is not null) { Begin(() => registration.DisposeAsync().AsTask()); }
            if (runner is not null)
            {
                Begin(() => runner.StopAsync());
                Begin(() => runner.DisposeAsync().AsTask());
            }
            if (monitor is not null) { Begin(() => monitor.DisposeAsync().AsTask()); }
            if (service is not null) { Begin(() => service.DisposeAsync().AsTask()); }
            Task drains = Task.WhenAll(actualDrains);
            try { await drains.WaitAsync(Deadline); }
            catch (Exception error) { Record(drains.Exception is { } fault ? fault : error); }
            // A pending WhenAll must not conceal an already completed actual
            // fault/cancellation in a sibling behind the watchdog exception.
            foreach (Task actual in actualDrains)
            {
                if (!actual.IsFaulted && !actual.IsCanceled) { continue; }
                try { actual.GetAwaiter().GetResult(); }
                catch (Exception error) { Record(error); }
            }
            foreach (Exception error in clock.Errors.Concat(owner.Errors)) { Record(error); }
        }

        void Record(Exception error)
        {
            if (error is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions) { Record(inner); }
            }
            else if (!ReferenceEquals(error, primary) && !cleanupFailures.Any(known => ReferenceEquals(known, error))) { cleanupFailures.Add(error); }
        }

        if (cleanupFailures.Count != 0)
        {
            if (primary is not null) { cleanupFailures.Insert(0, primary); }
            throw new AggregateException("Actual inert-retirement producer work or cleanup did not settle.", cleanupFailures);
        }
        if (primary is not null) { ExceptionDispatchInfo.Capture(primary).Throw(); }

        // Successful actual Stop/Dispose includes every queued Pump through
        // pumpScheduled/SignalProgress. The count is sampled only AFTER all
        // registration, runner, monitor and service drains above have returned.
        Assert.AreEqual(timestampsBeforeRetirement, Volatile.Read(ref clock.TimestampCalls),
            "Retiring an unstarted registration must not dispatch a collector Pump.");
        Assert.AreEqual(0, Volatile.Read(ref clock.TimerCalls));
        Assert.AreEqual(0, Volatile.Read(ref owner.Acquisitions));
    }

    private sealed class DispatchClock : TimeProvider
    {
        internal int TimestampCalls, TimerCalls;
        internal ConcurrentQueue<Exception> Errors { get; } = new();
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override long GetTimestamp()
        {
            Interlocked.Increment(ref TimestampCalls);
            return TimeProvider.System.GetTimestamp();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref TimerCalls);
            var error = new InvalidOperationException("An unstarted runner must not request a timer.");
            Errors.Enqueue(error);
            throw error;
        }
    }

    private sealed class InertOwner : IHealthCollector
    {
        internal int Acquisitions;
        internal ConcurrentQueue<Exception> Errors { get; } = new();
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            lease = null;
            Interlocked.Increment(ref Acquisitions);
            var error = new InvalidOperationException("An unstarted runner must not acquire an owner lease.");
            Errors.Enqueue(error);
            throw error;
        }
    }
}
