using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipeReceivingBoundaryQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow("health", HealthReportView.Health)]
    [DataRow("readiness", HealthReportView.Readiness)]
    [DataRow("details", HealthReportView.Details)]
    public async Task ASourceFailureRetainsTheCompleteSelectedPipeErrorEnvelope(string wireView, HealthReportView view)
    {
        var service = CreateService();
        var source = new SnapshotSource(service, failFirstCapture: true);
        var options = ServerOptions();
        var server = new HealthPipeServer(source, options);
        using var cancellation = new CancellationTokenSource(Deadline);
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            HealthSnapshot before = service.CaptureSnapshot();
            byte[] payload = await RequestAsync(options.PipeName!, wireView, cancellation.Token);
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
            byte[] recovered = await RequestAsync(options.PipeName!, wireView, cancellation.Token);
            HealthParsedResponse success = HealthReportProtocol.ParseResponse(recovered, view, options.MaxReportBytes);
            Assert.IsFalse(success.IsError);
            Assert.IsTrue(success.SelectedHealthy);
            Assert.AreEqual(2, source.Captures);
        }
        finally
        {
            await CloseAsync(server, service);
        }
    }

    [TestMethod]
    [DataRow("--timeout-ms", "-1")]
    [DataRow("--max-report-bytes", "1023")]
    public async Task AnOutOfRangeCliNumberCannotBecomeACompleteHealthyExchange(string option, string invalidValue)
    {
        var service = CreateService();
        var source = new SnapshotSource(service, failFirstCapture: false);
        var options = ServerOptions();
        var server = new HealthPipeServer(source, options);
        using var cancellation = new CancellationTokenSource(Deadline);
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            // The deliberately small, valid report fits the invalid 1023-byte
            // value. A size failure must not conceal acceptance of bad CLI input.
            HealthReportBuffer encoded = HealthReportFormatter.FormatPipeResponse(service.CaptureSnapshot(), HealthReportView.Health, options);
            Assert.IsTrue(encoded.Utf8Bytes.Length <= 1023);
            using (var primeOutput = new MemoryStream())
            using (var primeError = new StringWriter())
            {
                int prime = await HealthProbeClient.RunAsync(["--pipe", options.PipeName!], primeOutput, primeError, cancellation.Token).WaitAsync(Deadline);
                Assert.AreEqual(0, prime, "The real endpoint must first serve a complete healthy response.");
                Assert.IsTrue(primeOutput.Length > 0);
                Assert.AreEqual(string.Empty, primeError.ToString());
            }
            int capturesBefore = source.Captures;
            HealthSnapshot before = service.CaptureSnapshot();
            using var output = new MemoryStream();
            using var error = new StringWriter();
            int exit = await HealthProbeClient.RunAsync(["--pipe", options.PipeName!, option, invalidValue],
                output, error, cancellation.Token).WaitAsync(Deadline);
            Assert.AreEqual(2, exit, "C08 rejects out-of-range numbers even when a healthy endpoint exists.");
            Assert.AreEqual(0L, output.Length, "Rejected input cannot print a selected health snapshot.");
            Assert.AreEqual(capturesBefore, source.Captures, "Argument rejection must not start a reporting exchange.");
            Assert.IsTrue(error.ToString().Length is > 0 and <= 1024);
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
        }
        finally
        {
            await CloseAsync(server, service);
        }
    }

    [TestMethod]
    public async Task ARequestCompletedAtTheTotalDeadlineCannotEnterTheSnapshotSource()
    {
        var service = CreateService();
        var source = new SnapshotSource(service, failFirstCapture: false);
        var options = ServerOptions();
        var clock = new DelayedCallbackClock();
        var server = new HealthPipeServer(source, options, clock);
        using var cancellation = new CancellationTokenSource(Deadline);
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            HealthSnapshot before = service.CaptureSnapshot();
            using (var client = new NamedPipeClientStream(".", options.PipeName!, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                await client.ConnectAsync(cancellation.Token);
                await clock.TimerCreated.Task.WaitAsync(Deadline);
                byte[] request = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}");
                byte[] framed = HealthReportProtocol.EncodeFrame(request, 512);
                await client.WriteAsync(framed.AsMemory(0, framed.Length - 1), cancellation.Token);
                await client.FlushAsync(cancellation.Token);
                // This is a valid monotonic clock whose timer callback is late,
                // matching the existing deadline tests. Advance before the last
                // byte exists: a complete valid request cannot precede expiry.
                clock.Advance(options.PipeRequestTimeoutMs);
                await client.WriteAsync(framed.AsMemory(framed.Length - 1), cancellation.Token);
                await client.FlushAsync(cancellation.Token);
                try
                {
                    int received = await client.ReadAsync(new byte[1], cancellation.Token);
                    Assert.AreEqual(0, received, "An expired request cannot advertise a response prefix.");
                }
                catch (IOException) { /* Actual close may be reported as a broken pipe. */ }
            }
            // Peer closure establishes that request processing finished. The
            // later post-capture budget check also closes an expired exchange;
            // verify that it was rejected before entering the snapshot source.
            Assert.AreEqual(0, source.Captures, "A fully received expired request must not enter CaptureSnapshot.");
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);

            byte[] recovered = await RequestAsync(options.PipeName!, "health", cancellation.Token);
            Assert.IsTrue(HealthReportProtocol.ParseResponse(recovered, HealthReportView.Health, options.MaxReportBytes).SelectedHealthy);
            Assert.AreEqual(1, source.Captures);
        }
        finally
        {
            await CloseAsync(server, service);
        }
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

    private static async Task CloseAsync(HealthPipeServer server, HealthcheckService service)
    {
        try
        {
            try { await server.StopAsync().WaitAsync(Deadline); }
            finally { await server.DisposeAsync().AsTask().WaitAsync(Deadline); }
        }
        finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
    }

    private sealed class SnapshotSource(HealthcheckService service, bool failFirstCapture) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int current = Interlocked.Increment(ref captures);
            if (failFirstCapture && current == 1) { throw new InvalidOperationException("private-source-detail"); }
            return service.CaptureSnapshot();
        }
    }

    private sealed class DelayedCallbackClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Advance(long milliseconds) => Interlocked.Add(ref timestamp, milliseconds);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerCreated.TrySetResult();
            return new DelayedTimer();
        }

        private sealed class DelayedTimer : ITimer
        {
            private int disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public void Dispose() => Interlocked.Exchange(ref disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
