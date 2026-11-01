using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MutationChildren = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildSupport;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

[TestClass]
public sealed partial class HealthcheckProbeProcessTests
{
    private sealed class Probe : IAsyncDisposable
    {
        private readonly Process child;
        private readonly Task<string> stdout, stderr;
        private readonly bool captureExit;
        private Task? observedExit;
        internal Process ActualChild => child;
        internal Task<string> ActualOutput => stdout;
        internal Task<string> ActualError => stderr;
        internal Task? ActualExit => observedExit;
        internal Probe(IEnumerable<string> arguments, bool captureExit = false)
        {
            this.captureExit = captureExit;
            child = Process.Start(TransportTestSupport.StartInfo("healthcheck-probe", "CisHealthProbe", arguments))!;
            MutationChildren.ObserveStarted(child);
            stdout = child.StandardOutput.ReadToEndAsync(); stderr = child.StandardError.ReadToEndAsync();
        }
        internal async Task<(int Exit, string Output, string Error)> Finish()
        {
            Task exiting = child.WaitForExitAsync();
            if (captureExit) { observedExit = exiting; }
            try { await exiting.WaitAsync(TransportTestSupport.Limit); }
            catch (TimeoutException) { MutationChildren.MarkTimeout(child, "probe-finish"); throw; }
            string output = await stdout, error = await stderr;
            Assert.IsTrue(error.Length <= 1024, "CLI error text remains bounded.");
            Assert.IsFalse(output.StartsWith('\uFEFF'));
            return (child.ExitCode, output, error);
        }
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            var actualDrains = new List<Task> { stdout, stderr };
            bool exited = false;
            try
            {
                Task actualExit = child.WaitForExitAsync();
                actualDrains.Add(actualExit);
                // A body can fail while the probe is already completing its
                // documented error exit. Join that real operation before
                // asking to terminate an otherwise normally exiting child.
                await actualExit.WaitAsync(TransportTestSupport.Limit);
                exited = true;
            }
            catch (TimeoutException error)
            {
                Add(error);
                try { MutationChildren.MarkTimeout(child, "probe-cleanup-exit"); }
                catch (Exception markerError) { Add(markerError); }
            }
            catch (Exception error) { Add(error); }
            if (!exited)
            {
                bool needsStop = true;
                try { needsStop = !child.HasExited; }
                catch (Exception error) { Add(error); }
                if (needsStop)
                {
                    try { MutationChildren.MarkForcedStop(child, "cleanup"); }
                    catch (Exception error) { Add(error); }
                    try { child.Kill(entireProcessTree: true); }
                    catch (Exception error) { Add(error); }
                }
                try
                {
                    Task actualReap = child.WaitForExitAsync();
                    actualDrains.Add(actualReap);
                    await actualReap.WaitAsync(TransportTestSupport.Limit);
                }
                catch (Exception error) { Add(error); }
            }
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TransportTestSupport.Limit); }
            catch (TimeoutException error)
            {
                Add(error);
                try { MutationChildren.MarkTimeout(child, "probe-cleanup-output"); }
                catch (Exception markerError) { Add(markerError); }
            }
            catch (Exception error) { Add(error); }
            try { MutationChildren.CompleteLifecycle(child); }
            catch (Exception error) { Add(error); }
            try { child.Dispose(); }
            catch (Exception error) { Add(error); }
            // Final cleanup may itself publish a reader failure. Observe every
            // now-completed task AFTER both final attempts, without awaiting
            // any still-pending task or certifying missing work as done.
            foreach (Task actual in actualDrains)
            {
                if (actual.IsFaulted)
                {
                    foreach (Exception error in actual.Exception!.Flatten().InnerExceptions) { Add(error); }
                }
                else if (actual.IsCanceled)
                {
                    try { await actual; } catch (Exception error) { Add(error); }
                }
            }
            if (failures.Count == 1) { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
            if (failures.Count > 1) { throw new AggregateException("Actual probe cleanup did not settle normally.", failures); }

            void Add(Exception error)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, error))) { failures.Add(error); }
            }
        }
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
    public Task ActualProbeProcessExitsExactlyForSelectedCompleteView(string view, bool unhealthy, bool startup, bool details, int exit)
    {
        ProbeProcessObservation observed = RunProbeProcessScenario(view, unhealthy, startup, details, exit);
        ReviewProbeProcessScenario(observed);
        return Task.CompletedTask;
    }

    [TestMethod]
    public async Task ActualEntrypointReturnsExit2AndBoundedStderrForInvalidArguments()
    {
        await using var probe = new Probe(["--unknown", "value"]);
        ErrorResult(await probe.Finish(), "InvalidArguments");
    }

    [TestMethod]
    [DataRow("", "")]
    public async Task OmittedViewAndValidUpperCliLimitsStillRequestAndEmitHealth(string option, string value)
    {
        string pipe = TransportTestSupport.NewPipeName();
        var rawServer = NewRawServer(pipe);
        string[] arguments = option.Length == 0 ? ["--pipe", pipe] : ["--pipe", pipe, option, value];
        using var timeout = new CancellationTokenSource();
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Probe? probe = null;
        Task? childExit = null, connecting = null, fixtureCanceledConnection = null;
        var actualDrains = new List<Task>();
        var failures = new List<Exception>();
        bool validArgumentsRejectedBeforeConnection = false;
        try
        {
            probe = new Probe(arguments);
            timeout.CancelAfter(TransportTestSupport.Limit);
            childExit = probe.ActualChild.WaitForExitAsync();
            actualDrains.AddRange([childExit, probe.ActualOutput, probe.ActualError]);
            connecting = rawServer.WaitForConnectionAsync(connectionCancellation.Token);
            actualDrains.Add(connecting);
            if (await AwaitRequestProgress(connecting))
            {
                Task<byte[]> reading = TransportTestSupport.ReadFrameAsync(rawServer, timeout.Token);
                actualDrains.Add(reading);
                await AwaitRequestProgress(reading);
                byte[] request = await reading;
                Assert.AreEqual("{\"protocolVersion\":1,\"view\":\"health\"}", Encoding.UTF8.GetString(request));
                string response = HealthyResponse();
                Task writing = TransportTestSupport.WriteFrameAsync(rawServer, Encoding.UTF8.GetBytes(response), cancellation: timeout.Token);
                actualDrains.Add(writing);
                await writing;
                Task<(int Exit, string Output, string Error)> finishing = probe.Finish();
                actualDrains.Add(finishing);
                var result = await finishing;
                Assert.AreEqual(0, result.Exit, result.Error);
                Assert.AreEqual(JsonNode.Parse(response)!["snapshot"]!.ToJsonString() + "\n", result.Output);
                Assert.AreEqual("", result.Error);
            }
        }
        catch (Exception error) { Add(error); }

        // Preserve every error already completed before fixture cancellation.
        await ObserveCompleted();
        if (validArgumentsRejectedBeforeConnection && failures.Count == 0
            && connecting is { IsCompleted: false } && !timeout.IsCancellationRequested)
        {
            try
            {
                // Only this private token is canceled here. The server is still
                // undisposed; its internal disposal token cannot cause this join.
                connectionCancellation.Cancel();
                await connecting.WaitAsync(TransportTestSupport.Limit);
            }
            catch (OperationCanceledException) when (connecting.IsCanceled
                && connectionCancellation.IsCancellationRequested && !timeout.IsCancellationRequested)
            {
                // Exact fixture-created BCL connection wait only. Core frame,
                // child, output and disposal tasks never use this exception rule.
                fixtureCanceledConnection = connecting;
            }
            catch (Exception error) { Add(error); }
        }
        if (probe is not null)
        {
            try
            {
                Task disposingProbe = probe.DisposeAsync().AsTask();
                actualDrains.Add(disposingProbe);
                await disposingProbe.WaitAsync(TransportTestSupport.Limit);
            }
            catch (Exception error) { Add(error); }
        }
        try
        {
            Task disposingServer = rawServer.DisposeAsync().AsTask();
            actualDrains.Add(disposingServer);
            await disposingServer.WaitAsync(TransportTestSupport.Limit);
        }
        catch (Exception error) { Add(error); }
        Task drains = Task.WhenAll(actualDrains.Where(actual => !ReferenceEquals(actual, fixtureCanceledConnection)));
        try { await drains.WaitAsync(TransportTestSupport.Limit); }
        catch (Exception error) { Add(error); }
        // Final disposal can publish an original reader/task error. Observe it
        // after both owners' cleanup, without treating pending work as complete.
        await ObserveCompleted();
        if (failures.Count == 1) { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1) { throw new AggregateException("The valid-request body or its actual child/server cleanup did not settle.", failures); }
        if (validArgumentsRejectedBeforeConnection)
        {
            Assert.Fail("The documented valid default or inclusive upper CLI limit must be accepted and send its request before a response.");
        }

        async Task<bool> AwaitRequestProgress(Task progress)
        {
            await Task.WhenAny(progress, childExit!).WaitAsync(TransportTestSupport.Limit);
            if (progress.IsCompleted) { await progress; return true; }
            await childExit!;
            await Task.WhenAll(probe!.ActualOutput, probe.ActualError).WaitAsync(TransportTestSupport.Limit);
            if (progress.IsCompleted) { await progress; return true; }
            string output = await probe.ActualOutput, error = await probe.ActualError;
            string reason = error.Trim();
            const string currentProgramLabel = "cis-health-probe:";
            if (reason.StartsWith(currentProgramLabel, StringComparison.Ordinal)) { reason = reason[currentProgramLabel.Length..].Trim(); }
            if (ReferenceEquals(progress, connecting) && probe.ActualChild.ExitCode == 2
                && output.Length == 0 && error.Length <= 1024 && reason == "InvalidArguments")
            {
                validArgumentsRejectedBeforeConnection = true;
                return false;
            }
            // No assertion promotion for EOF, a frame cancellation, unknown
            // child failure or a timeout/transport diagnostic.
            throw new InvalidOperationException("The actual probe exited before its pending request operation completed; exit=" + probe.ActualChild.ExitCode + "; stderr=" + error);
        }

        async Task ObserveCompleted()
        {
            foreach (Task actual in actualDrains)
            {
                if (actual.IsFaulted)
                {
                    foreach (Exception error in actual.Exception!.Flatten().InnerExceptions) { Add(error); }
                }
                else if (actual.IsCanceled
                    && !(ReferenceEquals(actual, fixtureCanceledConnection) && !timeout.IsCancellationRequested))
                {
                    try { await actual; } catch (Exception error) { Add(error); }
                }
            }
        }

        void Add(Exception error)
        {
            if (!failures.Any(existing => ReferenceEquals(existing, error))) { failures.Add(error); }
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

    private static string HealthyResponse() => new JsonObject
    {
        ["protocolVersion"] = 1,
        ["schemaVersion"] = 1,
        ["view"] = "health",
        ["kind"] = "snapshot",
        ["snapshot"] = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["revision"] = "42",
            ["capturedAtUtc"] = "2026-10-21T08:00:00.0000000Z",
            ["application"] = new JsonObject { ["name"] = "independent-probe-oracle", ["instanceId"] = "10000000-0000-0000-0000-000000000001", ["startupCompleted"] = true, ["isStopping"] = false },
            ["healthStatus"] = "Healthy",
            ["ready"] = true,
            ["monitorCounts"] = new JsonObject { ["active"] = 1, ["critical"] = 1, ["failing"] = 0, ["criticalFailing"] = 0, ["pending"] = 0 }
        }
    }.ToJsonString();
}
