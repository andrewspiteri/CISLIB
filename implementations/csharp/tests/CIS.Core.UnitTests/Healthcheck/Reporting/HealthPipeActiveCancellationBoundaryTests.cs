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
        Task? connecting = null;
        Task<byte[]>? reading = null;
        Exception? failure = null;
        try
        {
            running = HealthProbeClient.RunAsync(["--pipe", pipe, "--timeout-ms", "60000"], output, error, caller.Token);
            if (running.IsCompleted)
            {
                // Observe a real early fault or cancellation before asserting
                // that the valid probe must progress to its actual request.
                _ = await running;
                Assert.Fail("The valid probe must deliver its actual request and await this silent peer before caller cancellation.");
            }
            connecting = peer.WaitForConnectionAsync(setup.Token);
            await AwaitActualRequestProgressAsync(connecting, running);
            reading = HealthReportProtocol.ReadFrameAsync(peer, 512, setup.Token);
            await AwaitActualRequestProgressAsync(reading, running);
            byte[] request = await reading;
            using (JsonDocument json = JsonDocument.Parse(request))
            {
                Assert.AreEqual(1, json.RootElement.GetProperty("protocolVersion").GetInt32());
                Assert.AreEqual("health", json.RootElement.GetProperty("view").GetString());
            }
            Assert.IsFalse(caller.IsCancellationRequested);
            if (running.IsCompleted) { _ = await running; }
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

        await FinishProbeOwnersAsync(failure, setup, caller, peer, connecting, reading, running);
    }

    private static async Task AwaitActualRequestProgressAsync(Task progress, Task<int> running)
    {
        await Task.WhenAny(progress, running).WaitAsync(Deadline);
        if (running.IsCompleted)
        {
            // Observe the original probe's fault or cancellation before asserting
            // that a valid request cannot finish while its peer sends no response.
            _ = await running;
            Assert.Fail("The valid probe must deliver its actual request and await this silent peer before caller cancellation.");
        }
        await progress;
    }

    private static async Task FinishProbeOwnersAsync(Exception? failure, CancellationTokenSource setup,
        CancellationTokenSource caller, NamedPipeServerStream peer, Task? connecting, Task<byte[]>? reading, Task<int>? running)
    {
        var failures = new List<Exception>();
        Task[] actual = new Task?[] { connecting, reading, running }.OfType<Task>().ToArray();
        var observed = new HashSet<Task>();
        bool cleanupCanceledSetup = false;
        if (failure is not null) { Record(failure); }
        ObserveCompleted();
        cleanupCanceledSetup = !setup.IsCancellationRequested;
        Attempt(setup.Cancel);
        Attempt(caller.Cancel);
        // Cancel setup first, then close the real peer to release any faulty
        // noncancelable read. All original tasks share one cleanup watchdog.
        Attempt(peer.Dispose);
        Task joined = Task.WhenAll(actual);
        try { await joined.WaitAsync(Deadline); }
        catch (Exception error)
        {
            // Completed tasks' individual errors are observed below, including
            // faults hidden behind a sibling which remains pending.
            if (error is TimeoutException || !joined.IsCompleted) { Record(error); }
        }
        ObserveCompleted();
        if (failures.Count == 1) { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1) { throw new AggregateException("The receiving assertion and actual owned-operation cleanup failed.", failures); }

        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { Record(error); }
        }

        void ObserveCompleted()
        {
            foreach (Task task in actual)
            {
                if (!task.IsCompleted || !observed.Add(task)) { continue; }
                try { task.GetAwaiter().GetResult(); }
                catch (OperationCanceledException error) when (cleanupCanceledSetup && task.IsCanceled
                    && !ReferenceEquals(task, running) && error.CancellationToken == setup.Token)
                {
                    // Only cancellation requested by this cleanup on its own
                    // connection/frame token is expected.
                }
                catch (Exception error) { Record(task.Exception ?? error); }
            }
        }

        void Record(Exception error)
        {
            if (error is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions) { Record(inner); }
            }
            else if (!failures.Any(existing => ReferenceEquals(existing, error))) { failures.Add(error); }
        }
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
