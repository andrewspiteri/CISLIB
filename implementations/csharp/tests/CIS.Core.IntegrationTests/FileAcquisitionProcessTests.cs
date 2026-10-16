using System.Collections;
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
    private static string Core => Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll");
    private string hostHash = "", coreHash = "", parentDirectory = "";
    private Dictionary<string, string?> parentEnvironment = [];
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void CaptureParent()
    {
        hostHash = Hash(Host); coreHash = Hash(Core); parentDirectory = Directory.GetCurrentDirectory();
        parentEnvironment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(p => (string)p.Key, p => (string?)p.Value, StringComparer.Ordinal);
    }

    [TestCleanup]
    public void ParentAndManagedInputsRemainUnchanged()
    {
        Assert.AreEqual(hostHash, Hash(Host)); Assert.AreEqual(coreHash, Hash(Core));
        Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
        Dictionary<string, string?> current = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(p => (string)p.Key, p => (string?)p.Value, StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(parentEnvironment.Keys.ToArray(), current.Keys.ToArray());
        foreach ((string key, string? value) in parentEnvironment) { Assert.AreEqual(value, current[key], "Parent environment changed."); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SameGroupIsExclusiveWhileDistinctGroupAndReadersWorkThenOwnershipIsReleased(bool terminate)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "Ω-🙂.jsonl"), distinct = Path.Combine(root.Path, "other.jsonl");
        byte[] retained = Encoding.UTF8.GetBytes("existing-unvalidated-data\n"); File.WriteAllBytes(path, retained);
        using var holder = new Process { StartInfo = Start("hold", path, root.Path) };
        holder.Start(); Task<string> stderr = holder.StandardError.ReadToEndAsync();
        TestContext.WriteLine($"Physical acquisition child: {holder.Id}; hold; {path}; {hostHash}; {coreHash}");
        try
        {
            string? ready = await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit); Assert.IsNotNull(ready);
            TestContext.WriteLine(ready); Validate(Parse(ready), "acquired", retained.Length, pid: holder.Id);
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            { byte[] observed = new byte[retained.Length]; reader.ReadExactly(observed); CollectionAssert.AreEqual(retained, observed); }
            await Probe("probe", path, root.Path, "failed", null, "FileOwnershipUnavailable");
            string alias = Path.Combine(root.Path, ".", Path.GetFileName(path));
            await Probe("probe", alias, root.Path, "failed", null, "FileOwnershipUnavailable");
            await Probe("probe", distinct, root.Path);
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            { byte[] observed = new byte[retained.Length]; reader.ReadExactly(observed); CollectionAssert.AreEqual(retained, observed); }
            if (terminate) { holder.Kill(entireProcessTree: true); }
            else
            {
                await holder.StandardInput.WriteLineAsync("release"); await holder.StandardInput.FlushAsync();
                string? released = await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit); Assert.IsNotNull(released);
                TestContext.WriteLine(released); Validate(Parse(released), "released", null, pid: holder.Id);
            }
            await holder.WaitForExitAsync().WaitAsync(Limit);
            if (terminate) { Assert.AreNotEqual(0, holder.ExitCode); } else { Assert.AreEqual(0, holder.ExitCode); }
            Assert.AreEqual("", await stderr.WaitAsync(Limit));
            Assert.AreEqual("", await holder.StandardOutput.ReadToEndAsync().WaitAsync(Limit));
            await Probe("probe", path, root.Path, length: retained.Length);
            CollectionAssert.AreEqual(retained, File.ReadAllBytes(path));
            CollectionAssert.AreEqual(Array.Empty<byte>(), File.ReadAllBytes(path + ".cis.lock"));
        }
        finally
        {
            if (!holder.HasExited) { holder.Kill(entireProcessTree: true); }
            await holder.WaitForExitAsync().WaitAsync(Limit); await stderr.WaitAsync(Limit);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisabledUnixFileLockingCannotSilentlyCreateAnUnownedActiveFile(bool environmentOverride)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        bool windows = OperatingSystem.IsWindows();
        await Probe(environmentOverride ? "probe" : "disabled-locks", path, root.Path,
            windows ? "acquired" : "failed", windows ? 0 : null, windows ? null : "FileOwnershipUnavailable", environmentOverride);
        Assert.AreEqual(windows, File.Exists(path));
        await Probe("probe", path, root.Path);
    }

    [TestMethod]
    [DataRow("logs/app.jsonl")]
    [DataRow("logs/Ω-🙂-é.jsonl")]
    public async Task CapturedPathIsAcquiredInItsOriginalDirectoryFromAnotherChildCwd(string relative)
    {
        using var root = new TemporaryDirectory(); string captured = Path.Combine(root.Path, "original"), later = Path.Combine(root.Path, "later");
        var options = new CIS.Logging.LoggingOptions { FilePath = relative }; options.Prepare(captured);
        Assert.IsFalse(Directory.Exists(captured)); Directory.CreateDirectory(later);
        await Probe("probe", options.FilePath!, later);
        CollectionAssert.AreEqual(Array.Empty<byte>(), File.ReadAllBytes(options.FilePath!));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(later).Length);
    }

    [TestMethod]
    public async Task UnrelatedLeaseBytesArePreservedWithoutCreatingAnActiveFile()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        byte[] retained = Encoding.UTF8.GetBytes("unrelated"); File.WriteAllBytes(path + ".cis.lock", retained);
        await Probe("probe", path, root.Path, "failed", null, "FileOwnershipUnavailable");
        Assert.IsFalse(File.Exists(path)); CollectionAssert.AreEqual(retained, File.ReadAllBytes(path + ".cis.lock"));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task FailedActiveOpenReleasesItsLeaseAndPreservesTheDirectory(int attempts)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active-directory"); Directory.CreateDirectory(path);
        string retained = Path.Combine(path, "retained.txt"); File.WriteAllText(retained, "keep");
        for (int i = 0; i < attempts; i++) { await Probe("probe", path, root.Path, "failed", null, "FileInitializationFailed"); }
        Assert.AreEqual("keep", File.ReadAllText(retained));
    }

    [TestMethod]
    public async Task ParentFileFailureDoesNotChooseAnotherOutputPath()
    {
        using var root = new TemporaryDirectory(); string parent = Path.Combine(root.Path, "parent-file"), later = Path.Combine(root.Path, "later");
        File.WriteAllText(parent, "keep-parent"); Directory.CreateDirectory(later);
        await Probe("probe", Path.Combine(parent, "active.jsonl"), later, "failed", null, "FileInitializationFailed");
        Assert.AreEqual("keep-parent", File.ReadAllText(parent)); Assert.AreEqual(0, Directory.GetFileSystemEntries(later).Length);
    }

    [TestMethod]
    [DataRow("status")]
    [DataRow("coreSha256")]
    [DataRow("length")]
    [DataRow("error")]
    public async Task OriginalReaderCorruptionsAreRejectedAfterPhysicalPositive(string field)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        File.WriteAllText(path + ".cis.lock", "unrelated");
        JsonObject report = await Probe("probe", path, root.Path, "failed", null, "FileOwnershipUnavailable");
        if (field == "error") { report["error"]!["inner"] = true; } else { report[field] = "damaged"; }
        string reason = field switch { "status" => "status", "coreSha256" => "Core hash", "length" => "length", _ => "safe error" };
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => Validate(report, "failed", null, "FileOwnershipUnavailable", report["pid"]!.GetValue<int>())).Message);
        TestContext.WriteLine($"Rejected original acquisition control: {field}");
    }

    private static ProcessStartInfo Start(string mode, string path, string cwd, bool disabledEnvironment = false)
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
        if (disabledEnvironment) { start.Environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = "1"; }
        return start;
    }

    private async Task<JsonObject> Probe(string mode, string path, string cwd, string status = "acquired", long? length = 0, string? code = null, bool disabledEnvironment = false)
    {
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-acquisition", mode, path], cwd, Limit,
            new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = disabledEnvironment ? "1" : null }, new UTF8Encoding(false, true));
        TestContext.WriteLine($"Physical acquisition child: {result.ProcessId}; {mode}; {path}; {hostHash}; {coreHash}");
        TestContext.WriteLine(result.StandardOutput); TestContext.WriteLine(result.StandardError);
        Assert.AreEqual(0, result.ExitCode, result.StandardError); Assert.AreEqual("", result.StandardError);
        JsonObject report = Parse(result.StandardOutput); Validate(report, status, length, code, result.ProcessId);
        return report;
    }

    private static JsonObject Parse(string text)
    {
        Assert.IsFalse(text.StartsWith('\uFEFF'));
        return JsonNode.Parse(text)!.AsObject();
    }

    private void Validate(JsonObject result, string status, long? length, string? code = null, int pid = 0)
    {
        Require(status == result["status"]!.GetValue<string>(), "status");
        Assert.AreEqual(".NET 10.0.12", result["runtime"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", result["os"]!.GetValue<string>());
        Assert.AreEqual(hostHash, result["fixtureSha256"]!.GetValue<string>());
        Require(coreHash == result["coreSha256"]!.GetValue<string>(), "Core hash");
        Assert.AreEqual(pid, result["pid"]!.GetValue<int>());
        if (length.HasValue) { Assert.AreEqual(JsonValue.Create(length.Value)!.ToJsonString(), result["length"]!.ToJsonString()); }
        else { Require(result["length"] is null, "length"); }
        if (code is null) { Assert.IsNull(result["error"]); }
        else
        {
            var expected = new JsonObject { ["output"] = "file", ["code"] = code, ["message"] = $"Logging output 'file' could not initialize ({code}).", ["inner"] = false, ["dataCount"] = 0 };
            Require(JsonNode.DeepEquals(expected, result["error"]), "safe error");
        }
    }

    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
