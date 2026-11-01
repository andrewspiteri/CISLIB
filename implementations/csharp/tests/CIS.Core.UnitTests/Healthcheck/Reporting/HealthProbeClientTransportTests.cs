using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
[TestCategory("ComponentIntegration")]
public sealed class HealthProbeClientTransportTests
{
    private sealed class DirectProbe : IAsyncDisposable
    {
        private readonly MemoryStream output = new();
        private readonly StringWriter error = new();
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task<int> execution;

        internal DirectProbe(string[] arguments)
            => execution = HealthProbeClient.RunAsync(arguments, output, error, cancellation.Token);

        internal DirectProbe(string[] arguments, TimeProvider clock,
            Func<byte[], HealthReportView, int, HealthParsedResponse> responseParser)
            => execution = HealthProbeClient.RunAsync(arguments, output, error, clock, responseParser, cancellation.Token);

        internal async Task<(int Exit, string Output, string Error)> Finish()
        {
            int exit = await execution.WaitAsync(Transport.Limit);
            Assert.IsTrue(output.CanWrite, "The shipping client must leave its caller-owned output open.");
            error.Write("");
            string stdout = new UTF8Encoding(false, true).GetString(output.ToArray());
            Assert.IsFalse(stdout.StartsWith('\uFEFF'));
            return (exit, stdout, error.ToString());
        }

        internal async Task WaitForConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            using var fixtureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task connecting = server.WaitForConnectionAsync(fixtureCancellation.Token);
            try
            {
                await Task.WhenAny(connecting, execution).WaitAsync(Transport.Limit);
                if (!connecting.IsCompleted)
                {
                    var result = await Finish();
                    if (result.Error.Contains("TimeoutOrCanceled", StringComparison.Ordinal))
                    {
                        throw new TimeoutException("The actual probe expired before the fixture connection completed.");
                    }
                    Assert.Fail("Valid probe arguments must connect before reporting a result; actual exit=" + result.Exit + ", stderr=" + result.Error);
                }
                await connecting;
            }
            finally
            {
                if (!connecting.IsCompleted)
                {
                    fixtureCancellation.Cancel();
                    try { await connecting.WaitAsync(Transport.Limit); }
                    catch (OperationCanceledException) when (connecting.IsCanceled && !cancellationToken.IsCancellationRequested) { }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!execution.IsCompleted) { cancellation.Cancel(); }
                await execution.WaitAsync(Transport.Limit);
            }
            finally { cancellation.Dispose(); output.Dispose(); error.Dispose(); }
        }
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("--timeout-ms", "60000")]
    [DataRow("--max-report-bytes", "16777216")]
    public async Task DefaultViewAndInclusiveUpperLimitsSendTheCompleteHealthRequest(string option, string value)
    {
        string pipe = Transport.NewPipeName();
        await using var server = NewRawServer(pipe);
        string[] arguments = option.Length == 0 ? ["--pipe", pipe] : ["--pipe", pipe, option, value];
        await using var probe = new DirectProbe(arguments);
        using var timeout = new CancellationTokenSource(Transport.Limit);
        await probe.WaitForConnectionAsync(server, timeout.Token);
        Assert.AreEqual("{\"protocolVersion\":1,\"view\":\"health\"}", Encoding.UTF8.GetString(await Transport.ReadFrameAsync(server, timeout.Token)));
        string response = Response("healthy");
        await Transport.WriteFrameAsync(server, Encoding.UTF8.GetBytes(response), cancellation: timeout.Token);
        var result = await probe.Finish();
        Assert.AreEqual(0, result.Exit, result.Error);
        Assert.AreEqual(JsonNode.Parse(response)!["snapshot"]!.ToJsonString() + "\n", result.Output);
        Assert.AreEqual("", result.Error);
    }

    private static class Transport
    {
        internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
        internal static string NewPipeName() => "cis-direct-probe-" + Guid.NewGuid().ToString("N");

        internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellation)
        {
            byte[] header = new byte[4];
            await stream.ReadExactlyAsync(header, cancellation);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header);
            Assert.IsTrue(size is > 0 and <= 16777216);
            byte[] payload = new byte[checked((int)size)];
            await stream.ReadExactlyAsync(payload, cancellation);
            return payload;
        }

        internal static async Task WriteFrameAsync(Stream stream, byte[] payload, bool fragmented = false,
            uint? announcedLength = null, CancellationToken cancellation = default)
        {
            byte[] header = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(header, announcedLength ?? checked((uint)payload.Length));
            if (fragmented)
            {
                foreach (byte value in header.Concat(payload))
                {
                    await stream.WriteAsync(new[] { value }, cancellation);
                    await stream.FlushAsync(cancellation);
                }
            }
            else
            {
                await stream.WriteAsync(header, cancellation);
                await stream.WriteAsync(payload, cancellation);
                await stream.FlushAsync(cancellation);
            }
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("--unknown x")]
    [DataRow("--pipe")]
    [DataRow("--pipe anonymous")]
    [DataRow("--pipe bad/name")]
    [DataRow("--pipe -bad")]
    [DataRow("--pipe valid --pipe other")]
    [DataRow("--pipe valid --view health --view health")]
    [DataRow("--pipe valid --view Health")]
    [DataRow("--pipe valid --view invalid")]
    [DataRow("--pipe valid --timeout-ms 49")]
    [DataRow("--pipe valid --timeout-ms 60001")]
    [DataRow("--pipe valid --timeout-ms 100 --timeout-ms 100")]
    [DataRow("--pipe valid --timeout-ms nope")]
    [DataRow("--pipe valid --timeout-ms 1.5")]
    [DataRow("--pipe valid --max-report-bytes 1023")]
    [DataRow("--pipe valid --max-report-bytes 16777217")]
    [DataRow("--pipe valid --max-report-bytes 1024 --max-report-bytes 1024")]
    [DataRow("--pipe valid --max-report-bytes 9999999999999999999999999")]
    [DataRow("--pipe valid unexpected")]
    public async Task InvalidArgumentsNeverPrintSnapshotOrReturnHealthSuccess(string arguments)
    {
        await using var probe = new DirectProbe(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        ErrorResult(await probe.Finish(), "InvalidArguments");
    }

    [TestMethod]
    public async Task MissingLocalEndpointIsExit2WithoutRetryOrSnapshot()
    {
        await using var probe = new DirectProbe(["--pipe", Transport.NewPipeName(), "--timeout-ms", "50"]);
        ErrorResult(await probe.Finish(), "TimeoutOrCanceled");
    }

    [TestMethod]
    [DataRow("healthy", 0)]
    [DataRow("unhealthy", 1)]
    [DataRow("not-ready", 1)]
    [DataRow("huge-revision", 0)]
    [DataRow("additive", 0)]
    [DataRow("wrong-view", 2)]
    [DataRow("protocol-version", 2)]
    [DataRow("schema-version", 2)]
    [DataRow("negative-count", 2)]
    [DataRow("inconsistent-count", 2)]
    [DataRow("inconsistent-ready", 2)]
    [DataRow("leading-zero-revision", 2)]
    [DataRow("numeric-revision", 2)]
    [DataRow("wrong-type", 2)]
    [DataRow("duplicate-root", 2)]
    [DataRow("duplicate-unknown", 2)]
    [DataRow("excess-depth", 2)]
    [DataRow("error", 2)]
    [DataRow("missing-snapshot", 2)]
    [DataRow("both-branches", 2)]
    public async Task ShippingProbeClientIndependentlyValidatesFragmentedWireResponses(string mutation, int expectedExit)
    {
        string pipe = Transport.NewPipeName();
        await using var rawServer = NewRawServer(pipe);
        string view = mutation == "not-ready" ? "readiness" : "health";
        await using var probe = new DirectProbe(["--pipe", pipe, "--view", view, "--timeout-ms", "3000"]);
        using var timeout = new CancellationTokenSource(Transport.Limit);
        await probe.WaitForConnectionAsync(rawServer, timeout.Token);
        JsonObject request = JsonNode.Parse(await Transport.ReadFrameAsync(rawServer, timeout.Token))!.AsObject();
        Assert.AreEqual(1, (int?)request["protocolVersion"]); Assert.AreEqual(view, (string?)request["view"]);
        string response = Response(mutation);
        await Transport.WriteFrameAsync(rawServer, Encoding.UTF8.GetBytes(response), fragmented: true, cancellation: timeout.Token);
        var result = await probe.Finish(); Assert.AreEqual(expectedExit, result.Exit, result.Error);
        if (expectedExit == 2) { ErrorResult(result, mutation == "error" ? "ReportingUnavailable" : "TransportOrProtocolFailure"); }
        else
        {
            JsonObject actual = JsonNode.Parse(result.Output)!.AsObject();
            Assert.AreEqual(mutation == "unhealthy" ? "Unhealthy" : "Healthy", (string?)actual["healthStatus"]);
            Assert.AreEqual(JsonNode.Parse(response)!["snapshot"]!.ToJsonString() + "\n", result.Output,
                "Stdout is exactly the received complete snapshot plus one newline, without its transport envelope.");
            Assert.AreEqual("", result.Error);
        }
    }

    [TestMethod]
    [DataRow("zero")]
    [DataRow("oversize")]
    [DataRow("truncated")]
    [DataRow("utf8")]
    [DataRow("missing-header")]
    [DataRow("partial-header-1")]
    [DataRow("partial-header-3")]
    public async Task InvalidResponseFramesCannotReachStdoutOrHealthExit(string mode)
    {
        string pipe = Transport.NewPipeName();
        await using var rawServer = NewRawServer(pipe);
        await using var probe = new DirectProbe(["--pipe", pipe, "--timeout-ms", "1000", "--max-report-bytes", "1024"]);
        using var timeout = new CancellationTokenSource(Transport.Limit);
        await probe.WaitForConnectionAsync(rawServer, timeout.Token); await Transport.ReadFrameAsync(rawServer, timeout.Token);
        if (mode is "missing-header" or "partial-header-1" or "partial-header-3")
        {
            int count = mode == "missing-header" ? 0 : mode == "partial-header-1" ? 1 : 3;
            if (count > 0) { await rawServer.WriteAsync(new byte[count], timeout.Token); await rawServer.FlushAsync(timeout.Token); }
            rawServer.Disconnect();
        }
        else
        {
            byte[] payload = mode == "utf8" ? [0xff] : [];
            uint? size = mode switch { "zero" => 0, "oversize" => uint.MaxValue, "truncated" => 100, _ => null };
            await Transport.WriteFrameAsync(rawServer, payload, announcedLength: size, cancellation: timeout.Token);
            if (mode == "truncated") { rawServer.Disconnect(); }
        }
        ErrorResult(await probe.Finish(), "TransportOrProtocolFailure");
    }

    [TestMethod]
    public async Task ResponseHeaderAndBodyShareOneTotalMonotonicTimeoutBudget()
    {
        string pipe = Transport.NewPipeName();
        await using var rawServer = NewRawServer(pipe);
        await using var probe = new DirectProbe(["--pipe", pipe, "--timeout-ms", "800"]);
        using var timeout = new CancellationTokenSource(Transport.Limit);
        await probe.WaitForConnectionAsync(rawServer, timeout.Token); await Transport.ReadFrameAsync(rawServer, timeout.Token);
        byte[] payload = Encoding.UTF8.GetBytes(Response("healthy"));
        await Task.Delay(500, timeout.Token);
        byte[] header = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)payload.Length));
        await rawServer.WriteAsync(header, timeout.Token); await rawServer.FlushAsync(timeout.Token);
        await Task.Delay(500, timeout.Token);
        try { await rawServer.WriteAsync(payload, timeout.Token); await rawServer.FlushAsync(timeout.Token); } catch (IOException) { }
        var result = await probe.Finish(); Assert.AreEqual(2, result.Exit, "A fresh per-read timeout would incorrectly accept the delayed complete response."); Assert.AreEqual("", result.Output);
        ErrorResult(result, "TimeoutOrCanceled");
    }

    [TestMethod]
    public async Task ResponseValidationMustFinishWithinTheOriginalMonotonicBudget()
    {
        string pipe = Transport.NewPipeName();
        await using var server = NewRawServer(pipe);
        var clock = new DelayedTimerClock();
        byte[] payload = Encoding.UTF8.GetBytes(Response("healthy"));
        int parserCalls = 0;

        HealthParsedResponse ParseAndAdvance(byte[] received, HealthReportView view, int maximum)
        {
            CollectionAssert.AreEqual(payload, received);
            HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(received, view, maximum);
            Assert.IsFalse(parsed.IsError);
            Assert.IsTrue(parsed.SelectedHealthy);
            Interlocked.Increment(ref parserCalls);
            // Advance after real framing and validation, independently of every budget-check call.
            // The timer remains delayed; only the final monotonic check can reject this late result.
            clock.Timestamp = 51;
            return parsed;
        }

        await using var probe = new DirectProbe(["--pipe", pipe, "--timeout-ms", "50"], clock, ParseAndAdvance);
        using var timeout = new CancellationTokenSource(Transport.Limit);
        await probe.WaitForConnectionAsync(server, timeout.Token);
        Assert.AreEqual("{\"protocolVersion\":1,\"view\":\"health\"}",
            Encoding.UTF8.GetString(await Transport.ReadFrameAsync(server, timeout.Token)));
        await Transport.WriteFrameAsync(server, payload, cancellation: timeout.Token);

        ErrorResult(await probe.Finish(), "TimeoutOrCanceled");
        Assert.AreEqual(1, Volatile.Read(ref parserCalls));
        Assert.AreEqual(51L, clock.Timestamp);
    }

    // The same delayed-timer clock shape used by the existing deadline qualification:
    // advancing monotonic time deliberately does not dispatch the cancellation callback.
    private sealed class DelayedTimerClock : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new DelayedTimer();

        private sealed class DelayedTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    [DataRow(1023, 0)]
    [DataRow(1024, 0)]
    [DataRow(1025, 2)]
    public async Task CompleteResponsePayloadRespectsTheExactConfiguredByteLimit(int payloadBytes, int expectedExit)
    {
        string pipe = Transport.NewPipeName();
        await using var rawServer = NewRawServer(pipe);
        await using var probe = new DirectProbe(["--pipe", pipe, "--timeout-ms", "3000", "--max-report-bytes", "1024"]);
        using var timeout = new CancellationTokenSource(Transport.Limit);
        await probe.WaitForConnectionAsync(rawServer, timeout.Token);
        await Transport.ReadFrameAsync(rawServer, timeout.Token);
        string response = Response("healthy").PadRight(payloadBytes);
        byte[] payload = Encoding.UTF8.GetBytes(response);
        Assert.AreEqual(payloadBytes, payload.Length);
        try { await Transport.WriteFrameAsync(rawServer, payload, cancellation: timeout.Token); }
        catch (IOException) when (expectedExit == 2) { }
        var result = await probe.Finish();
        Assert.AreEqual(expectedExit, result.Exit, result.Error);
        if (expectedExit == 2) { ErrorResult(result, "TransportOrProtocolFailure"); }
        else
        {
            Assert.AreEqual(JsonNode.Parse(response)!["snapshot"]!.ToJsonString() + "\n", result.Output);
            Assert.AreEqual("", result.Error);
        }
    }

    private static void ErrorResult((int Exit, string Output, string Error) result, string code)
    {
        Assert.AreEqual(2, result.Exit);
        Assert.AreEqual("", result.Output);
        Assert.IsTrue(result.Error.Length <= 1024);
        string reason = result.Error.Trim();
        const string currentProgramLabel = "cis-health-probe:";
        if (reason.StartsWith(currentProgramLabel, StringComparison.Ordinal)) { reason = reason[currentProgramLabel.Length..].Trim(); }
        Assert.IsFalse(string.IsNullOrWhiteSpace(reason), "The failure must have a reason beyond an optional program label: " + code);
        StringAssert.Contains(reason, code);
        Assert.IsFalse(result.Error.Contains("private-server-detail", StringComparison.Ordinal), "Untrusted remote error text must not be copied to stderr.");
    }

    private static NamedPipeServerStream NewRawServer(string pipe) => new(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static string Response(string mutation)
    {
        // This independent DTO is deliberately not produced by HealthReportFormatter.
        JsonObject snapshot = new()
        {
            ["schemaVersion"] = 1,
            ["revision"] = "42",
            ["capturedAtUtc"] = "2026-10-21T08:00:00.0000000Z",
            ["application"] = new JsonObject { ["name"] = "independent-probe-oracle", ["instanceId"] = "10000000-0000-0000-0000-000000000001", ["startupCompleted"] = true, ["isStopping"] = false },
            ["healthStatus"] = "Healthy",
            ["ready"] = true,
            ["monitorCounts"] = new JsonObject { ["active"] = 1, ["critical"] = 1, ["failing"] = 0, ["criticalFailing"] = 0, ["pending"] = 0 },
        };
        JsonObject envelope = new() { ["protocolVersion"] = 1, ["schemaVersion"] = 1, ["view"] = mutation == "not-ready" ? "readiness" : "health", ["kind"] = "snapshot", ["snapshot"] = snapshot };
        switch (mutation)
        {
            case "healthy": break;
            case "unhealthy": snapshot["healthStatus"] = "Unhealthy"; snapshot["ready"] = false; snapshot["monitorCounts"]!["failing"] = 1; snapshot["monitorCounts"]!["criticalFailing"] = 1; break;
            case "not-ready": snapshot["ready"] = false; snapshot["application"]!["startupCompleted"] = false; break;
            case "huge-revision": snapshot["revision"] = new string('9', 120); break;
            case "additive": envelope["future"] = new JsonObject { ["values"] = new JsonArray(true, 1, "string") }; break;
            case "wrong-view": envelope["view"] = "readiness"; break;
            case "protocol-version": envelope["protocolVersion"] = 2; break;
            case "schema-version": envelope["schemaVersion"] = 2; break;
            case "negative-count": snapshot["monitorCounts"]!["active"] = -1; break;
            case "inconsistent-count": snapshot["monitorCounts"]!["criticalFailing"] = 2; break;
            case "inconsistent-ready": snapshot["ready"] = false; break;
            case "leading-zero-revision": snapshot["revision"] = "042"; break;
            case "numeric-revision": snapshot["revision"] = 42; break;
            case "wrong-type": snapshot["ready"] = "true"; break;
            case "duplicate-root": return envelope.ToJsonString().Insert(1, "\"protocolVersion\":1,");
            case "duplicate-unknown": return envelope.ToJsonString().Insert(1, "\"future\":{\"x\":1,\"x\":2},");
            case "excess-depth": JsonNode node = new JsonObject(); for (int i = 0; i < 17; i++) { node = new JsonObject { ["x"] = node }; } envelope["future"] = node; break;
            case "error": envelope.Remove("snapshot"); envelope["kind"] = "error"; envelope["error"] = new JsonObject { ["code"] = "ReportingUnavailable", ["message"] = "private-server-detail" }; break;
            case "missing-snapshot": envelope.Remove("snapshot"); break;
            case "both-branches": envelope["error"] = new JsonObject { ["code"] = "ReportingUnavailable" }; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return envelope.ToJsonString();
    }

}
