using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileInventoryProcessTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static string Host => Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
    private static string Metadata(string path) => path + ".cis.inventory.json";

    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task SeparateProcessRestartUsesExistingFormatAndIdentityWithoutChangingContent(string format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "missing", "\u03a9-\U0001f642.log");
        JsonObject first = await Probe(format, path, root.Path); Validate(first, "acquired");
        Assert.IsTrue(first["created"]!.GetValue<bool>()); Assert.AreEqual(format, first["format"]!.GetValue<string>());
        Assert.AreEqual(1L, first["segmentId"]!.GetValue<long>()); Assert.AreEqual(1, first["segments"]!.GetValue<int>());
        Assert.IsTrue(Guid.TryParseExact(first["groupId"]!.GetValue<string>(), "N", out Guid id)); Assert.AreNotEqual(Guid.Empty, id);
        byte[] inventory = File.ReadAllBytes(Metadata(path)), active = Encoding.UTF8.GetBytes("pre-existing records\n"); File.WriteAllBytes(path, active);
        JsonObject second = await Probe(format, path, root.Path); Validate(second, "acquired");
        Assert.IsFalse(second["created"]!.GetValue<bool>()); Assert.AreEqual(first["groupId"]!.GetValue<string>(), second["groupId"]!.GetValue<string>());
        Assert.AreEqual(first["inventorySha256"]!.GetValue<string>(), second["inventorySha256"]!.GetValue<string>());
        Assert.AreEqual(active.Length, second["length"]!.GetValue<long>());
        CollectionAssert.AreEqual(inventory, File.ReadAllBytes(Metadata(path))); CollectionAssert.AreEqual(active, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow("missing", "FileInventoryMissing")]
    [DataRow("format", "FileInventoryIncompatible")]
    [DataRow("encoding", "FileInventoryIncompatible")]
    public async Task StartupRejectsUnsafeInventoryBeforeModifyingExistingContent(string damage, string code)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        if (damage != "missing") { Validate(await Probe(damage == "format" ? "human" : "jsonl", path, root.Path), "acquired"); }
        if (damage == "encoding")
        { JsonObject stored = JsonNode.Parse(File.ReadAllBytes(Metadata(path)))!.AsObject(); stored["encoding"] = "utf-16"; File.WriteAllText(Metadata(path), stored.ToJsonString()); }
        byte[] active = Encoding.UTF8.GetBytes("old content\n"); File.WriteAllBytes(path, active);
        byte[]? before = File.Exists(Metadata(path)) ? File.ReadAllBytes(Metadata(path)) : null;
        JsonObject failed = await Probe("jsonl", path, root.Path); Validate(failed, "failed"); AssertSafe(failed, code);
        CollectionAssert.AreEqual(active, File.ReadAllBytes(path));
        if (before is null) { Assert.IsFalse(File.Exists(Metadata(path))); } else { CollectionAssert.AreEqual(before, File.ReadAllBytes(Metadata(path))); }
        // A subsequent raw acquisition proves rollback released the actual group lease.
        ProcessResult raw = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-acquisition", "probe", path], root.Path, Limit);
        Assert.AreEqual(0, raw.ExitCode, raw.StandardError); Assert.AreEqual("acquired", JsonNode.Parse(raw.StandardOutput)!["status"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task ALostActiveSegmentDoesNotInheritOldInventoryIdentity()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "lost.jsonl");
        Validate(await Probe("jsonl", path, root.Path), "acquired"); byte[] before = File.ReadAllBytes(Metadata(path)); File.Delete(path);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            JsonObject failed = await Probe("jsonl", path, root.Path); Validate(failed, "failed"); AssertSafe(failed, "FileInventoryIncompatible");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Metadata(path))); Assert.IsFalse(File.Exists(path));
        }
    }

    [TestMethod]
    public async Task TwoProcessesHoldOneInventoryIdentityAndCannotOverwriteEachOther()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root.Path,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false, true)
        };
        foreach (string value in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-inventory", "hold-jsonl", path }) { start.ArgumentList.Add(value); }
        start.Environment.Remove("DOTNET_SYSTEM_IO_DISABLEFILELOCKING");
        using var holder = new Process { StartInfo = start }; holder.Start(); Task<string> stderr = holder.StandardError.ReadToEndAsync();
        try
        {
            JsonObject ready = JsonNode.Parse((await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit))!)!.AsObject(); Validate(ready, "acquired");
            byte[] before = File.ReadAllBytes(Metadata(path)); JsonObject blocked = await Probe("jsonl", path, root.Path); Validate(blocked, "failed"); AssertSafe(blocked, "FileOwnershipUnavailable");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Metadata(path))); Validate(await Probe("human", Path.Combine(root.Path, "distinct.log"), root.Path), "acquired");
            await holder.StandardInput.WriteLineAsync("release"); await holder.StandardInput.FlushAsync();
            Validate(JsonNode.Parse((await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit))!)!.AsObject(), "released");
            await holder.WaitForExitAsync().WaitAsync(Limit); Assert.AreEqual(0, holder.ExitCode); Assert.AreEqual("", await stderr.WaitAsync(Limit));
            JsonObject reopened = await Probe("jsonl", path, root.Path); Validate(reopened, "acquired");
            Assert.AreEqual(ready["groupId"]!.GetValue<string>(), reopened["groupId"]!.GetValue<string>()); CollectionAssert.AreEqual(before, File.ReadAllBytes(Metadata(path)));
        }
        finally
        { if (!holder.HasExited) { holder.Kill(entireProcessTree: true); } await holder.WaitForExitAsync().WaitAsync(Limit); await stderr.WaitAsync(Limit); }
    }

    private static async Task<JsonObject> Probe(string format, string path, string cwd)
    {
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-inventory", format, path], cwd, Limit,
            new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, new UTF8Encoding(false, true));
        Assert.AreEqual(0, result.ExitCode, result.StandardError); Assert.AreEqual("", result.StandardError); return JsonNode.Parse(result.StandardOutput)!.AsObject();
    }
    private static void Validate(JsonObject result, string status)
    {
        Assert.AreEqual(status, result["status"]!.GetValue<string>()); Assert.AreEqual(".NET 10.0.12", result["runtime"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", result["os"]!.GetValue<string>());
        Assert.AreEqual(Hash(Host), result["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll")), result["coreSha256"]!.GetValue<string>());
        if (status != "failed") { Assert.IsNull(result["error"]); }
    }
    private static void AssertSafe(JsonObject result, string code)
    {
        JsonObject error = result["error"]!.AsObject(); Assert.AreEqual("file", error["output"]!.GetValue<string>()); Assert.AreEqual(code, error["code"]!.GetValue<string>());
        Assert.IsFalse(error["inner"]!.GetValue<bool>()); Assert.AreEqual(0, error["dataCount"]!.GetValue<int>()); Assert.AreEqual($"Logging output 'file' could not initialize ({code}).", error["message"]!.GetValue<string>());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
