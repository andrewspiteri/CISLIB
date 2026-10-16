using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRotationPolicyProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
    private static readonly string[] CommonModes = ["collision-jsonl-file", "collision-human-file", "collision-jsonl-directory", "collision-human-directory", "metadata-jsonl-directory", "metadata-human-directory", "write-jsonl-failure", "write-human-failure"];
    public static IEnumerable<object[]> Modes => (OperatingSystem.IsLinux() ? CommonModes.Append("write-jsonl-os-limit") : CommonModes).Select(mode => new object[] { mode });

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task RealConsumersExposeTerminalPolicyFailureAndHealthyOutput(string mode)
    {
        using var root = new TemporaryDirectory();
        JsonObject actual;
        if (mode == "write-jsonl-os-limit") { actual = await CapacityChild(root.Path); }
        else
        {
            (actual, _) = await FixtureProcessChecks.RunAsync(["file-rotation-policy", mode, root.Path], root.Path, TestContext,
                environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: Limit, flatReport: true);
        }
        FileRotationPolicyExpectations.Validate(mode, actual);
        Assert.AreEqual(Path.Combine(root.Path, "logs", "Ω-🙂.log"), (string?)actual["path"]);
        var physical = new JsonObject();
        foreach (string file in Directory.GetFiles(root.Path, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".cis.lock", StringComparison.Ordinal)))
        { physical.Add(Path.GetRelativePath(root.Path, file).Replace('\\', '/'), Convert.ToBase64String(File.ReadAllBytes(file))); }
        Assert.IsTrue(JsonNode.DeepEquals(physical, actual["after"]));
        CollectionAssert.AreEqual(actual["namesAfter"]!.AsArray().Select(n => (string)n!).ToArray(), Directory.GetFileSystemEntries(Path.Combine(root.Path, "logs")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
    }

    // Only this owned, checkpointed Linux child receives a soft RLIMIT_FSIZE of zero.
    // The hard limit and every parent/global limit remain unchanged.
    private async Task<JsonObject> CapacityChild(string root)
    {
        Assert.IsTrue(OperatingSystem.IsLinux());
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"), core = Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll");
        string hostHash = Hash(host), coreHash = Hash(core), cwd = Directory.GetCurrentDirectory();
        Dictionary<string, string?> parent = Parent();
        string[] command = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-rotation-policy", "write-jsonl-os-limit", root];
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true) };
        foreach (string argument in command) { start.ArgumentList.Add(argument); }
        start.Environment.Remove("DOTNET_SYSTEM_IO_DISABLEFILELOCKING");
        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start owned capacity child.");
        Task<string> stderr = child.StandardError.ReadToEndAsync(); var stdout = new StringBuilder();
        try
        {
            string preparedLine = await child.StandardOutput.ReadLineAsync().WaitAsync(Limit) ?? throw new InvalidDataException("Missing capacity checkpoint.");
            stdout.AppendLine(preparedLine); JsonObject prepared = JsonNode.Parse(preparedLine)!.AsObject();
            Assert.IsTrue(JsonNode.DeepEquals(new JsonObject { ["stage"] = "prepared", ["mode"] = "write-jsonl-os-limit", ["pid"] = child.Id, ["path"] = Path.Combine(root, "logs", "Ω-🙂.log") }, prepared));
            string[] before = await Limits();
            Assert.AreEqual(2, before.Length);
            ProcessResult change = await ProcessRunner.RunAsync("prlimit", ["--pid", child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "--fsize=0:" + before[1]], root, Limit);
            Assert.AreEqual(0, change.ExitCode, change.StandardError); Assert.AreEqual("", change.StandardError);
            CollectionAssert.AreEqual(new[] { "0", before[1] }, await Limits());
            TestContext.WriteLine("Verified owned child capacity limit: soft=0; hard unchanged; before release.");
            await child.StandardInput.WriteLineAsync("continue"); await child.StandardInput.FlushAsync();
            string remaining = await child.StandardOutput.ReadToEndAsync().WaitAsync(Limit); stdout.Append(remaining);
            await child.WaitForExitAsync().WaitAsync(Limit);
            Assert.AreEqual(0, child.ExitCode, await stderr); Assert.AreEqual("", await stderr);
            JsonObject report = JsonNode.Parse(remaining)!.AsObject();
            Assert.AreEqual(child.Id, report["pid"]!.GetValue<int>()); Assert.AreEqual("linux", (string?)report["os"]); Assert.AreEqual(".NET 10.0.12", (string?)report["runtime"]);
            Assert.AreEqual(hostHash, (string?)report["fixtureSha256"]); Assert.AreEqual(coreHash, (string?)report["coreSha256"]);
            Assert.AreEqual(hostHash, Hash(host)); Assert.AreEqual(coreHash, Hash(core));
            TestContext.WriteLine("Physical logging fixture command: " + string.Join(" ", command)); TestContext.WriteLine("Physical child PID: " + child.Id); TestContext.WriteLine(stdout.ToString());
            return report;
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(Limit); }
            await stderr.WaitAsync(Limit);
            Assert.AreEqual(cwd, Directory.GetCurrentDirectory()); Dictionary<string, string?> after = Parent();
            CollectionAssert.AreEquivalent(parent.Keys.ToArray(), after.Keys.ToArray()); Assert.IsTrue(parent.All(p => after[p.Key] == p.Value), "Parent environment changed.");
            TestContext.WriteLine("Owned capacity child exited: " + child.HasExited);
        }

        async Task<string[]> Limits()
        {
            ProcessResult result = await ProcessRunner.RunAsync("prlimit", ["--pid", child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "--fsize", "--output", "SOFT,HARD", "--noheadings", "--raw"], root, Limit);
            Assert.AreEqual(0, result.ExitCode, result.StandardError); Assert.AreEqual("", result.StandardError);
            return result.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string?> Parent() => Environment.GetEnvironmentVariables().Keys.Cast<string>().ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
}
