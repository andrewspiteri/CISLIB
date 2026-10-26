using System.Reflection;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipeOwnedSemaphoreDisposalTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task DisposingAnInertServerReleasesItsActualOwnedEndpointSemaphoreHandle()
    {
        var source = new InertSource();
        var clock = new InertClock();
        var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = false }, clock);
        SemaphoreSlim? actualOwner = null;
        WaitHandle? borrowedHandle = null;
        SafeWaitHandle? observedHandle = null;
        Exception? primary = null;
        var cleanupFailures = new List<Exception>();
        try
        {
            // Read the actual private owner; do not replace its state or handle.
            FieldInfo? field = typeof(HealthPipeServer).GetField("endpointChanges", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            Assert.AreEqual(typeof(SemaphoreSlim), field.FieldType);
            actualOwner = (SemaphoreSlim?)field.GetValue(server);
            Assert.IsNotNull(actualOwner);

            // SemaphoreSlim.AvailableWaitHandle allocates a handle owned by its semaphore.
            // The fixture borrows that actual handle and retains a read-only
            // SafeWaitHandle observation; it never directly closes either one.
            borrowedHandle = actualOwner.AvailableWaitHandle;
            observedHandle = borrowedHandle.SafeWaitHandle;
            Assert.IsFalse(observedHandle.IsClosed);
            Assert.AreEqual(0, source.Captures);
            Assert.AreEqual(0, clock.TimerCalls);

            await server.DisposeAsync().AsTask().WaitAsync(Deadline);
            Assert.IsTrue(observedHandle.IsClosed,
                "Disposing the server must close its actual owned endpoint semaphore handle.");
            Assert.AreEqual(0, source.Captures);
            Assert.AreEqual(0, clock.TimerCalls);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // Start both real ancillary APIs independently, including after an
            // assertion. An unfinished or faulted API remains a cleanup hold.
            Task stopped;
            try { stopped = server.StopAsync(); }
            catch (Exception error) { stopped = Task.FromException(error); }
            Task disposed;
            try { disposed = server.DisposeAsync().AsTask(); }
            catch (Exception error) { disposed = Task.FromException(error); }
            Task drains = Task.WhenAll(stopped, disposed);
            try { await drains.WaitAsync(Deadline); }
            catch (Exception error) { RecordCleanup(drains.Exception is { } fault ? fault : error); }
            // WhenAll remains pending while either sibling is pending. Preserve
            // every already-completed actual fault/cancellation independently;
            // a watchdog must not hide that error behind its own Timeout.
            foreach (Task actual in new[] { stopped, disposed })
            {
                if (!actual.IsFaulted && !actual.IsCanceled) { continue; }
                try { actual.GetAwaiter().GetResult(); }
                catch (Exception error) { RecordCleanup(error); }
            }

            if (stopped.IsCompletedSuccessfully && disposed.IsCompletedSuccessfully && actualOwner is not null)
            {
                try
                {
                    // This is fixture cleanup AFTER the SUT resource assertion
                    // and actual public joins. Under the one missing-Dispose
                    // leaf it releases the residual real resource through its
                    // actual owning semaphore, without repairing the saved assertion.
                    actualOwner.Dispose();
                    if (observedHandle is not null && !observedHandle.IsClosed)
                    {
                        cleanupFailures.Add(new InvalidOperationException("The actual endpoint semaphore owner did not release its observed handle during fixture cleanup."));
                    }
                }
                catch (Exception error) { cleanupFailures.Add(error); }
            }
            GC.KeepAlive(borrowedHandle);
            if (source.Captures != 0 || clock.TimerCalls != 0)
            {
                cleanupFailures.Add(new InvalidOperationException("The inert server unexpectedly entered snapshot or request-timer work."));
            }
        }

        void RecordCleanup(Exception error)
        {
            if (error is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions) { RecordCleanup(inner); }
            }
            else if (!ReferenceEquals(error, primary) && !cleanupFailures.Any(known => ReferenceEquals(known, error)))
            { cleanupFailures.Add(error); }
        }

        if (cleanupFailures.Count != 0)
        {
            if (primary is not null) { cleanupFailures.Insert(0, primary); }
            throw new AggregateException("The actual inert-server resource observation or cleanup did not settle.", cleanupFailures);
        }
        if (primary is not null) { ExceptionDispatchInfo.Capture(primary).Throw(); }
    }

    private sealed class InertSource : IHealthSnapshotSource
    {
        internal int Captures;
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref Captures);
            throw new InvalidOperationException("The inert server must not request a snapshot.");
        }
    }

    private sealed class InertClock : TimeProvider
    {
        internal int TimerCalls;
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref TimerCalls);
            throw new InvalidOperationException("The inert server must not create a request timer.");
        }
    }
}
