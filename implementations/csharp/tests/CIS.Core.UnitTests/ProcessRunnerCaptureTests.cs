using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProcessRunnerCaptureTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task CappedStreamsDrainToNativeExitAndRetainTheirOwnHeadAndTail()
    {
        using var directory = new TemporaryDirectory();
        ProcessResult result = await Run(directory, "[Console]::Out.Write(('o' * 131072) + 'OUT-END'); [Console]::Error.Write(('e' * 262144) + 'ERR-END'); exit 7", 128);

        Assert.AreEqual(7, result.ExitCode);
        Assert.IsTrue(result.ProcessId > 0);
        Assert.AreEqual(new string('o', 121) + "OUT-END", result.StandardOutput);
        Assert.AreEqual(new string('e', 121) + "ERR-END", result.StandardError);
        Assert.AreEqual(131079L, result.StandardOutputTotalBytes);
        Assert.AreEqual(262151L, result.StandardErrorTotalBytes);
        Assert.AreEqual(128, result.StandardOutputBytes.Length);
        Assert.AreEqual(128, result.StandardErrorBytes.Length);
        Assert.IsTrue(result.StandardOutputTruncated && result.StandardErrorTruncated && result.CaptureTruncated);
        Assert.IsTrue(result.OutputDrained);
    }

    [TestMethod]
    [DataRow(0, "")]
    [DataRow(1, "a")]
    [DataRow(2, "at")]
    [DataRow(7, "abcdrst")]
    [DataRow(20, "abcdefghijklmnopqrst")]
    public async Task ExactAndSmallCapsPreserveTheirDeclaredByteBound(int limit, string expected)
    {
        using var directory = new TemporaryDirectory();
        ProcessResult result = await Run(directory, "[Console]::Out.Write('abcdefghijklmnopqrst')", limit);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(expected, result.StandardOutput);
        Assert.AreEqual(20L, result.StandardOutputTotalBytes);
        Assert.AreEqual(limit < 20, result.StandardOutputTruncated);
        Assert.AreEqual(0L, result.StandardErrorTotalBytes);
        Assert.IsFalse(result.StandardErrorTruncated);
        Assert.IsTrue(result.OutputDrained);
    }

    [TestMethod]
    public async Task TimeoutRetainsPartialDiagnosticsAndObservesTheStoppedProcess()
    {
        using var directory = new TemporaryDirectory();
        ProcessTimeoutException exception = await Assert.ThrowsExactlyAsync<ProcessTimeoutException>(() => ProcessRunner.RunAsync("pwsh",
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write('before-deadline'); [Console]::Out.Flush(); [System.Threading.ManualResetEvent]::new($false).WaitOne()"],
            directory.Path, TimeSpan.FromSeconds(3), maximumCapturedBytesPerStream: 128));

        Assert.AreEqual("before-deadline", exception.Result.StandardOutput);
        Assert.AreEqual(15L, exception.Result.StandardOutputTotalBytes);
        Assert.IsTrue(exception.CleanupCompleted);
        Assert.IsTrue(exception.Result.OutputDrained);
        Assert.IsFalse(exception.Result.CaptureTruncated);
        Assert.AreNotEqual(0, exception.Result.ExitCode);
        try
        {
            using Process process = Process.GetProcessById(exception.Result.ProcessId);
            Assert.IsTrue(process.HasExited);
        }
        catch (ArgumentException) { /* A reaped process is also no longer running. */ }
    }

    [TestMethod]
    public async Task ExitedParentWithInheritedChildPipesReturnsIncompleteCleanupWithinTheBound()
    {
        using var directory = new TemporaryDirectory();
        string childPidPath = Path.Combine(directory.Path, "child.pid");
        string childReadyPath = Path.Combine(directory.Path, "child.ready");
        string childScript = "[Console]::Out.Write('owned-child'); [Console]::Out.Flush(); [IO.File]::WriteAllText($env:CIS_CAPTURE_CHILD_READY, 'ready'); [Threading.ManualResetEvent]::new($false).WaitOne()";
        string encodedChild = Convert.ToBase64String(Encoding.Unicode.GetBytes(childScript));
        string parentScript = """
            $info = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
            $info.UseShellExecute = $false
            $info.ArgumentList.Add('-NoProfile')
            $info.ArgumentList.Add('-NonInteractive')
            $info.ArgumentList.Add('-EncodedCommand')
            $info.ArgumentList.Add($env:CIS_CAPTURE_CHILD_SCRIPT)
            $child = [Diagnostics.Process]::Start($info)
            [IO.File]::WriteAllText($env:CIS_CAPTURE_CHILD_PID, $child.Id.ToString([Globalization.CultureInfo]::InvariantCulture))
            $clock = [Diagnostics.Stopwatch]::StartNew()
            while (![IO.File]::Exists($env:CIS_CAPTURE_CHILD_READY)) {
                if ($child.HasExited -or $clock.Elapsed.TotalSeconds -gt 3) { throw 'Owned child did not become ready' }
                [Threading.Thread]::Sleep(10)
            }
            exit 0
            """;
        var environment = new Dictionary<string, string?>
        {
            ["CIS_CAPTURE_CHILD_SCRIPT"] = encodedChild,
            ["CIS_CAPTURE_CHILD_PID"] = childPidPath,
            ["CIS_CAPTURE_CHILD_READY"] = childReadyPath
        };
        try
        {
            Task<ProcessResult> run = ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", parentScript],
                directory.Path, TimeSpan.FromSeconds(5), environment, maximumCapturedBytesPerStream: 128);
            ProcessTimeoutException exception = await Assert.ThrowsExactlyAsync<ProcessTimeoutException>(() => run.WaitAsync(Deadline));

            Assert.IsTrue(File.Exists(childReadyPath), "The actual descendant must have inherited and written the parent's stdout pipe.");
            Assert.AreEqual("owned-child", exception.Result.StandardOutput);
            Assert.AreEqual(0, exception.Result.ExitCode, "The original parent exited naturally; its descendant still owns the pipes.");
            Assert.IsFalse(exception.CleanupCompleted);
            Assert.IsFalse(exception.Result.OutputDrained);
        }
        finally
        {
            if (File.Exists(childPidPath))
            {
                int pid = int.Parse(File.ReadAllText(childPidPath), CultureInfo.InvariantCulture);
                try
                {
                    using Process child = Process.GetProcessById(pid);
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); }
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (ArgumentException) { /* The owned child has already exited. */ }
            }
        }
    }

    [TestMethod]
    public async Task InvalidCaptureLimitsAreRejectedBeforeAnyProcessStarts()
    {
        foreach (int limit in new[] { -1, 64 * 1024 * 1024 + 1 })
        {
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => ProcessRunner.RunAsync("must-not-start", [],
                Environment.CurrentDirectory, Deadline, maximumCapturedBytesPerStream: limit));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => ProcessRunner.RunAsync("must-not-start", [],
                Environment.CurrentDirectory, Deadline, maximumCapturedStandardOutputBytes: limit));
        }
    }

    [TestMethod]
    public async Task DeliberateLargeAuthoritativeStdoutCaptureKeepsTheDefaultStderrBoundAndRejectsTruncation()
    {
        using var directory = new TemporaryDirectory();
        const int ordinaryLimit = 1_048_576, outputLimit = 2 * ordinaryLimit;
        ProcessResult complete = await ProcessRunner.RunAsync("pwsh",
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write(('p' * 1048576) + 'PATH-END'); [Console]::Error.Write('diagnostic')"],
            directory.Path, Deadline, maximumCapturedStandardOutputBytes: outputLimit);

        Assert.AreEqual(0, complete.ExitCode);
        Assert.AreEqual(ordinaryLimit + 8, complete.StandardOutputBytes.Length);
        Assert.AreEqual((long)ordinaryLimit + 8, complete.StandardOutputTotalBytes);
        Assert.IsTrue(complete.StandardOutput.StartsWith(new string('p', 64), StringComparison.Ordinal));
        Assert.IsTrue(complete.StandardOutput.EndsWith("PATH-END", StringComparison.Ordinal));
        Assert.AreEqual("diagnostic", complete.StandardError);
        Assert.IsFalse(complete.CaptureTruncated);
        Assert.IsTrue(complete.OutputDrained);

        ProcessOutputLimitException overflow = await Assert.ThrowsExactlyAsync<ProcessOutputLimitException>(() => ProcessRunner.RunAsync("pwsh",
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write('complete'); [Console]::Error.Write(('e' * 1048576) + 'ERR-END')"],
            directory.Path, Deadline, maximumCapturedStandardOutputBytes: outputLimit));

        Assert.AreEqual("complete", overflow.Result.StandardOutput);
        Assert.IsFalse(overflow.Result.StandardOutputTruncated);
        Assert.AreEqual(ordinaryLimit, overflow.Result.StandardErrorBytes.Length);
        Assert.AreEqual((long)ordinaryLimit + 7, overflow.Result.StandardErrorTotalBytes);
        Assert.IsTrue(overflow.Result.StandardErrorTruncated);
        Assert.IsTrue(overflow.Result.OutputDrained);
        Assert.AreEqual(0, overflow.Result.ExitCode, "Native success cannot override incomplete authoritative capture.");
    }

    [TestMethod]
    public async Task DefaultPolicyRejectsTruncatedAuthoritativeOutputEvenWhenItsJoinedJsonParses()
    {
        using var directory = new TemporaryDirectory();
        ProcessOutputLimitException exception = await Assert.ThrowsExactlyAsync<ProcessOutputLimitException>(() => ProcessRunner.RunAsync("pwsh",
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write('{\"value\":\"' + ('x' * 2048) + '\"}'); exit 0"],
            directory.Path, Deadline, maximumCapturedBytesPerStream: 64));

        Assert.AreEqual(0, exception.Result.ExitCode);
        Assert.IsTrue(exception.Result.OutputDrained);
        Assert.IsTrue(exception.Result.StandardOutputTruncated);
        Assert.IsFalse(exception.Result.StandardErrorTruncated);
        Assert.AreEqual(64, exception.Result.StandardOutputBytes.Length);
        JsonObject partial = JsonNode.Parse(exception.Result.StandardOutput)!.AsObject();
        Assert.IsTrue(partial["value"]!.GetValue<string>().Length < 2048,
            "A syntactically valid head/tail JSON document still lacks authoritative original output.");
    }

    private static Task<ProcessResult> Run(TemporaryDirectory directory, string command, int maximumBytes) => ProcessRunner.RunAsync("pwsh",
        ["-NoProfile", "-NonInteractive", "-Command", command], directory.Path, Deadline, maximumCapturedBytesPerStream: maximumBytes, allowTruncatedOutput: true);
}
