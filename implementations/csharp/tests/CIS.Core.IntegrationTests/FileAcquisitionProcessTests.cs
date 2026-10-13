using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileAcquisitionProcessTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static string Host => Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SameGroupIsExclusiveWhileDistinctGroupAndReadersWorkThenOwnershipIsReleased(bool terminate)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "\u03a9-\U0001f642.jsonl"), distinct = Path.Combine(root.Path, "other.jsonl");
        byte[] retained = Encoding.UTF8.GetBytes("pre-existing-active-data\n"); File.WriteAllBytes(path, retained);
        using var holder = new Process { StartInfo = Start("hold", path, root.Path) };
        holder.Start(); Task<string> stderr = holder.StandardError.ReadToEndAsync();
        try
        {
            JsonObject ready = JsonNode.Parse((await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit))!)!.AsObject();
            Validate(ready, "acquired"); Assert.AreEqual(retained.Length, ready["length"]!.GetValue<long>());
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            { byte[] observed = new byte[retained.Length]; reader.ReadExactly(observed); CollectionAssert.AreEqual(retained, observed); }
            JsonObject blocked = await Probe("probe", path, root.Path); Validate(blocked, "failed");
            AssertSafe(blocked, "FileOwnershipUnavailable");
            Validate(await Probe("probe", distinct, root.Path), "acquired");
            Assert.AreEqual(0L, new FileInfo(distinct).Length);
            if (terminate) { holder.Kill(entireProcessTree: true); }
            else
            {
                await holder.StandardInput.WriteLineAsync("release"); await holder.StandardInput.FlushAsync();
                Validate(JsonNode.Parse((await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit))!)!.AsObject(), "released");
            }
            await holder.WaitForExitAsync().WaitAsync(Limit);
            if (!terminate) { Assert.AreEqual(0, holder.ExitCode); }
            Assert.AreEqual("", await stderr.WaitAsync(Limit));
            Validate(await Probe("probe", path, root.Path), "acquired");
            CollectionAssert.AreEqual(retained, File.ReadAllBytes(path));
            Assert.IsTrue(File.Exists(path + ".cis.lock"));
        }
        finally
        {
            if (!holder.HasExited) { holder.Kill(entireProcessTree: true); }
            await holder.WaitForExitAsync().WaitAsync(Limit); await stderr.WaitAsync(Limit);
        }
    }

    [TestMethod]
    public async Task DisabledUnixFileLockingCannotSilentlyCreateAnUnownedActiveFile()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        JsonObject result = await Probe("disabled-locks", path, root.Path);
        Validate(result, OperatingSystem.IsWindows() ? "acquired" : "failed");
        if (!OperatingSystem.IsWindows()) { AssertSafe(result, "FileOwnershipUnavailable"); Assert.IsFalse(File.Exists(path)); }
        Validate(await Probe("probe", path, root.Path), "acquired");
    }

    [TestMethod]
    [DataRow("logs/app.jsonl")]
    [DataRow("logs/\u03a9-\U0001f642.jsonl")]
    public async Task CapturedPathIsAcquiredInItsOriginalDirectoryFromAnotherChildCwd(string relative)
    {
        using var root = new TemporaryDirectory(); string captured = Path.Combine(root.Path, "original"), later = Path.Combine(root.Path, "later");
        var options = new CIS.Logging.LoggingOptions { FilePath = relative }; options.Prepare(captured);
        Assert.IsFalse(Directory.Exists(captured)); Directory.CreateDirectory(later);
        Validate(await Probe("probe", options.FilePath!, later), "acquired");
        Assert.IsTrue(File.Exists(options.FilePath)); Assert.AreEqual(0, Directory.GetFileSystemEntries(later).Length);
    }

    private static ProcessStartInfo Start(string mode, string path, string cwd)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true)
        };
        foreach (string value in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-acquisition", mode, path }) { start.ArgumentList.Add(value); }
        start.Environment.Remove("DOTNET_SYSTEM_IO_DISABLEFILELOCKING");
        return start;
    }
    private static async Task<JsonObject> Probe(string mode, string path, string cwd)
    {
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-acquisition", mode, path], cwd, Limit,
            new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, new UTF8Encoding(false, true));
        Assert.AreEqual(0, result.ExitCode, result.StandardError); Assert.AreEqual("", result.StandardError);
        return JsonNode.Parse(result.StandardOutput)!.AsObject();
    }
    private static void Validate(JsonObject result, string status)
    {
        Assert.AreEqual(status, result["status"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", result["runtime"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", result["os"]!.GetValue<string>());
        Assert.AreEqual(Hash(Host), result["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll")), result["coreSha256"]!.GetValue<string>());
        if (status != "failed") { Assert.IsNull(result["error"]); }
    }
    private static void AssertSafe(JsonObject result, string code)
    {
        JsonObject error = result["error"]!.AsObject();
        Assert.AreEqual("file", error["output"]!.GetValue<string>()); Assert.AreEqual(code, error["code"]!.GetValue<string>());
        Assert.AreEqual($"Logging output 'file' could not initialize ({code}).", error["message"]!.GetValue<string>());
        Assert.IsFalse(error["inner"]!.GetValue<bool>()); Assert.AreEqual(0, error["dataCount"]!.GetValue<int>());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
