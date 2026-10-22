using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipeActiveCancellationBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow("stop")]
    [DataRow("dispose")]
    public async Task ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource(string operation)
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity
        {
            Name = "connected transport closure",
            InstanceId = Guid.NewGuid()
        });
        service.MarkStartupCompleted();
        using var release = new ManualResetEventSlim();
        var source = new GatedSource(service.CaptureSnapshot(), release);
        string pipe = "hc-active-stop-" + Guid.NewGuid().ToString("N");
        var server = new HealthPipeServer(source, new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = pipe,
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 60000
        });
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var setup = new CancellationTokenSource(Deadline);
        Task<int>? reading = null;
        Task? terminal = null;
        Exception? failure = null;
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            await client.ConnectAsync(setup.Token);
            await HealthReportProtocol.WriteFrameAsync(client,
                Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), 512, setup.Token);
            await source.Entered.WaitAsync(Deadline);

            // The actual request is inside CaptureSnapshot. No response byte
            // can be generated until this source's owned release gate opens.
            reading = client.ReadAsync(new byte[1], CancellationToken.None).AsTask();
            Assert.IsFalse(reading.IsCompleted);
            terminal = operation == "stop" ? server.StopAsync() : server.DisposeAsync().AsTask();
            Assert.IsFalse(terminal.IsCompleted, "The transport still owns the source invocation that has entered.");
            try
            {
                Assert.AreEqual(0, await reading.WaitAsync(Deadline), "Closing a pending response cannot emit a successful prefix.");
            }
            catch (IOException)
            {
                // The peer's actual close may be EOF or an I/O error on the
                // executed platform. A canceled local wait is not closure.
            }
            catch (TimeoutException)
            {
                Assert.Fail("The connected client must observe transport closure while the actual source remains gated.");
            }
            Assert.IsFalse(release.IsSet);
            Assert.AreEqual(1, source.Captures);
            Assert.IsFalse(terminal.IsCompleted, "Peer closure does not certify actual source settlement.");
        }
        catch (Exception error) { failure = error; }

        await FinishPreservingFailureAsync(failure, async () =>
        {
            // Release the actual callback before joining any transport drain.
            // Disposing the client also releases its real read on a mutant.
            release.Set();
            client.Dispose();
            try
            {
                if (reading is not null)
                {
                    try { _ = await reading.WaitAsync(Deadline); }
                    catch (Exception error) when (error is IOException or ObjectDisposedException) { }
                }
            }
            finally
            {
                try { await (terminal ?? server.StopAsync()).WaitAsync(Deadline); }
                finally
                {
                    try { await server.DisposeAsync().AsTask().WaitAsync(Deadline); }
                    finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
                }
            }
        });
    }

    [TestMethod]
    public async Task CallerCancellationAfterARealRequestDoesNotWaitForThePeerToSendAResponse()
    {
        string pipe = "hc-active-probe-" + Guid.NewGuid().ToString("N");
        using var peer = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var output = new MemoryStream();
        using var error = new StringWriter();
        using var caller = new CancellationTokenSource();
        using var setup = new CancellationTokenSource(Deadline);
        Task<int>? running = null;
        Exception? failure = null;
        try
        {
            running = HealthProbeClient.RunAsync(["--pipe", pipe, "--timeout-ms", "60000"], output, error, caller.Token);
            await peer.WaitForConnectionAsync(setup.Token);
            byte[] request = await HealthReportProtocol.ReadFrameAsync(peer, 512, setup.Token);
            using (JsonDocument json = JsonDocument.Parse(request))
            {
                Assert.AreEqual(1, json.RootElement.GetProperty("protocolVersion").GetInt32());
                Assert.AreEqual("health", json.RootElement.GetProperty("view").GetString());
            }
            Assert.IsFalse(caller.IsCancellationRequested);
            Assert.IsFalse(running.IsCompleted, "This actual server has received the request and deliberately sends no response.");

            caller.Cancel();
            try { Assert.AreEqual(2, await running.WaitAsync(Deadline)); }
            catch (TimeoutException)
            {
                Assert.Fail("Canceling the caller after request delivery must complete the probe while its peer remains open and silent.");
            }
            Assert.IsFalse(peer.SafePipeHandle.IsClosed, "Completion cannot rely on this test closing the actual peer.");
            Assert.AreEqual(0L, output.Length);
            StringAssert.Contains(error.ToString(), "TimeoutOrCanceled");
            Assert.IsTrue(error.ToString().Length <= 1024);
            output.WriteByte(0x42);
            error.Write("still caller owned");
        }
        catch (Exception errorValue) { failure = errorValue; }

        await FinishPreservingFailureAsync(failure, async () =>
        {
            caller.Cancel();
            // Close the real peer before joining a faulty noncancelable read.
            // The original assertion survives; normal cleanup failures are not suppressed.
            peer.Dispose();
            if (running is not null) { _ = await running.WaitAsync(Deadline); }
        });
    }

    private static async Task FinishPreservingFailureAsync(Exception? failure, Func<Task> cleanup)
    {
        try { await cleanup(); }
        catch (Exception cleanupFailure)
        {
            if (failure is not null)
            {
                throw new AggregateException("The receiving assertion and actual owned-operation cleanup both failed.", failure, cleanupFailure);
            }
            throw;
        }
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private sealed class GatedSource(HealthSnapshot snapshot, ManualResetEventSlim release) : IHealthSnapshotSource
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int captures;
        internal Task Entered => entered.Task;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref captures);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(30))) { throw new TimeoutException("The owned source was not released during cleanup."); }
            return snapshot;
        }
    }
}
