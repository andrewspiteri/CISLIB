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
public sealed class HealthcheckPipeTransportTests
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
    public async Task AnActualRequestTimerCreationFailureClosesAdmissionWithoutReadingOrChangingHealth()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            var clock = new FailingTimerClock();
            var source = new CountingSource(service);
            var server = new HealthPipeServer(source,
                new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }, clock);
            Task? stopping = null, disposing = null;
            try
            {
                await TransportTestSupport.StartServerAsync(server, pipe);
                HealthSnapshot before = service.CaptureSnapshot();
                using var cancellation = new CancellationTokenSource(TransportTestSupport.Limit);
                await using (var peer = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await peer.ConnectAsync(cancellation.Token);
                    await clock.Attempted.Task.WaitAsync(TransportTestSupport.Limit);
                    try { Assert.AreEqual(0, await peer.ReadAsync(new byte[1], cancellation.Token), "A failed clock cannot manufacture a response."); }
                    catch (IOException) { /* The actual endpoint may close with a broken-pipe error. */ }
                }
                // Peer closure follows the worker's infrastructure-failure handling.
                // Inspect admission before a caller's Stop could conceal a missing stop.
                Assert.ThrowsExactly<InvalidOperationException>(() => server.StartAsync());
                Assert.AreEqual(1, clock.Attempts);
                Assert.AreEqual(0, source.Captures);
                HealthSnapshot after = service.CaptureSnapshot();
                Assert.AreEqual(before.Revision, after.Revision);
                Assert.AreEqual(before.HealthStatus, after.HealthStatus);
                Assert.AreEqual(before.Ready, after.Ready);
                Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
                stopping = server.StopAsync();
                await stopping.WaitAsync(TransportTestSupport.Limit);
                disposing = server.DisposeAsync().AsTask();
                await disposing.WaitAsync(TransportTestSupport.Limit);

                var replacement = new HealthPipeServer(service,
                    new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 });
                await using var replacementCleanup = new TransportTestSupport.PipeServerCleanup(replacement);
                await TransportTestSupport.StartServerAsync(replacement, pipe);
                TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe,
                    "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
                await replacement.StopAsync().WaitAsync(TransportTestSupport.Limit);
            }
            finally
            {
                await new TransportTestSupport.PipeServerCleanup(server).DisposeAsync();
            }
            GC.KeepAlive(monitor);
        }
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, DetailsEnabled = true });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            Assert.AreEqual(0, source.Captures);
            await TransportTestSupport.StartServerAsync(server, pipe);
            await TransportTestSupport.StartServerAsync(server, pipe);
            string revision = service.CaptureSnapshot().Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            JsonObject envelope = await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"" + view + "\"}", fragmented);
            JsonObject snapshot = TransportTestSupport.ValidateSnapshotEnvelope(envelope, view, !unhealthy, startup && !unhealthy);
            Assert.AreEqual(revision, (string?)snapshot["revision"]);
            Assert.AreEqual(1, source.Captures);
            Assert.AreEqual(revision, service.CaptureSnapshot().Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await server.StopAsync().WaitAsync(TransportTestSupport.Limit);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeRequestTimeoutMs = 100 });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, DetailsEnabled = detailsEnabled, MaxReportBytes = 1024 });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1, PipeRequestTimeoutMs = 100 });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
            // This case proves concurrent settlement rather than a one-second latency promise.
            // Dedicated deadline cases retain their short and exact-boundary budgets.
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 2, PipeRequestTimeoutMs = 5000 });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
            JsonObject[] replies = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}")));
            foreach (JsonObject reply in replies) { TransportTestSupport.ValidateSnapshotEnvelope(reply, "health", true, true); }
            Assert.AreEqual(24, source.Captures); await server.StopAsync().WaitAsync(TransportTestSupport.Limit); await server.StopAsync().WaitAsync(TransportTestSupport.Limit);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => server.StartAsync()).WaitAsync(TransportTestSupport.Limit); GC.KeepAlive(monitor);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
            Task<JsonObject> request = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
            try
            {
                await entered.Task.WaitAsync(TransportTestSupport.Limit);
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => server.StopAsync(canceled.Token)).WaitAsync(TransportTestSupport.Limit);
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
    public async Task DisposalWaitsForAnActuallyRunningSnapshotSource()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var server = new HealthPipeServer(new BlockingSource(service, entered, release),
                new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            Task<JsonObject>? request = null;
            Task? disposal = null;
            try
            {
                await TransportTestSupport.StartServerAsync(server, pipe);
                request = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
                await entered.Task.WaitAsync(TransportTestSupport.Limit);
                disposal = server.DisposeAsync().AsTask();
                Assert.IsFalse(disposal.IsCompleted, "Disposal must await the source that has entered and whose release gate is still closed.");
                release.Set();
                await disposal.WaitAsync(TransportTestSupport.Limit);
                await server.StopAsync().WaitAsync(TransportTestSupport.Limit);
                try { await request; Assert.Fail("A pending reply cannot succeed after disposal closes admission."); }
                catch (IOException) { }
                catch (OperationCanceledException) { }
            }
            finally
            {
                release.Set();
                Task serverDrain = new TransportTestSupport.PipeServerCleanup(server).DisposeAsync().AsTask();
                Task requestDrain = DrainRequestAsync();
                await Task.WhenAll(serverDrain, requestDrain).WaitAsync(TransportTestSupport.Limit);

                async Task DrainRequestAsync()
                {
                    if (request is not null)
                    {
                        try { await request.WaitAsync(TransportTestSupport.Limit); }
                        catch (Exception error) when (error is IOException or OperationCanceledException) { }
                    }
                    if (disposal is not null) { await disposal.WaitAsync(TransportTestSupport.Limit); }
                }
            }
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
            var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = false, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe, enabled: false);
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
            var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => server.StartAsync(canceled.Token)).WaitAsync(TransportTestSupport.Limit);
            using var timeout = new CancellationTokenSource(100);
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.ConnectAsync(timeout.Token));
            await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = capacity });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1, PipeRequestTimeoutMs = 1000 }, clock);
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
    public async Task ATotalBudgetIsExpiredAtItsExactMonotonicBoundary(int elapsedMilliseconds, bool replyAllowed)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var clock = new DelayedCallbackClock();
            var source = new AdvancingSource(service, clock, elapsedMilliseconds);
            var server = new HealthPipeServer(source, new HealthcheckOptions
            { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1, PipeRequestTimeoutMs = 1000 }, clock);
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            await TransportTestSupport.StartServerAsync(server, pipe);
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            byte[]? payload = null;
            await using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(timeout.Token);
                await clock.TimerCreated.Task.WaitAsync(TransportTestSupport.Limit);
                await TransportTestSupport.WriteFrameAsync(client, Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), cancellation: timeout.Token);
                try { payload = await TransportTestSupport.ReadFrameAsync(client, timeout.Token); }
                catch (IOException) { }
            }
            if (replyAllowed)
            {
                Assert.IsNotNull(payload, "A complete reply is permitted strictly before the total deadline.");
                TransportTestSupport.ValidateSnapshotEnvelope(JsonNode.Parse(payload)!.AsObject(), "health", true, true);
            }
            else { Assert.IsNull(payload, "A delayed timer callback cannot authorize a reply at or beyond the elapsed-time deadline."); }
            Assert.AreEqual(1, source.Captures);
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            Assert.AreEqual(2, source.Captures);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(1, 0)]
    [DataRow(3, 0)]
    [DataRow(4, 0)]
    [DataRow(4, 7)]
    public async Task TruncatedRequestHeaderOrBodyReleasesCapacityWithoutCapturing(int headerBytes, int bodyBytes)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var source = new CountingSource(service);
            var server = new HealthPipeServer(source, new HealthcheckOptions
            { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            await TransportTestSupport.StartServerAsync(server, pipe);
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
            await using (var abandoned = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await abandoned.ConnectAsync(timeout.Token);
                byte[] header = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, 40);
                await abandoned.WriteAsync(header.AsMemory(0, headerBytes), timeout.Token);
                if (bodyBytes > 0) { await abandoned.WriteAsync(Encoding.UTF8.GetBytes("{\"proto").AsMemory(0, bodyBytes), timeout.Token); }
                await abandoned.FlushAsync(timeout.Token);
            }
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            Assert.AreEqual(1, source.Captures, "Only the later complete valid request may read the source.");
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow("cancellation")]
    [DataRow("format")]
    public async Task ASourceFailureCannotBeConfusedWithPeerFramingOrServerCancellation(string failure)
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            Exception error = failure == "cancellation" ? new OperationCanceledException("private-source") : new FormatException("private-source");
            var source = new FaultingOnceSource(service, error);
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            await TransportTestSupport.StartServerAsync(server, pipe);
            TransportTestSupport.ValidateError(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "ReportingUnavailable");
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            Assert.AreEqual(2, source.Captures);
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task ACompletedRequestReleasesItsOwnedTimerBeforeServerStopSettles()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName(); var clock = new ObservingTimerClock();
            var server = new HealthPipeServer(service, new HealthcheckOptions
            { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }, clock);
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server);
            await TransportTestSupport.StartServerAsync(server, pipe);
            TransportTestSupport.ValidateSnapshotEnvelope(await TransportTestSupport.RequestAsync(pipe,
                "{\"protocolVersion\":1,\"view\":\"health\"}"), "health", true, true);
            await server.StopAsync().WaitAsync(TransportTestSupport.Limit);
            Assert.AreEqual(1, clock.CreatedTimers);
            Assert.AreEqual(1, clock.DisposedTimers, "A settled request must release its actual TimeProvider timer before stopping completes.");
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task EndpointHasCurrentUserBoundaryOnTheExecutedPlatform()
    {
        var (service, monitor) = Create();
        await using (service)
        {
            string pipe = TransportTestSupport.NewPipeName();
            var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
            await using var serverCleanup = new TransportTestSupport.PipeServerCleanup(server); await TransportTestSupport.StartServerAsync(server, pipe);
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
                // Unix CurrentUserOnly checks peer UID on accept. Socket mode follows
                // the process umask; it is not promised to be 0600. Different-UID
                // rejection is exercised separately against real packaged processes.
                string endpoint = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipe);
                UnixFileMode mode = File.GetUnixFileMode(endpoint);
                const UnixFileMode ownerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                Assert.AreEqual(ownerReadWrite, mode & ownerReadWrite);
                Console.WriteLine($"Actual Unix pipe endpoint mode: {mode}; current-owner connection succeeded.");
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
    private sealed class FailingTimerClock : TimeProvider
    {
        private int attempts;
        internal int Attempts => Volatile.Read(ref attempts);
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref attempts);
            Attempted.TrySetResult();
            throw new InvalidOperationException("actual-test-clock-timer-failure");
        }
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
