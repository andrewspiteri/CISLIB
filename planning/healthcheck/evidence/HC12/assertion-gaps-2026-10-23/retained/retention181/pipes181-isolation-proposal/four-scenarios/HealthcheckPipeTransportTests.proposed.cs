using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MutationChildren = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildSupport;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

[TestClass]
public sealed partial class HealthcheckPipeTransportTests
{
    [TestMethod]
    public async Task WindowsPipeEndpointDescriptorsReleasePinsAfterActualSettlement()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows native pipe security-descriptor pin lifetime is inapplicable on this platform.");
            return;
        }
        Process child = Process.Start(TransportTestSupport.StartInfo("healthcheck-http-host", "CIS.Healthcheck.HttpFixture", ["pipe-pin-release"]))!;
        try
        {
            MutationChildren.ObserveStarted(child);
            Task<string> stdout = child.StandardOutput.ReadToEndAsync(), stderr = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TransportTestSupport.Limit); }
            catch (TimeoutException) { MutationChildren.MarkTimeout(child, "pipe-pin-release-observation"); throw; }
            string output = await stdout, error = await stderr;
            Assert.AreEqual(0, child.ExitCode, error);
            Assert.AreEqual("", error, "The resource observation must finish without a child error.");
            Assert.IsTrue(output.Length is > 0 and <= 1024, "The isolated observation is one bounded JSON record.");
            string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            Assert.HasCount(1, lines);
            JsonObject observation = JsonNode.Parse(lines[0])!.AsObject();
            Assert.AreEqual("observed", (string?)observation["status"]);
            Assert.AreEqual("pipe-pin-release", (string?)observation["diagnostic"]);
            Assert.AreEqual("Windows", (string?)observation["platform"]);
            Assert.AreEqual(child.Id, (int?)observation["pid"]);
            Assert.AreEqual(".NET 10.0.12", (string?)observation["runtime"]);
            Assert.AreEqual(8, (int?)observation["warmupServers"]);
            Assert.AreEqual(32, (int?)observation["measuredServers"]);
            long before = (long?)observation["pinnedBefore"] ?? throw new AssertFailedException("Missing initial pin count.");
            long after = (long?)observation["pinnedAfter"] ?? throw new AssertFailedException("Missing final pin count.");
            Assert.IsTrue(before >= 0 && after >= 0);
            Assert.IsTrue(after <= before,
                $"Actually stopped and disposed pipe endpoints retained pins after full collection: before={before}, after={after}.");
        }
        finally
        {
            try
            {
                if (!child.HasExited)
                {
                    MutationChildren.MarkForcedStop(child, "pipe-pin-release-cleanup");
                    child.Kill(entireProcessTree: true);
                }
                await child.WaitForExitAsync().WaitAsync(TransportTestSupport.Limit);
            }
            catch (TimeoutException) { MutationChildren.MarkTimeout(child, "pipe-pin-release-stop"); throw; }
            finally { try { MutationChildren.CompleteLifecycle(child); } finally { child.Dispose(); } }
        }
    }

    private static (HealthcheckService Service, IConditionMonitor Monitor) Create(bool unhealthy = false, bool startup = true)
    {
        var service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "pipe-fixture", InstanceId = Guid.NewGuid() });
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = unhealthy, Name = "critical" });
        if (startup) { service.MarkStartupCompleted(); }
        return (service, monitor);
    }

    [TestMethod]
    public async Task AnOccupiedEndpointFaultsPublicStartAndTheOriginalOwnerRemainsUsable()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            // Leave a spare OS instance: failure must respect the existing name,
            // rather than depend solely on exhausting its instance capacity.
            await using var owner = new NamedPipeServerStream(pipe, PipeDirection.InOut, 2,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var source = new CountingSource(service);
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 2 });
            Task? stopping = null, disposing = null;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    await Assert.ThrowsExactlyAsync<Win32Exception>(() => server.StartAsync()).WaitAsync(TransportTestSupport.Limit);
                }
                else
                {
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => server.StartAsync()).WaitAsync(TransportTestSupport.Limit);
                }
                Assert.ThrowsExactly<InvalidOperationException>(() => server.StartAsync(), "A failed start must already have closed admission.");
                Assert.AreEqual(0, source.Captures);
                stopping = server.StopAsync();
                await stopping.WaitAsync(TransportTestSupport.Limit);
                disposing = server.DisposeAsync().AsTask();
                await disposing.WaitAsync(TransportTestSupport.Limit);

                using var cancellation = new CancellationTokenSource(TransportTestSupport.Limit);
                Task accepted = owner.WaitForConnectionAsync(cancellation.Token);
                await using var peer = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                await peer.ConnectAsync(cancellation.Token);
                await accepted.WaitAsync(TransportTestSupport.Limit);
                byte[] received = new byte[1];
                // This BCL constructor requests zero-sized pipe buffers. Post the
                // matching read before awaiting a Windows write's completion.
                await Task.WhenAll(owner.ReadExactlyAsync(received, cancellation.Token).AsTask(),
                    peer.WriteAsync(new byte[] { 0x5a }, cancellation.Token).AsTask());
                Assert.AreEqual((byte)0x5a, received[0], "Failed start and cleanup must leave the real original endpoint usable.");
            }
            finally
            {
                await new TransportTestSupport.PipeServerCleanup(server).DisposeAsync();
            }
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public void AnActualRequestTimerCreationFailureClosesAdmissionWithoutReadingOrChangingHealth()
    {
        RunTimerCreationScenario();
    }

    [TestMethod]
    [DataRow("health", false, true, false)]
    [DataRow("health", true, true, true)]
    [DataRow("readiness", false, false, true)]
    [DataRow("readiness", false, true, false)]
    [DataRow("details", false, true, true)]
    [DataRow("details", true, true, false)]
    public void ActualLocalServerUsesCompleteSingleSnapshotAndFragmentedLittleEndianFrames(string view, bool unhealthy, bool startup, bool fragmented)
    {
        SingleSnapshotObservation observed = RunSingleSnapshotScenario(view, unhealthy, startup, fragmented);
        ReviewSingleSnapshotScenario(observed);
    }

    [TestMethod]
    [DataRow("{\"protocolVersion\":2,\"view\":\"health\"}", "UnsupportedVersion")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"Health\"}", "InvalidView")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"other\"}", "InvalidView")]
    [DataRow("{\"protocolVersion\":1}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":\"1\",\"view\":\"health\"}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"view\":\"details\"}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":{\"x\":1,\"x\":2}}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":[{\"x\":1,\"x\":2}]}", "InvalidRequest")]
    [DataRow("[]", "InvalidRequest")]
    [DataRow("null", "InvalidRequest")]
    [DataRow("{", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",}", "InvalidRequest")]
    public void InvalidRequestsReceiveBoundedErrorsAndNeverCapture(string request, string code)
    {
        InvalidRequestObservation observed = RunInvalidRequestScenario(request, code);
        ReviewInvalidRequestScenario(observed);
    }

    [TestMethod]
    [DataRow("zero")]
    [DataRow("oversize")]
    [DataRow("uint-max")]
    [DataRow("utf8")]
    public void InvalidFramesAreRejectedBeforeSnapshotAndDoNotPoisonTheEndpoint(string mode)
    {
        WireRecoveryObservation observed = RunInvalidFrameScenario(mode);
        ReviewWireRecoveryScenario(observed);
    }

    [TestMethod]
    public void UnknownFieldsStillRespectDepthLimitAndValidAdditionsAreIgnored()
    {
        ProtocolPairObservation observed = RunUnknownFieldsScenario();
        ReviewProtocolPairScenario(observed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetailsEnablementAndReportSizeAreEnforcedBeforeReply(bool detailsEnabled)
    {
        DetailsBoundsObservation observed = RunDetailsBoundsScenario(detailsEnabled);
        ReviewDetailsBoundsScenario(observed);
    }

    [TestMethod]
    public void SnapshotExceptionIsTransportErrorAndNextRequestCanSucceed()
    {
        SnapshotRecoveryObservation observed = RunSnapshotRecoveryScenario();
        ReviewSnapshotRecoveryScenario(observed);
    }

    [TestMethod]
    [DoNotParallelize]
    public void OneConnectionIsClosedAfterExactlyOneResponse()
    {
        SingleConnectionObservation observed = RunSingleConnectionScenario();
        ReviewSingleConnectionScenario(observed);
    }

    [TestMethod]
    [DoNotParallelize] // Preserve the 100ms budget without concurrent owner-review collections.
    public void SlowPartialFrameTimesOutAndCapacityIsReusable()
    {
        ConnectionRecoveryObservation observed = RunSlowPartialScenario();
        ReviewConnectionRecoveryScenario(observed);
    }

    [TestMethod]
    [DoNotParallelize]
    public void ManyConcurrentClientsSettleAndStopIsTerminal()
    {
        ConcurrentObservation observed = RunConcurrentScenario();
        ReviewConcurrentScenario(observed);
    }

    [TestMethod]
    [DoNotParallelize]
    public void StopWaitCancellationDoesNotCertifyBlockedHandlerSettlement()
    {
        IntegrationStopObservation observed = RunStopCancellationScenario();
        ReviewStopCancellationScenario(observed);
    }

    [TestMethod]
    [DoNotParallelize]
    public void DisposalWaitsForAnActuallyRunningSnapshotSource()
    {
        PendingDisposeObservation observed = RunPendingDisposeScenario();
        ReviewPendingDisposeScenario(observed);
    }

    [TestMethod]
    public async Task DisabledServerNeverOpensEndpoint()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = false, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe, enabled: false);
            using var cancellation = new CancellationTokenSource(100);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.ConnectAsync(cancellation.Token)); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public void EnabledConstructorAndCanceledStartAreInertBeforeAnExplicitSuccessfulStart()
    {
        CanceledStartObservation observed = RunCanceledStartScenario();
        ReviewCanceledStartScenario(observed);
    }

    [TestMethod]
    public void MaximumValidRequestLengthIsAccepted()
    {
        ProtocolPairObservation observed = RunMaximumRequestScenario();
        ReviewProtocolPairScenario(observed);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void RepeatedClientsClosingBeforeRequestDoNotStopTheServer(int capacity)
    {
        ConnectionRecoveryObservation observed = RunRepeatedCloseScenario(capacity);
        ReviewConnectionRecoveryScenario(observed);
    }

    [TestMethod]
    public void ServerBudgetIncludesRequestAndSynchronousSnapshotWhenTimerCallbackIsDelayed()
    {
        TotalBudgetObservation observed = RunTotalBudgetScenario();
        ReviewTotalBudgetScenario(observed);
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("options")]
    public async Task RequiredConstructorInputsAreRejectedBeforeOpeningAnEndpoint(string parameter)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            var options = new HealthcheckOptions { PipeEnabled = true, PipeName = TransportTestSupport.NewPipeName() };
            ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => new HealthPipeServer(
                parameter == "source" ? null! : service, parameter == "options" ? null! : options));
            Assert.AreEqual(parameter, error.ParamName);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public async Task NonpositiveClockFrequencyCannotCreateAReportingServer(long frequency)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new HealthPipeServer(service,
                new HealthcheckOptions(), new FrequencyClock(frequency)));
            Assert.AreEqual("timeProvider", error.ParamName);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task StoppingBeforeStartIsTerminalAndDisposalIsIdempotent()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            var server = new HealthPipeServer(service, new HealthcheckOptions());
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            await server.StopAsync().WaitAsync(TransportTestSupport.Limit);
            await server.StopAsync().WaitAsync(TransportTestSupport.Limit);
            Assert.ThrowsExactly<InvalidOperationException>(() => server.StartAsync());
            await server.DisposeAsync().AsTask().WaitAsync(TransportTestSupport.Limit);
            await server.DisposeAsync().AsTask().WaitAsync(TransportTestSupport.Limit);
            Assert.ThrowsExactly<ObjectDisposedException>(() => server.StartAsync());
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(999, true)]
    [DataRow(1000, false)]
    [DataRow(1001, false)]
    public void ATotalBudgetIsExpiredAtItsExactMonotonicBoundary(int elapsedMilliseconds, bool replyAllowed)
    {
        ExactBoundaryObservation observed = RunExactBoundaryScenario(elapsedMilliseconds, replyAllowed);
        ReviewExactBoundaryScenario(observed);
    }

    [TestMethod]
    [DataRow(1, 0)]
    [DataRow(3, 0)]
    [DataRow(4, 0)]
    [DataRow(4, 7)]
    public void TruncatedRequestHeaderOrBodyReleasesCapacityWithoutCapturing(int headerBytes, int bodyBytes)
    {
        WireRecoveryObservation observed = RunTruncatedFrameScenario(headerBytes, bodyBytes);
        ReviewWireRecoveryScenario(observed);
    }

    [TestMethod]
    [DataRow("cancellation")]
    [DataRow("format")]
    public void ASourceFailureCannotBeConfusedWithPeerFramingOrServerCancellation(string failure)
    {
        SourceFailureObservation observed = RunSourceFailureScenario(failure);
        ReviewSourceFailureScenario(observed);
    }

    [TestMethod]
    public void ACompletedRequestReleasesItsOwnedTimerBeforeServerStopSettles()
    {
        OwnedTimerObservation observed = RunOwnedTimerScenario();
        ReviewOwnedTimerScenario(observed);
    }

    [TestMethod]
    public void EndpointHasCurrentUserBoundaryOnTheExecutedPlatform()
    {
        CurrentUserObservation observed = RunCurrentUserScenario();
        ReviewCurrentUserScenario(observed);
    }

    private sealed class CountingSource(HealthcheckService service) : IHealthSnapshotSource
    {
        private int captures;
        internal bool Throw { get; set; }
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot() { Interlocked.Increment(ref captures); if (Throw) { throw new InvalidOperationException("secret-source-error"); } return service.CaptureSnapshot(); }
    }
    private sealed class BlockingSource(HealthcheckService service, TaskCompletionSource entered, ManualResetEventSlim release) : IHealthSnapshotSource
    {
        public HealthSnapshot CaptureSnapshot() { entered.TrySetResult(); release.Wait(TransportTestSupport.Limit); return service.CaptureSnapshot(); }
    }
    private sealed class FaultingOnceSource(HealthcheckService service, Exception failure) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot() { if (Interlocked.Increment(ref captures) == 1) { throw failure; } return service.CaptureSnapshot(); }
    }
    private sealed class FrequencyClock(long frequency) : TimeProvider
    {
        public override long TimestampFrequency => frequency;
    }
    private sealed class ObservingTimerClock : TimeProvider
    {
        private int created, disposed;
        internal int CreatedTimers => Volatile.Read(ref created);
        internal int DisposedTimers => Volatile.Read(ref disposed);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => 0;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref created);
            return new ObservedTimer(() => Interlocked.Increment(ref disposed));
        }
        private sealed class ObservedTimer(Action dispose) : ITimer
        {
            private int disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { dispose(); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class AdvancingSource(HealthcheckService service, DelayedCallbackClock clock, long firstCaptureAdvance = 600) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot() { if (Interlocked.Increment(ref captures) == 1) { clock.Advance(firstCaptureAdvance); } return service.CaptureSnapshot(); }
    }
    private sealed class DelayedCallbackClock : TimeProvider
    {
        private long timestamp;
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal void Advance(long milliseconds) => Interlocked.Add(ref timestamp, milliseconds);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerCreated.TrySetResult();
            return new DelayedTimer();
        }
        private sealed class DelayedTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
