using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopLifecycleProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => LifecycleProcessChecks.Modes("stop-lifecycle", StopLifecycleExpectations.Modes);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(60);
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ShutdownAndCrashBoundariesUseActualChildren(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string core = Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll"), hostHash = Hash(host), coreHash = Hash(core), parentDirectory = Directory.GetCurrentDirectory();
        Dictionary<string, string?> parent = ParentEnvironment();
        using Process child = Start(host, mode, root.Path); Task<string> stderr = child.StandardError.ReadToEndAsync();
        try
        {
            if (!mode.StartsWith("kill-", StringComparison.Ordinal))
            {
                using var stdoutBytes = new MemoryStream();
                await child.StandardOutput.BaseStream.CopyToAsync(stdoutBytes).WaitAsync(Guard); await child.WaitForExitAsync().WaitAsync(Guard);
                byte[] originalStdout = stdoutBytes.ToArray();
                string stdout = new UTF8Encoding(false, true).GetString(originalStdout);
                string error = await stderr.WaitAsync(Guard); Assert.AreEqual(0, child.ExitCode, error);
                if (mode != "normal-dual") { Assert.AreEqual("", error); }
                JsonObject report = JsonNode.Parse(mode == "normal-dual" ? error : stdout)!.AsObject();
                if (mode == "normal-dual") { StopLifecycleExpectations.PhysicalConsole(report, originalStdout.AsMemory()); }
                Identity(host, child.Id, report); StopLifecycleExpectations.Validate(mode, report, child.Id);
                TestContext.WriteLine(stdout); TestContext.WriteLine(error);
                StopLifecycleReaderControls.Check(mode, report, child.Id, TestContext, stdout); return;
            }
            string? line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)); Assert.IsNotNull(line);
            JsonObject checkpoint = JsonNode.Parse(line)!.AsObject(); Identity(host, child.Id, checkpoint); StopLifecycleExpectations.Checkpoint(mode, checkpoint, child.Id);
            TestContext.WriteLine(line); StopLifecycleReaderControls.Check(mode, checkpoint, child.Id, TestContext);
            Assert.IsFalse(child.HasExited); child.Kill(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.AreNotEqual(0, child.ExitCode);
            Assert.AreEqual("", await stderr.WaitAsync(Guard)); Assert.AreEqual("", await child.StandardOutput.ReadToEndAsync().WaitAsync(Guard));
            string path = Path.Combine(root.Path, "logs Ω", "active.log"); Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(Convert.FromBase64String(checkpoint["bytes"]!.GetValue<string>())));
            using Process restart = Start(host, "restart", root.Path); Task<string> restartError = restart.StandardError.ReadToEndAsync();
            try
            {
                string stdout = await restart.StandardOutput.ReadToEndAsync().WaitAsync(Guard); await restart.WaitForExitAsync().WaitAsync(Guard);
                Assert.AreEqual(0, restart.ExitCode, await restartError.WaitAsync(Guard)); Assert.AreEqual("", await restartError.WaitAsync(Guard));
                JsonObject report = JsonNode.Parse(stdout)!.AsObject(); Identity(host, restart.Id, report); StopLifecycleExpectations.Restart(mode, checkpoint, report, restart.Id);
                TestContext.WriteLine(stdout); StopLifecycleReaderControls.Restart(mode, checkpoint, report, restart.Id, TestContext);
                Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(Convert.FromBase64String(report["bytes"]!.GetValue<string>())));
                string[] damaged = Directory.GetFiles(Path.GetDirectoryName(path)!).Where(p => Path.GetFileName(p).Contains(".damaged", StringComparison.Ordinal)).ToArray();
                Assert.AreEqual(mode == "kill-queued" ? 0 : 1, damaged.Length);
                if (damaged.Length == 1) { Assert.IsTrue(File.ReadAllBytes(damaged[0]).SequenceEqual(Convert.FromBase64String(report["damagedBytes"]!.GetValue<string>()))); }
            }
            finally { if (!restart.HasExited) { restart.Kill(); await restart.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } }
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            Assert.AreEqual(hostHash, Hash(host)); Assert.AreEqual(coreHash, Hash(core)); Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
            Dictionary<string, string?> after = ParentEnvironment();
            CollectionAssert.AreEquivalent(parent.Keys.ToArray(), after.Keys.ToArray());
            Assert.IsTrue(parent.All(pair => after[pair.Key] == pair.Value), "Parent environment changed.");
        }
    }
    private Process Start(string host, string mode, string root)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true)
        };
        foreach (string argument in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "stop-lifecycle", mode, root }) { start.ArgumentList.Add(argument); }
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase)).ToArray()) { start.Environment.Remove(key); }
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Fixture child could not start.");
        TestContext.WriteLine("Physical lifecycle command: " + string.Join(" ", start.ArgumentList)); TestContext.WriteLine("Actual child PID: " + process.Id);
        return process;
    }
    private static void Identity(string host, int pid, JsonObject report)
    {
        Assert.AreEqual(pid, report["pid"]!.GetValue<int>()); Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", report["os"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
    }
    private static Dictionary<string, string?> ParentEnvironment() => Environment.GetEnvironmentVariables().Keys.Cast<string>().ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
