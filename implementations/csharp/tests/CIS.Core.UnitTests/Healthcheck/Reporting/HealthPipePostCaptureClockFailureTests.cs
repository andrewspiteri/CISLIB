using System.IO.Pipes;
using System.Text;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipePostCaptureClockFailureTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task AOneShotClockFailureAfterCaptureProducesAnIsolatedErrorAndLeavesTheEndpointReusable()
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
        var server = new HealthPipeServer(source, options, clock);
        using var cancellation = new CancellationTokenSource(Deadline);
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            HealthSnapshot before = service.CaptureSnapshot();
            HealthParsedResponse initial = HealthReportProtocol.ParseResponse(
                await RequestAsync(options.PipeName!, cancellation.Token), HealthReportView.Health, options.MaxReportBytes);
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
            try { failedResponse = await RequestAsync(options.PipeName!, cancellation.Token); }
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
            await server.StartAsync().WaitAsync(Deadline);
            HealthParsedResponse recovered = HealthReportProtocol.ParseResponse(
                await RequestAsync(options.PipeName!, cancellation.Token), HealthReportView.Health, options.MaxReportBytes);
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
        finally
        {
            try
            {
                try { await server.StopAsync().WaitAsync(Deadline); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(Deadline); }
            }
            finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
        }
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

    private sealed class CapturingSource(HealthcheckService service, OneShotFaultClock clock) : IHealthSnapshotSource
    {
        private int captures, failAfterCapture;
        internal int Captures => Volatile.Read(ref captures);
        internal void FailClockAfterNextCapture() => Volatile.Write(ref failAfterCapture, 1);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref captures);
            HealthSnapshot snapshot = service.CaptureSnapshot();
            if (Interlocked.Exchange(ref failAfterCapture, 0) != 0) { clock.FailNextTimestamp(); }
            return snapshot;
        }
    }

    private sealed class OneShotFaultClock : TimeProvider
    {
        private int failNext, faults;
        internal int Faults => Volatile.Read(ref faults);
        internal void FailNextTimestamp() => Volatile.Write(ref failNext, 1);
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override long GetTimestamp()
        {
            if (Interlocked.Exchange(ref failNext, 0) != 0)
            {
                Interlocked.Increment(ref faults);
                throw new ClockSampleUnavailableException();
            }
            return TimeProvider.System.GetTimestamp();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            TimeProvider.System.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class ClockSampleUnavailableException : Exception;
}
