using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed partial class HealthPipeActiveCancellationBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow("stop")]
    [DataRow("dispose")]
    public void ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource(string operation)
    {
        ActiveSettlementObservation observed = RunConnectedSourceScenario(operation);
        ReviewActualActiveSettlement(observed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ActiveSettlementObservation RunConnectedSourceScenario(string operation)
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity
        {
            Name = "connected transport closure",
            InstanceId = Guid.NewGuid()
        });
        service.MarkStartupCompleted();
        using var release = new ManualResetEventSlim();
        var source = new GatedSource(service.CaptureSnapshot(), release);
        var clock = new ObservedBudgetClock();
        string pipe = "hc-active-stop-" + Guid.NewGuid().ToString("N");
        var options = new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = pipe,
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 60000
        };
        HealthPipeServer? server = null;
        NamedPipeClientStream? client = null;
        using var setup = new CancellationTokenSource(Deadline);
        Task<int>? reading = null;
        Task? terminal = null;
        Exception? failure = null;
        try
        {
            // Verify the real detached settings prerequisite before creating
            // any server or client. This extra copy owns no runtime resources;
            // the original options still go through the real server constructor.
            HealthcheckOptions detached = options.ValidatedCopy();
            Assert.IsNotNull(detached, "Valid transport settings must produce their detached copy.");
            Assert.IsTrue(detached.PipeEnabled, "The detached transport settings must retain enabled admission before real startup.");
            Assert.AreEqual(pipe, detached.PipeName, "The detached transport settings must retain the requested local endpoint.");
            Assert.AreEqual(1, detached.PipeMaxClients, "The detached transport settings must retain the one-client scenario.");
            Assert.AreEqual(60000, detached.PipeRequestTimeoutMs, "The detached transport settings must retain the configured request budget.");
            // No actual client exists until the fresh endpoint's startup has
            // succeeded and its concrete startup owner can be handed to us.
            server = CreateStartedServer(source, options, clock);
            client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            JoinActual(client.ConnectAsync(setup.Token));
            JoinActual(HealthReportProtocol.WriteFrameAsync(client,
                Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), 512, setup.Token));
            JoinActual(source.Entered);

            // The actual request is inside CaptureSnapshot. No response byte
            // can be generated until this source's owned release gate opens.
            reading = client.ReadAsync(new byte[1], CancellationToken.None).AsTask();
            Assert.IsFalse(reading.IsCompleted);
            terminal = operation == "stop" ? server.StopAsync() : server.DisposeAsync().AsTask();
            Assert.IsFalse(terminal.IsCompleted, "The transport still owns the source invocation that has entered.");
            try
            {
                Assert.AreEqual(0, JoinActual(reading), "Closing a pending response cannot emit a successful prefix.");
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

        return ReleaseAndObserveActualActiveOwners(server, source, clock, service, release, client, reading, terminal, failure);
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
        private readonly TaskCompletionSource returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int captures, returns;
        private Exception? actualFailure;
        internal Task Entered => entered.Task;
        internal int Captures => Volatile.Read(ref captures);
        internal int Returns => Volatile.Read(ref returns);
        internal Task Returned => returned.Task;
        internal Exception? ActualFailure => Volatile.Read(ref actualFailure);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref captures);
            entered.TrySetResult();
            try
            {
                if (!release.Wait(TimeSpan.FromSeconds(30))) { throw new TimeoutException("The owned source was not released during cleanup."); }
                return snapshot;
            }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref actualFailure, error, null);
                throw;
            }
            finally { Interlocked.Increment(ref returns); returned.TrySetResult(); }
        }
    }
}
