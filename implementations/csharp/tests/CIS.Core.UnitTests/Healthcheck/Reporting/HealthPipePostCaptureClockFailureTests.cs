using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

// Forced collections for lifetime checks must not overlap other real pipe deadlines.
[TestClass]
[DoNotParallelize]
public sealed partial class HealthPipePostCaptureClockFailureTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void AOneShotClockFailureAfterCaptureProducesAnIsolatedErrorAndLeavesTheEndpointReusable()
    {
        ReviewPostCaptureOwners(RunPostCaptureScenario());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PostCaptureObservation RunPostCaptureScenario()
    {
        var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "post-capture clock", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        var clock = new OneShotFaultClock();
        var source = new CapturingSource(service, clock);
        var options = new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = "hc-clock-" + Guid.NewGuid().ToString("N"),
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 5000,
            MaxReportBytes = 1024
        };
        HealthPipeServer? server = null;
        var cancellation = new CancellationTokenSource(Deadline);
        var requests = new List<Task<byte[]>>();
        Exception? primary = null;
        try
        {
            // Every actual request and owned timer remains after successful
            // startup; a failed startup never hands its server to this caller.
            server = CreateStartedServer(source, options, clock);
            HealthSnapshot before = service.CaptureSnapshot();
            HealthParsedResponse initial = HealthReportProtocol.ParseResponse(
                RequestAndJoin(options.PipeName!, cancellation.Token, requests), HealthReportView.Health, options.MaxReportBytes);
            Assert.IsFalse(initial.IsError);
            Assert.IsTrue(initial.SelectedHealthy);
            Assert.AreEqual(1, source.Captures);
            Assert.AreEqual(0, clock.Faults);

            // The first full exchange is a control. Only the source's next actual
            // capture arms an outage on the supplied clock's next timestamp read.
            // There are no fabricated time values, private fields or I/O contexts.
            source.FailClockAfterNextCapture();
            byte[]? failedResponse = null;
            Exception? prematureClosure = null;
            try { failedResponse = RequestAndJoin(options.PipeName!, cancellation.Token, requests); }
            catch (Exception failure) when (failure is IOException or FormatException)
            {
                // The shared frame reader normalizes an early end of stream to
                // FormatException. Both forms mean the real response was lost.
                prematureClosure = failure;
            }

            Assert.AreEqual(2, source.Captures);
            Assert.AreEqual(1, clock.Faults);
            Assert.IsNull(prematureClosure,
                "This one-shot failure inside the source/format isolation boundary must produce its bounded error, rather than terminally closing the server.");
            Assert.IsNotNull(failedResponse);
            HealthParsedResponse error = HealthReportProtocol.ParseResponse(
                failedResponse, HealthReportView.Health, options.MaxReportBytes);
            Assert.IsTrue(error.IsError);
            Assert.AreEqual("ReportingUnavailable", error.ErrorCode);
            Assert.IsFalse(error.SelectedHealthy);
            Assert.IsTrue(error.SnapshotUtf8.IsEmpty);

            // Starting an already running server and serving a new real request
            // establish that a reporting failure did not become terminal stop.
            JoinPostCapture(server.StartAsync());
            HealthParsedResponse recovered = HealthReportProtocol.ParseResponse(
                RequestAndJoin(options.PipeName!, cancellation.Token, requests), HealthReportView.Health, options.MaxReportBytes);
            Assert.IsFalse(recovered.IsError);
            Assert.IsTrue(recovered.SelectedHealthy);
            Assert.AreEqual(3, source.Captures);
            Assert.AreEqual(1, clock.Faults);
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
        }
        catch (Exception error) { primary = error; }
        return ReleasePostCaptureOwners(server, service, source, clock, options, cancellation, requests, primary);
    }

    private static async Task<byte[]> RequestAsync(string pipe, CancellationToken cancellation)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(cancellation);
        await HealthReportProtocol.WriteFrameAsync(client,
            Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), 512, cancellation);
        return await HealthReportProtocol.ReadFrameAsync(client, 1024, cancellation);
    }

    private static byte[] RequestAndJoin(string pipe, CancellationToken cancellation, List<Task<byte[]>> actualRequests)
    {
        Task<byte[]> actual = RequestAsync(pipe, cancellation);
        actualRequests.Add(actual);
        return JoinPostCapture(actual);
    }

    private sealed class CapturingSource(HealthcheckService service, OneShotFaultClock clock) : IHealthSnapshotSource
    {
        private int failAfterCapture;
        internal PostCaptureSourcePhase Phase { get; } = new();
        internal int Captures => Volatile.Read(ref Phase.Captures);
        internal void FailClockAfterNextCapture() => Volatile.Write(ref failAfterCapture, 1);
        public HealthSnapshot CaptureSnapshot()
        {
            int capture = Interlocked.Increment(ref Phase.Captures);
            try
            {
                HealthSnapshot snapshot = service.CaptureSnapshot();
                if (Interlocked.Exchange(ref failAfterCapture, 0) != 0) { clock.FailNextTimestamp(capture); }
                return snapshot;
            }
            catch (Exception error) { Phase.RecordFailure(error); throw; }
            finally { Interlocked.Increment(ref Phase.Exits); }
        }
    }

    private sealed class OneShotFaultClock(TimeProvider? sampleClock = null) : TimeProvider
    {
        private readonly TimeProvider actualSamples = sampleClock ?? TimeProvider.System;
        private readonly object gate = new();
        private readonly List<ObservedPostCaptureTimer> timers = [];
        private int failNext;
        internal PostCaptureClockPhase Phase { get; } = new();
        internal int Faults => Volatile.Read(ref Phase.Faults);
        internal int TimersCreated => Volatile.Read(ref Phase.Created);
        internal void FailNextTimestamp(int actualCapture)
        {
            Volatile.Write(ref Phase.ArmedCapture, actualCapture);
            Interlocked.Increment(ref Phase.Arms);
            Volatile.Write(ref failNext, 1);
        }
        public override long TimestampFrequency => actualSamples.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => actualSamples.GetUtcNow();
        public override long GetTimestamp()
        {
            try
            {
                if (Interlocked.Exchange(ref failNext, 0) != 0)
                {
                    Interlocked.Increment(ref Phase.Faults);
                    var actual = new ClockSampleUnavailableException();
                    Phase.RecordProduced(actual, Volatile.Read(ref Phase.ArmedCapture));
                    throw actual;
                }
                return actualSamples.GetTimestamp();
            }
            catch (Exception error) { Phase.RecordClockFailure(error); throw; }
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ObservedPostCaptureTimer(this, callback, state);
            timer.Publish(TimeProvider.System.CreateTimer(timer.Invoke, null, dueTime, period));
            lock (gate) { timers.Add(timer); }
            Interlocked.Increment(ref Phase.Created);
            return timer;
        }
        internal void JoinActualRequestDisposals()
        {
            ObservedPostCaptureTimer[] actual; lock (gate) { actual = [.. timers]; }
            if (actual.Length != 3 || Volatile.Read(ref Phase.Created) != 3)
            { throw new InvalidOperationException("Exactly three actual server request timers are required."); }
            foreach (ObservedPostCaptureTimer timer in actual)
            {
                timer.JoinActualDisposal();
                Interlocked.Increment(ref Phase.JoinedDisposals);
            }
        }
        internal WeakReference[] WeakGraph() { lock (gate) { return [new(this), .. timers.Select(timer => new WeakReference(timer))]; } }
    }

    private sealed class ClockSampleUnavailableException : Exception;
}
