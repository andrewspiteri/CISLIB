using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

[TestClass]
public sealed class HealthcheckPipeTransportTests
{
    private static (HealthcheckService Service, IConditionMonitor Monitor) Create(bool unhealthy = false, bool startup = true)
    {
        var service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "pipe-fixture", InstanceId = Guid.NewGuid() });
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = unhealthy, Name = "critical" });
        if (startup) { service.MarkStartupCompleted(); }
        return (service, monitor);
    }

    [TestMethod]
    [DataRow("health", false, true, false)]
    [DataRow("health", true, true, true)]
    [DataRow("readiness", false, false, true)]
    [DataRow("readiness", false, true, false)]
    [DataRow("details", false, true, true)]
    [DataRow("details", true, true, false)]
    public async Task ActualLocalServerUsesCompleteSingleSnapshotAndFragmentedLittleEndianFrames(string view, bool unhealthy, bool startup, bool fragmented)
    {
        var (service, monitor) = Create(unhealthy, startup);
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            var source = new CountingSource(service);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, DetailsEnabled = true });
            Assert.AreEqual(0, source.Captures);
            await server.StartAsync();
            await server.StartAsync();
            string revision = service.CaptureSnapshot().Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            JsonObject envelope = await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"" + view + "\"}", fragmented);
            JsonObject snapshot = TransportTestSupport.ValidateSnapshotEnvelope(envelope, view, !unhealthy, startup && !unhealthy);
            Assert.AreEqual(revision, (string?)snapshot["revision"]);
            Assert.AreEqual(1, source.Captures);
            Assert.AreEqual(revision, service.CaptureSnapshot().Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await server.StopAsync();
            GC.KeepAlive(monitor);
        }
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
    public async Task InvalidRequestsReceiveBoundedErrorsAndNeverCapture(string request, string code)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await server.StartAsync();
            JsonObject response = await TransportTestSupport.RequestAsync(pipe, request, fragmented: true);
            TransportTestSupport.ValidateError(response, code);
            Assert.AreEqual(1, (int?)response["protocolVersion"]);
            Assert.AreEqual(1, (int?)response["schemaVersion"]);
            Assert.IsNull(response["view"]);
            Assert.AreEqual(0, source.Captures);
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow("zero")]
    [DataRow("oversize")]
    [DataRow("uint-max")]
    [DataRow("utf8")]
    public async Task InvalidFramesAreRejectedBeforeSnapshotAndDoNotPoisonTheEndpoint(string mode)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeRequestTimeoutMs = 100 });
            await server.StartAsync();
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeout.Token);
            byte[] payload = mode == "utf8" ? [0xff, 0xfe] : [];
            uint? announced = mode switch { "zero" => 0, "oversize" => 513, "uint-max" => uint.MaxValue, _ => null };
            await TransportTestSupport.WriteFrameAsync(client, payload, announcedLength: announced, cancellation: timeout.Token);
            try
            {
                JsonObject error = JsonNode.Parse(await TransportTestSupport.ReadFrameAsync(client, timeout.Token))!.AsObject();
                TransportTestSupport.ValidateError(error, "InvalidFrame");
            }
            catch (EndOfStreamException) { /* Damaged framing may terminate the local connection. */ }
            catch (IOException) { /* A closed pipe still fails the invalid exchange. */ }
            Assert.AreEqual(0, source.Captures);
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task UnknownFieldsStillRespectDepthLimitAndValidAdditionsAreIgnored()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }); await server.StartAsync();
            string nested = "0";
            for (int i = 0; i < 16; i++) { nested = "{\"x\":" + nested + "}"; }
            TransportTestSupport.ValidateError(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":" + nested + "}"), "InvalidRequest");
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\",\"future\":{\"v\":[true,12]}}"), "health", true, true);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DetailsEnablementAndReportSizeAreEnforcedBeforeReply(bool detailsEnabled)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            var extra = new List<IConditionMonitor>();
            for (int i = 0; i < 30; i++) { extra.Add(service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false, Name = new string('x', 128) })); }
            string pipe = TransportTestSupport.NewPipeName();
            await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, DetailsEnabled = detailsEnabled, MaxReportBytes = 1024 }); await server.StartAsync();
            TransportTestSupport.ValidateError(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"details\"}"), detailsEnabled ? "ReportTooLarge" : "DetailsDisabled");
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true, 31);
            GC.KeepAlive(extra); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task SnapshotExceptionIsTransportErrorAndNextRequestCanSucceed()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service) { Throw = true };
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }); await server.StartAsync();
            TransportTestSupport.ValidateError(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "ReportingUnavailable");
            source.Throw = false;
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            Assert.AreEqual(2, source.Captures); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task OneConnectionIsClosedAfterExactlyOneResponse()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }); await server.StartAsync();
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous); await client.ConnectAsync(timeout.Token);
            await TransportTestSupport.WriteFrameAsync(client, Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), cancellation: timeout.Token);
            TransportTestSupport.ValidateSnapshotEnvelope(JsonNode.Parse(await TransportTestSupport.ReadFrameAsync(client, timeout.Token))!.AsObject(), "health", true, true);
            Assert.AreEqual(0, await client.ReadAsync(new byte[1], timeout.Token)); Assert.AreEqual(1, source.Captures); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task SlowPartialFrameTimesOutAndCapacityIsReusable()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1, PipeRequestTimeoutMs = 100 }); await server.StartAsync();
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous); await client.ConnectAsync(timeout.Token);
            await client.WriteAsync(new byte[] { 32 }, timeout.Token); await client.FlushAsync(timeout.Token);
            try { Assert.AreEqual(0, await client.ReadAsync(new byte[4], timeout.Token)); } catch (IOException) { }
            Assert.AreEqual(0, source.Captures);
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task ManyConcurrentClientsSettleAndStopIsTerminal()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 2, PipeRequestTimeoutMs = 1000 }); await server.StartAsync();
            JsonObject[] replies = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}")));
            foreach (JsonObject reply in replies) { TransportTestSupport.ValidateSnapshotEnvelope(reply, "health", true, true); }
            Assert.AreEqual(24, source.Captures); await server.StopAsync(); await server.StopAsync();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => server.StartAsync()); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task StopWaitCancellationDoesNotCertifyBlockedHandlerSettlement()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new BlockingSource(service, entered, release);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }); await server.StartAsync();
            Task<JsonObject> request = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
            try
            {
                await entered.Task.WaitAsync(TransportTestSupport.Limit);
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => server.StopAsync(canceled.Token));
                Task actualStop = server.StopAsync(); Assert.IsFalse(actualStop.IsCompleted, "Stop must include actual source settlement.");
                release.Set(); await actualStop.WaitAsync(TransportTestSupport.Limit);
                try { await request; Assert.Fail("Closed stop admission must not produce a successful pending reply."); }
                catch (IOException) { }
                catch (OperationCanceledException) { }
            }
            finally { release.Set(); try { await request; } catch (Exception error) when (error is IOException or OperationCanceledException) { } }
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task DisabledServerNeverOpensEndpoint()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = false, PipeName = pipe }); await server.StartAsync();
            using var cancellation = new CancellationTokenSource(100);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.ConnectAsync(cancellation.Token)); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task EnabledConstructorAndCanceledStartAreInertBeforeAnExplicitSuccessfulStart()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => server.StartAsync(canceled.Token));
            using var timeout = new CancellationTokenSource(100);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.ConnectAsync(timeout.Token));
            await server.StartAsync();
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task MaximumValidRequestLengthIsAccepted()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }); await server.StartAsync();
            const string request = "{\"protocolVersion\":1,\"view\":\"health\"}";
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, request.PadRight(512)), "health", true, true);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task RepeatedClientsClosingBeforeRequestDoNotStopTheServer(int capacity)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = capacity }); await server.StartAsync();
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            for (int i = 0; i < 32; i++)
            {
                await using var abandoned = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                await abandoned.ConnectAsync(timeout.Token);
            }
            Assert.AreEqual(0, source.Captures);
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            Assert.AreEqual(1, source.Captures); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task ServerBudgetIncludesRequestAndSynchronousSnapshotWhenTimerCallbackIsDelayed()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var clock = new DelayedCallbackClock();
            var source = new AdvancingSource(service, clock);
            await using var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1, PipeRequestTimeoutMs = 1000 }, clock); await server.StartAsync();
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            await using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(timeout.Token);
                await clock.TimerCreated.Task.WaitAsync(TransportTestSupport.Limit);
                // 600ms belongs to request reception; the source consumes another 600ms.
                // The timer deliberately cannot fire, requiring an elapsed-time deadline check.
                clock.Advance(600);
                await TransportTestSupport.WriteFrameAsync(client, Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), cancellation: timeout.Token);
                try { await TransportTestSupport.ReadFrameAsync(client, timeout.Token); Assert.Fail("An expired total budget cannot commit any late complete reply."); }
                catch (IOException) { }
            }
            Assert.AreEqual(1, source.Captures);
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            Assert.AreEqual(2, source.Captures); GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task EndpointHasCurrentUserBoundaryOnTheExecutedPlatform()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }); await server.StartAsync();
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous); await client.ConnectAsync(timeout.Token);
            if (OperatingSystem.IsWindows())
            {
                PipeSecurity security = client.GetAccessControl();
                var owner = (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!;
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                Assert.AreEqual(identity.User, owner);
                Assert.IsTrue(security.AreAccessRulesProtected);
                AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
                Assert.IsTrue(rules.Count > 0);
                foreach (PipeAccessRule rule in rules) { Assert.AreEqual(owner, rule.IdentityReference); Assert.AreEqual(AccessControlType.Allow, rule.AccessControlType); }
            }
            else
            {
                // On Unix the BCL endpoint is a local Unix-domain socket; inspect its actual mode.
                string endpoint = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipe);
                UnixFileMode mode = File.GetUnixFileMode(endpoint);
                Assert.AreEqual((UnixFileMode)0, mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
            }
            GC.KeepAlive(monitor);
        }
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
    private sealed class AdvancingSource(HealthcheckService service, DelayedCallbackClock clock) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot() { if (Interlocked.Increment(ref captures) == 1) { clock.Advance(600); } return service.CaptureSnapshot(); }
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
