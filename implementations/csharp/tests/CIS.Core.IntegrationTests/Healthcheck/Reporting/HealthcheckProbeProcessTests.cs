using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

[TestClass]
public sealed class HealthcheckProbeProcessTests
{
    private sealed class Probe : IAsyncDisposable
    {
        private readonly Process child;
        private readonly Task<string> stdout, stderr;
        internal Probe(IEnumerable<string> arguments)
        {
            child = Process.Start(TransportTestSupport.StartInfo("healthcheck-probe", "CisHealthProbe", arguments))!;
            stdout = child.StandardOutput.ReadToEndAsync(); stderr = child.StandardError.ReadToEndAsync();
        }
        internal async Task<(int Exit, string Output, string Error)> Finish()
        {
            await child.WaitForExitAsync().WaitAsync(TransportTestSupport.Limit);
            string output = await stdout, error = await stderr;
            Assert.IsTrue(error.Length <= 1024, "CLI error text remains bounded.");
            Assert.IsFalse(output.StartsWith('\uFEFF'));
            return (child.ExitCode, output, error);
        }
        public async ValueTask DisposeAsync() { try { if (!child.HasExited) { child.Kill(entireProcessTree: true); } await child.WaitForExitAsync().WaitAsync(TransportTestSupport.Limit); } finally { child.Dispose(); } }
    }

    [TestMethod]
    [DataRow("health", false, false, true, 0)]
    [DataRow("health", false, true, true, 0)]
    [DataRow("health", true, true, true, 1)]
    [DataRow("readiness", false, false, true, 1)]
    [DataRow("readiness", false, true, true, 0)]
    [DataRow("readiness", true, true, true, 1)]
    [DataRow("details", false, true, true, 0)]
    [DataRow("details", true, true, true, 1)]
    [DataRow("details", false, true, false, 2)]
    public async Task ActualProbeProcessExitsExactlyForSelectedCompleteView(string view, bool unhealthy, bool startup, bool details, int exit)
    {
        string pipe = TransportTestSupport.NewPipeName();
        var options = new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, DetailsEnabled = details };
        await using var service = new HealthcheckService(options, new HealthApplicationIdentity { Name = "probe-fixture", InstanceId = Guid.NewGuid() });
        await using IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = unhealthy });
        if (startup) { service.MarkStartupCompleted(); }
        await using var server = new HealthPipeServer(service, options); await server.StartAsync();
        await using var probe = new Probe(["--pipe", pipe, "--view", view, "--timeout-ms", "3000"]);
        var result = await probe.Finish(); Assert.AreEqual(exit, result.Exit, result.Error);
        if (exit == 2) { Assert.AreEqual("", result.Output); Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error)); }
        else { TransportTestSupport.ValidateSnapshot(JsonNode.Parse(result.Output)!.AsObject(), view, !unhealthy, startup && !unhealthy); Assert.AreEqual("", result.Error); }
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
    public async Task InvalidCliNeverPrintsSnapshotOrReturnsHealthSuccess(string arguments)
    {
        await using var probe = new Probe(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var result = await probe.Finish(); Assert.AreEqual(2, result.Exit); Assert.AreEqual("", result.Output); Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task MissingLocalEndpointIsExit2WithoutRetryOrSnapshot()
    {
        await using var probe = new Probe(["--pipe", TransportTestSupport.NewPipeName(), "--timeout-ms", "50"]);
        var result = await probe.Finish(); Assert.AreEqual(2, result.Exit); Assert.AreEqual("", result.Output); Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
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
    public async Task RealProbeIndependentlyValidatesFragmentedWireResponses(string mutation, int expectedExit)
    {
        string pipe = TransportTestSupport.NewPipeName();
        await using var rawServer = NewRawServer(pipe);
        string view = mutation == "not-ready" ? "readiness" : "health";
        await using var probe = new Probe(["--pipe", pipe, "--view", view, "--timeout-ms", "3000"]);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        await rawServer.WaitForConnectionAsync(timeout.Token);
        JsonObject request = JsonNode.Parse(await TransportTestSupport.ReadFrameAsync(rawServer, timeout.Token))!.AsObject();
        Assert.AreEqual(1, (int?)request["protocolVersion"]); Assert.AreEqual(view, (string?)request["view"]);
        string response = Response(mutation);
        await TransportTestSupport.WriteFrameAsync(rawServer, Encoding.UTF8.GetBytes(response), fragmented: true, cancellation: timeout.Token);
        var result = await probe.Finish(); Assert.AreEqual(expectedExit, result.Exit, result.Error);
        if (expectedExit == 2) { Assert.AreEqual("", result.Output); Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error)); }
        else { JsonObject actual = JsonNode.Parse(result.Output)!.AsObject(); Assert.AreEqual(mutation == "unhealthy" ? "Unhealthy" : "Healthy", (string?)actual["healthStatus"]); Assert.AreEqual("", result.Error); }
    }

    [TestMethod]
    [DataRow("zero")]
    [DataRow("oversize")]
    [DataRow("truncated")]
    [DataRow("utf8")]
    public async Task InvalidResponseFramesCannotReachStdoutOrHealthExit(string mode)
    {
        string pipe = TransportTestSupport.NewPipeName();
        await using var rawServer = NewRawServer(pipe);
        await using var probe = new Probe(["--pipe", pipe, "--timeout-ms", "1000", "--max-report-bytes", "1024"]);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        await rawServer.WaitForConnectionAsync(timeout.Token); await TransportTestSupport.ReadFrameAsync(rawServer, timeout.Token);
        byte[] payload = mode == "utf8" ? [0xff] : [];
        uint? size = mode switch { "zero" => 0, "oversize" => uint.MaxValue, "truncated" => 100, _ => null };
        await TransportTestSupport.WriteFrameAsync(rawServer, payload, announcedLength: size, cancellation: timeout.Token);
        if (mode == "truncated") { rawServer.Disconnect(); }
        var result = await probe.Finish(); Assert.AreEqual(2, result.Exit); Assert.AreEqual("", result.Output); Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task ResponseHeaderAndBodyShareOneTotalMonotonicTimeoutBudget()
    {
        string pipe = TransportTestSupport.NewPipeName();
        await using var rawServer = NewRawServer(pipe);
        await using var probe = new Probe(["--pipe", pipe, "--timeout-ms", "800"]);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        await rawServer.WaitForConnectionAsync(timeout.Token); await TransportTestSupport.ReadFrameAsync(rawServer, timeout.Token);
        byte[] payload = Encoding.UTF8.GetBytes(Response("healthy"));
        await Task.Delay(500, timeout.Token);
        byte[] header = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)payload.Length));
        await rawServer.WriteAsync(header, timeout.Token); await rawServer.FlushAsync(timeout.Token);
        await Task.Delay(500, timeout.Token);
        try { await rawServer.WriteAsync(payload, timeout.Token); await rawServer.FlushAsync(timeout.Token); } catch (IOException) { }
        var result = await probe.Finish(); Assert.AreEqual(2, result.Exit, "A fresh per-read timeout would incorrectly accept the delayed complete response."); Assert.AreEqual("", result.Output);
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
            case "error": envelope.Remove("snapshot"); envelope["kind"] = "error"; envelope["error"] = new JsonObject { ["code"] = "ReportingUnavailable" }; break;
            case "missing-snapshot": envelope.Remove("snapshot"); break;
            case "both-branches": envelope["error"] = new JsonObject { ["code"] = "ReportingUnavailable" }; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return envelope.ToJsonString();
    }
}
