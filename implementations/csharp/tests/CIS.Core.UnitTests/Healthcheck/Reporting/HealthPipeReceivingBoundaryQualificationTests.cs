using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed partial class HealthPipeReceivingBoundaryQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow("health", HealthReportView.Health)]
    [DataRow("readiness", HealthReportView.Readiness)]
    [DataRow("details", HealthReportView.Details)]
    public void ASourceFailureRetainsTheCompleteSelectedPipeErrorEnvelope(string wireView, HealthReportView view)
    {
        ReviewReceivingOwners(RunSourceErrorScenario(wireView, view));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReceivingObservation RunSourceErrorScenario(string wireView, HealthReportView view)
    {
        var service = CreateService();
        var source = new SnapshotSource(service, failFirstCapture: true);
        var options = ServerOptions();
        var clock = new ReceivingSystemClock();
        HealthPipeServer? server = null;
        var cancellation = new CancellationTokenSource(Deadline);
        var actualClients = new List<Task>();
        Exception? failure = null;
        try
        {
            server = CreateStartedServer(source, options, clock);
            HealthSnapshot before = service.CaptureSnapshot();
            byte[] payload = RequestAndJoin(options.PipeName!, wireView, cancellation.Token, actualClients);
            using (JsonDocument response = JsonDocument.Parse(payload))
            {
                JsonElement root = response.RootElement;
                // C08 requires a pipe envelope even when CaptureSnapshot fails.
                // Checking just kind/error.code also accepts an HTTP error body.
                Assert.IsTrue(root.TryGetProperty("protocolVersion", out JsonElement protocolVersion));
                Assert.AreEqual(1, protocolVersion.GetInt32());
                Assert.IsTrue(root.TryGetProperty("schemaVersion", out JsonElement schemaVersion));
                Assert.AreEqual(1, schemaVersion.GetInt32());
                Assert.IsTrue(root.TryGetProperty("view", out JsonElement selectedView));
                Assert.AreEqual(wireView, selectedView.GetString());
                Assert.AreEqual("error", root.GetProperty("kind").GetString());
                Assert.AreEqual("ReportingUnavailable", root.GetProperty("error").GetProperty("code").GetString());
                Assert.IsFalse(root.TryGetProperty("snapshot", out _));
            }
            HealthParsedResponse error = HealthReportProtocol.ParseResponse(payload, view, options.MaxReportBytes);
            Assert.IsTrue(error.IsError);
            Assert.IsFalse(error.SelectedHealthy);
            Assert.AreEqual("ReportingUnavailable", error.ErrorCode);
            Assert.IsTrue(error.SnapshotUtf8.IsEmpty);
            Assert.IsFalse(Encoding.UTF8.GetString(payload).Contains("private-source-detail", StringComparison.Ordinal));

            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);

            // A genuine source exception must leave the endpoint reusable.
            byte[] recovered = RequestAndJoin(options.PipeName!, wireView, cancellation.Token, actualClients);
            HealthParsedResponse success = HealthReportProtocol.ParseResponse(recovered, view, options.MaxReportBytes);
            Assert.IsFalse(success.IsError);
            Assert.IsTrue(success.SelectedHealthy);
            Assert.AreEqual(2, source.Captures);
        }
        catch (Exception error) { failure = error; }
        return ReleaseReceivingOwners(server, service, source, clock, options, cancellation, actualClients, ReceivingScenario.SourceError, failure);
    }

    [TestMethod]
    [DataRow("--timeout-ms", "-1")]
    [DataRow("--max-report-bytes", "1023")]
    public void AnOutOfRangeCliNumberCannotBecomeACompleteHealthyExchange(string option, string invalidValue)
    {
        ReviewReceivingOwners(RunInvalidCliScenario(option, invalidValue));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReceivingObservation RunInvalidCliScenario(string option, string invalidValue)
    {
        var service = CreateService();
        var source = new SnapshotSource(service, failFirstCapture: false);
        var options = ServerOptions();
        var clock = new ReceivingSystemClock();
        HealthPipeServer? server = null;
        var cancellation = new CancellationTokenSource(Deadline);
        var actualClients = new List<Task>();
        Exception? failure = null;
        try
        {
            server = CreateStartedServer(source, options, clock);
            // The deliberately small, valid report fits the invalid 1023-byte
            // value. A size failure must not conceal acceptance of bad CLI input.
            HealthReportBuffer encoded = HealthReportFormatter.FormatPipeResponse(service.CaptureSnapshot(), HealthReportView.Health, options);
            Assert.IsTrue(encoded.Utf8Bytes.Length <= 1023);
            using (var primeOutput = new MemoryStream())
            using (var primeError = new StringWriter())
            {
                int prime = ProbeAndJoin(["--pipe", options.PipeName!], primeOutput, primeError, cancellation.Token, actualClients);
                Assert.AreEqual(0, prime, "The real endpoint must first serve a complete healthy response.");
                Assert.IsTrue(primeOutput.Length > 0);
                Assert.AreEqual(string.Empty, primeError.ToString());
            }
            int capturesBefore = source.Captures;
            HealthSnapshot before = service.CaptureSnapshot();
            using var output = new MemoryStream();
            using var error = new StringWriter();
            int exit = ProbeAndJoin(["--pipe", options.PipeName!, option, invalidValue],
                output, error, cancellation.Token, actualClients);
            Assert.AreEqual(2, exit, "C08 rejects out-of-range numbers even when a healthy endpoint exists.");
            Assert.AreEqual(0L, output.Length, "Rejected input cannot print a selected health snapshot.");
            Assert.AreEqual(capturesBefore, source.Captures, "Argument rejection must not start a reporting exchange.");
            Assert.IsTrue(error.ToString().Length is > 0 and <= 1024);
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
        }
        catch (Exception error) { failure = error; }
        return ReleaseReceivingOwners(server, service, source, clock, options, cancellation, actualClients, ReceivingScenario.InvalidCli, failure);
    }

    [TestMethod]
    public void ARequestCompletedAtTheTotalDeadlineCannotEnterTheSnapshotSource()
    {
        ReviewReceivingOwners(RunDeadlineScenario());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReceivingObservation RunDeadlineScenario()
    {
        var service = CreateService();
        var source = new SnapshotSource(service, failFirstCapture: false);
        var options = ServerOptions();
        var clock = new DelayedCallbackClock();
        HealthPipeServer? server = null;
        var cancellation = new CancellationTokenSource(Deadline);
        var actualClients = new List<Task>();
        Exception? failure = null;
        try
        {
            server = CreateStartedServer(source, options, clock);
            HealthSnapshot before = service.CaptureSnapshot();
            Task expired = RunExpiredClientAsync(options.PipeName!, options.PipeRequestTimeoutMs, clock, cancellation.Token);
            actualClients.Add(expired);
            JoinReceiving(expired);
            // Peer closure establishes that request processing finished. The
            // later post-capture budget check also closes an expired exchange;
            // verify that it was rejected before entering the snapshot source.
            Assert.AreEqual(0, source.Captures, "A fully received expired request must not enter CaptureSnapshot.");
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);

            byte[] recovered = RequestAndJoin(options.PipeName!, "health", cancellation.Token, actualClients);
            Assert.IsTrue(HealthReportProtocol.ParseResponse(recovered, HealthReportView.Health, options.MaxReportBytes).SelectedHealthy);
            Assert.AreEqual(1, source.Captures);
        }
        catch (Exception error) { failure = error; }
        return ReleaseReceivingOwners(server, service, source, clock, options, cancellation, actualClients, ReceivingScenario.Deadline, failure);
    }

    private static HealthcheckService CreateService()
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity { Name = "p", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        return service;
    }

    private static HealthcheckOptions ServerOptions() => new()
    {
        PipeEnabled = true,
        PipeName = "hc-boundary-" + Guid.NewGuid().ToString("N"),
        PipeMaxClients = 1,
        DetailsEnabled = true,
        MaxReportBytes = 1024,
        PipeRequestTimeoutMs = 1000
    };

    private static async Task<byte[]> RequestAsync(string pipe, string wireView, CancellationToken cancellation)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(cancellation);
        byte[] request = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"" + wireView + "\"}");
        await HealthReportProtocol.WriteFrameAsync(client, request, 512, cancellation);
        return await HealthReportProtocol.ReadFrameAsync(client, 1024, cancellation);
    }

    private static async Task RunExpiredClientAsync(string pipe, int timeoutMilliseconds, DelayedCallbackClock clock, CancellationToken cancellation)
    {
        using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await client.ConnectAsync(cancellation);
            await clock.TimerCreated.Task.WaitAsync(Deadline);
            byte[] request = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}");
            byte[] framed = HealthReportProtocol.EncodeFrame(request, 512);
            await client.WriteAsync(framed.AsMemory(0, framed.Length - 1), cancellation);
            await client.FlushAsync(cancellation);
            // This is a valid monotonic clock whose timer callback is late,
            // matching the existing deadline tests. Advance before the last
            // byte exists: a complete valid request cannot precede expiry.
            clock.Advance(timeoutMilliseconds);
            await client.WriteAsync(framed.AsMemory(framed.Length - 1), cancellation);
            await client.FlushAsync(cancellation);
            try
            {
                int received = await client.ReadAsync(new byte[1], cancellation);
                Assert.AreEqual(0, received, "An expired request cannot advertise a response prefix.");
            }
            catch (IOException) { /* Actual close may be reported as a broken pipe. */ }
        }
    }

    private static byte[] RequestAndJoin(string pipe, string wireView, CancellationToken cancellation, List<Task> actualClients)
    {
        Task<byte[]> actual = RequestAsync(pipe, wireView, cancellation);
        actualClients.Add(actual);
        return JoinReceiving(actual);
    }
    private static int ProbeAndJoin(string[] arguments, Stream output, TextWriter error, CancellationToken cancellation, List<Task> actualClients)
    {
        Task<int> actual = HealthProbeClient.RunAsync(arguments, output, error, cancellation);
        actualClients.Add(actual);
        return JoinReceiving(actual);
    }

    private sealed class SnapshotSource(HealthcheckService service, bool failFirstCapture) : IHealthSnapshotSource
    {
        internal ReceivingSourcePhase Phase { get; } = new();
        internal int Captures => Volatile.Read(ref Phase.Captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int current = Interlocked.Increment(ref Phase.Captures);
            try
            {
                if (failFirstCapture && current == 1)
                {
                    var actual = new InvalidOperationException("private-source-detail");
                    Phase.RecordProduced(actual, current);
                    throw actual;
                }
                return service.CaptureSnapshot();
            }
            catch (Exception error) { Phase.RecordFailure(error); throw; }
            finally { Interlocked.Increment(ref Phase.Exits); }
        }
    }

    private sealed class DelayedCallbackClock() : ReceivingClock(manual: true)
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Advance(long milliseconds)
        {
            Interlocked.Increment(ref Phase.Advances);
            Interlocked.Add(ref Phase.AdvancedMilliseconds, milliseconds);
            Interlocked.Add(ref timestamp, milliseconds);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerCreated.TrySetResult();
            // Preserve the original intentionally delayed timer: no callback
            // or state is retained or invoked by this private manual timer.
            var timer = new DelayedTimer();
            RecordActualTimer(timer);
            return timer;
        }
        private sealed class DelayedTimer : ReceivingTimer
        {
            private int disposed;
            public override bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public override void Dispose()
            {
                Interlocked.Increment(ref DisposalRequests);
                Interlocked.Exchange(ref disposed, 1);
                Interlocked.Increment(ref DisposalReturns);
                Returned.TrySetResult();
            }
            public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            internal override void JoinActualDisposal() => JoinReceiving(Returned.Task);
        }
    }
}
