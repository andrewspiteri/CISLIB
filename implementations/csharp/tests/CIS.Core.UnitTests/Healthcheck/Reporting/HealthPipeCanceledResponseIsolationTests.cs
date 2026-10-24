using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed partial class HealthPipeCanceledResponseIsolationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ARealDeadlineCallbackDuringCaptureClosesOnlyThatConnection(bool sourceThrowsAfterCancellation)
    {
        ObserveCanceledSettlement(RunCanceledScenario(sourceThrowsAfterCancellation));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CanceledObservation RunCanceledScenario(bool sourceThrowsAfterCancellation)
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity
        {
            Name = "canceled response isolation",
            InstanceId = Guid.NewGuid()
        });
        service.MarkStartupCompleted();
        var clock = new CallbackControlledClock();
        var source = new CancelingSource(service, clock, sourceThrowsAfterCancellation);
        var options = new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = "hc-cr-" + Guid.NewGuid().ToString("N"),
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 1000,
            MaxReportBytes = 4096
        };
        HealthPipeServer? server = null;
        using var cancellation = new CancellationTokenSource(Deadline);
        Exception? failure = null;
        try
        {
            // Every actual client and deadline timer belongs to a later phase.
            // Hand out the fresh server only after its real startup succeeds.
            server = CreateStartedServer(source, options, clock);
            HealthSnapshot before = service.CaptureSnapshot();
            using (var client = CreateClient(options.PipeName!))
            {
                JoinCanceled(client.ConnectAsync(cancellation.Token));
                JoinCanceled(SendRequestAsync(client, cancellation.Token));
                try
                {
                    int received = JoinCanceled(client.ReadAsync(new byte[1], cancellation.Token).AsTask());
                    Assert.AreEqual(0, received, "A canceled capture cannot advertise a report prefix.");
                }
                catch (IOException) { /* Actual peer closure can be reported as a broken pipe. */ }
            }
            Assert.AreEqual(1, source.Captures);
            Assert.AreEqual(1, clock.CallbacksFired);

            // Actual EOF follows the canceled handler. A connection deadline
            // must leave the public server running, including idempotent Start.
            // This checks admission, without requiring synchronous completion.
            Task? repeatedStart = null;
            InvalidOperationException? stopped = null;
            try { repeatedStart = server.StartAsync(); }
            catch (InvalidOperationException error) { stopped = error; }
            Assert.IsNull(stopped, "Canceling one response must not terminally stop the server.");
            JoinCanceled(repeatedStart ?? throw new InvalidOperationException("No start wait was returned."));

            using (var client = CreateClient(options.PipeName!))
            {
                JoinCanceled(client.ConnectAsync(cancellation.Token));
                JoinCanceled(SendRequestAsync(client, cancellation.Token));
                byte[] response = JoinCanceled(HealthReportProtocol.ReadFrameAsync(client, options.MaxReportBytes, cancellation.Token));
                HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(response, HealthReportView.Health, options.MaxReportBytes);
                Assert.IsFalse(parsed.IsError);
                Assert.IsTrue(parsed.SelectedHealthy);
                Assert.IsFalse(parsed.SnapshotUtf8.IsEmpty);
            }
            Assert.AreEqual(2, source.Captures);
            Assert.AreEqual(1, clock.CallbacksFired);
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
        }
        catch (Exception error) { failure = error; }

        return ReleaseCanceledOwners(server, service, source, clock, sourceThrowsAfterCancellation, failure);
    }

    private static NamedPipeClientStream CreateClient(string pipe) =>
        new(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static Task SendRequestAsync(Stream client, CancellationToken cancellation) =>
        HealthReportProtocol.WriteFrameAsync(client, Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), 512, cancellation);

    private sealed class CancelingSource(HealthcheckService service, CallbackControlledClock clock, bool throwsAfterCancellation) : IHealthSnapshotSource
    {
        internal int Captures => Volatile.Read(ref clock.Phase.Captures);

        public HealthSnapshot CaptureSnapshot()
        {
            int capture = Interlocked.Increment(ref clock.Phase.Captures);
            OperationCanceledException? expectedCancellation = null;
            try
            {
                if (capture == 1)
                {
                    // Deliver the same actual callback at the same original
                    // capture location and due time. Cleanup never fires it.
                    clock.FireCurrentDeadline(1000);
                    if (throwsAfterCancellation)
                    {
                        expectedCancellation = new OperationCanceledException("The source observed cancellation.");
                        throw expectedCancellation;
                    }
                }
                return service.CaptureSnapshot();
            }
            catch (Exception error)
            {
                // Only this exact producer-owned first-capture exception is
                // intentional. Callback/service/other cancellation faults are
                // retained separately and remain runtime cleanup holds.
                if (ReferenceEquals(error, expectedCancellation) && error.GetType() == typeof(OperationCanceledException)
                    && expectedCancellation!.Message == "The source observed cancellation."
                    && expectedCancellation.CancellationToken == default && capture == 1 && throwsAfterCancellation
                    && clock.CallbacksFired == 1 && Volatile.Read(ref clock.Phase.CallbackEntries) == 1
                    && Volatile.Read(ref clock.Phase.CallbackReturns) == 1)
                { Interlocked.Increment(ref clock.Phase.ExpectedSourceCancellations); }
                else { clock.Phase.RecordFailure(error); }
                throw;
            }
            finally { Interlocked.Increment(ref clock.Phase.SourceExits); }
        }
    }

    private sealed class CallbackControlledClock : TimeProvider
    {
        private readonly object gate = new();
        private ControlledTimer? current;
        private readonly List<ControlledTimer> timers = [];
        private long timestamp;
        internal CanceledPhase Phase { get; } = new();
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal int CallbacksFired => Volatile.Read(ref Phase.CallbacksFired);
        internal bool TimerCreated { get { lock (gate) { return current is not null; } } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(callback, state, Phase);
            lock (gate) { current = timer; timers.Add(timer); }
            Interlocked.Increment(ref Phase.CreatedTimers);
            return timer;
        }

        internal void FireCurrentDeadline(long milliseconds)
        {
            ControlledTimer timer;
            lock (gate) { timer = current ?? throw new InvalidOperationException("No actual deadline timer exists."); }
            Interlocked.Add(ref timestamp, milliseconds);
            timer.Fire();
            Interlocked.Increment(ref Phase.CallbacksFired);
        }

        internal void JoinBothActualDisposals()
        {
            ControlledTimer[] actual; lock (gate) { actual = [.. timers]; }
            if (actual.Length != 2 || Volatile.Read(ref Phase.CreatedTimers) != 2)
            { throw new InvalidOperationException("Exactly the two original actual request timers must exist."); }
            // These signals observe the actual synchronous timer Dispose body;
            // no signal is manufactured by the fixture's cleanup.
            JoinCanceled(Task.WhenAll(actual.Select(timer => timer.DisposalReturned)));
        }

        internal WeakReference[] WeakGraph()
        {
            lock (gate) { return [new(this), .. timers.Select(timer => new WeakReference(timer))]; }
        }

        // This existing private timer has no scheduler. Only the original
        // CaptureSnapshot call above invokes Fire. Its callback/state stay
        // readonly and are never cleared or strongly returned to the caller.
        private sealed class ControlledTimer(TimerCallback callback, object? state, CanceledPhase phase) : ITimer
        {
            private int disposed;
            private int fired;
            private readonly TaskCompletionSource disposalReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task DisposalReturned => disposalReturned.Task;
            internal void Fire()
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (Interlocked.Exchange(ref fired, 1) != 0) { throw new InvalidOperationException("The deadline callback already fired."); }
                Interlocked.Increment(ref phase.CallbackEntries);
                try { callback(state); }
                catch (Exception error) { phase.RecordFailure(error); throw; }
                finally { Interlocked.Increment(ref phase.CallbackReturns); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public void Dispose()
            {
                Interlocked.Increment(ref phase.DisposalRequests);
                if (Interlocked.Exchange(ref disposed, 1) == 0) { Interlocked.Increment(ref phase.DisposedTimers); }
                Interlocked.Increment(ref phase.DisposalReturns);
                disposalReturned.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
