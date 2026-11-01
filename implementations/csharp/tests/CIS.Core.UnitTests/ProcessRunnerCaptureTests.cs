using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
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
        ProcessResult result = await Run(directory, ["repeat", "o", "131072", "OUT-END", "e", "262144", "ERR-END", "7"], 128);

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
        ProcessResult result = await Run(directory, ["text", Text("abcdefghijklmnopqrst"), Text(""), "0"], limit);

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
        ProcessTimeoutException exception = await Assert.ThrowsExactlyAsync<ProcessTimeoutException>(() => ProcessRunner.RunAsync("dotnet",
            Arguments("wait", "before-deadline"),
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
        var environment = new Dictionary<string, string?>
        {
            ["CIS_CAPTURE_CHILD_PID"] = childPidPath,
            ["CIS_CAPTURE_CHILD_READY"] = childReadyPath
        };
        try
        {
            Task<ProcessResult> run = ProcessRunner.RunAsync("dotnet", Arguments("inherited-parent"),
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
        ProcessResult complete = await ProcessRunner.RunAsync("dotnet",
            Arguments("repeat", "p", "1048576", "PATH-END", "e", "0", "diagnostic", "0"),
            directory.Path, Deadline, maximumCapturedStandardOutputBytes: outputLimit);

        Assert.AreEqual(0, complete.ExitCode);
        Assert.AreEqual(ordinaryLimit + 8, complete.StandardOutputBytes.Length);
        Assert.AreEqual((long)ordinaryLimit + 8, complete.StandardOutputTotalBytes);
        Assert.IsTrue(complete.StandardOutput.StartsWith(new string('p', 64), StringComparison.Ordinal));
        Assert.IsTrue(complete.StandardOutput.EndsWith("PATH-END", StringComparison.Ordinal));
        Assert.AreEqual("diagnostic", complete.StandardError);
        Assert.IsFalse(complete.CaptureTruncated);
        Assert.IsTrue(complete.OutputDrained);

        ProcessOutputLimitException overflow = await Assert.ThrowsExactlyAsync<ProcessOutputLimitException>(() => ProcessRunner.RunAsync("dotnet",
            Arguments("repeat", "o", "0", "complete", "e", "1048576", "ERR-END", "0"),
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
        ProcessOutputLimitException exception = await Assert.ThrowsExactlyAsync<ProcessOutputLimitException>(() => ProcessRunner.RunAsync("dotnet",
            Arguments("text", Text("{\"value\":\"" + new string('x', 2048) + "\"}"), Text(""), "0"),
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

    private static string Text(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string[] Arguments(params string[] values) =>
        ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Environment.GetEnvironmentVariable("CIS_VERIFICATION_CHECKER") ?? typeof(SourceInventory).Assembly.Location,
            "process-capture-fixture", .. values];

    private static Task<ProcessResult> Run(TemporaryDirectory directory, string[] values, int maximumBytes) => ProcessRunner.RunAsync("dotnet",
        Arguments(values), directory.Path, Deadline, maximumCapturedBytesPerStream: maximumBytes, allowTruncatedOutput: true);
}
