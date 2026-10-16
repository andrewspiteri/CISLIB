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
    public TestContext TestContext { get; set; } = null!;
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
        JsonObject switched = await Probe(format == "jsonl" ? "human" : "jsonl", path, root.Path); Validate(switched, "failed"); AssertSafe(switched, "FileInventoryIncompatible");
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
            JsonObject ready = JsonNode.Parse((await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit))!)!.AsObject(); Validate(ready, "acquired"); ValidateAcquired(ready, "jsonl", path);
            Assert.IsTrue(ready["created"]!.GetValue<bool>()); Assert.AreEqual(0L, ready["length"]!.GetValue<long>());
            Assert.AreEqual(1L, ready["segmentId"]!.GetValue<long>()); Assert.AreEqual(1, ready["segments"]!.GetValue<int>());
            byte[] before = File.ReadAllBytes(Metadata(path)); JsonObject blocked = await Probe("jsonl", path, root.Path); Validate(blocked, "failed"); AssertSafe(blocked, "FileOwnershipUnavailable");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Metadata(path))); JsonObject distinct = await Probe("human", Path.Combine(root.Path, "distinct.log"), root.Path); Validate(distinct, "acquired");
            Assert.AreNotEqual((string?)ready["groupId"], (string?)distinct["groupId"]); Assert.AreEqual(holder.Id, ready["pid"]!.GetValue<int>());
            await holder.StandardInput.WriteLineAsync("release"); await holder.StandardInput.FlushAsync();
            Validate(JsonNode.Parse((await holder.StandardOutput.ReadLineAsync().WaitAsync(Limit))!)!.AsObject(), "released");
            await holder.WaitForExitAsync().WaitAsync(Limit); Assert.AreEqual(0, holder.ExitCode); Assert.AreEqual("", await stderr.WaitAsync(Limit));
            JsonObject reopened = await Probe("jsonl", path, root.Path); Validate(reopened, "acquired");
            Assert.AreEqual(ready["groupId"]!.GetValue<string>(), reopened["groupId"]!.GetValue<string>()); CollectionAssert.AreEqual(before, File.ReadAllBytes(Metadata(path)));
        }
        finally
        { if (!holder.HasExited) { holder.Kill(entireProcessTree: true); } await holder.WaitForExitAsync().WaitAsync(Limit); await stderr.WaitAsync(Limit); }
    }


    [TestMethod]
    [DataRow("missing-jsonl")]
    [DataRow("missing-human")]
    [DataRow("missing-partial")]
    [DataRow("format")]
    [DataRow("encoding")]
    [DataRow("bom")]
    [DataRow("newline")]
    [DataRow("schema-kind")]
    [DataRow("id-kind")]
    [DataRow("duplicate-key")]
    [DataRow("invalid-utf8")]
    [DataRow("bom-prefix")]
    [DataRow("too-large")]
    [DataRow("too-deep")]
    [DataRow("valid-members")]
    [DataRow("escaped-member")]
    [DataRow("missing-member")]
    [DataRow("count-1000")]
    [DataRow("count-1001")]
    [DataRow("metadata-directory")]
    public async Task OriginalPhysicalInventoryCasesPreserveEveryPreimage(string mode)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "members", "active.log"), later = Path.Combine(root.Path, "later");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(later);
        byte[] active = Encoding.UTF8.GetBytes(mode switch { "missing-jsonl" => "{\"schemaVersion\":1}\n", "missing-human" => "INFO prior human record\n", "missing-partial" => "partial-json", _ => "active\n" });
        File.WriteAllBytes(path, active);
        string group = "0123456789abcdef0123456789abcdef";
        string Closed(int id, string state = "closed") => ".cis-log-" + group + "-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + "." + state;
        JsonObject Segment(long id, string name, string state) => new() { ["id"] = id, ["fileName"] = name, ["state"] = state };
        var entries = new JsonArray(Segment(1, Path.GetFileName(path), "active"));
        var doc = new JsonObject { ["schema"] = 1, ["groupId"] = group, ["format"] = "jsonl", ["encoding"] = "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = entries };
        var retained = new Dictionary<string, byte[]>();
        string outside = Path.Combine(root.Path, "unrelated.txt"); File.WriteAllText(outside, "keep-unrelated"); retained[outside] = File.ReadAllBytes(outside);
        string expected = "failed", code = "FileInventoryIncompatible"; int count = 1;
        if (mode.StartsWith("missing-", StringComparison.Ordinal) && mode != "missing-member") { code = "FileInventoryMissing"; }
        else if (mode == "metadata-directory") { Directory.CreateDirectory(Metadata(path)); string kept = Path.Combine(Metadata(path), "keep.txt"); File.WriteAllText(kept, "keep"); retained[kept] = File.ReadAllBytes(kept); code = "FileInventoryInitializationFailed"; }
        else
        {
            switch (mode)
            {
                case "format": doc["format"] = "human"; break;
                case "encoding": doc["encoding"] = "utf-16"; break;
                case "bom": doc["bom"] = true; break;
                case "newline": doc["newline"] = "crlf"; break;
                case "schema-kind": doc["schema"] = "1"; break;
                case "id-kind": entries[0]!["id"] = "1"; break;
                case "too-deep": JsonNode nested = JsonValue.Create(1)!; for (int i = 0; i < 14; i++) { nested = new JsonArray(nested); } doc["future"] = nested; break;
            }
            if (mode.EndsWith("-members", StringComparison.Ordinal) || mode == "escaped-member" || mode == "missing-member")
            {
                count = 3; entries.Clear();
                foreach ((int id, string state) in new[] { (1, "closed"), (2, "damaged") })
                {
                    string name = Closed(id, state), file = Path.Combine(Path.GetDirectoryName(path)!, name);
                    byte[] bytes = Encoding.UTF8.GetBytes("keep-" + state); File.WriteAllBytes(file, bytes); retained[file] = bytes;
                    entries.Add(Segment(id, name, state));
                }
                entries.Add(Segment(3, Path.GetFileName(path), "active"));
                if (mode == "valid-members") { expected = "acquired"; }
                if (mode == "escaped-member") { entries[0]!["fileName"] = "../unrelated.txt"; }
                if (mode == "missing-member") { string missing = Path.Combine(Path.GetDirectoryName(path)!, Closed(1)); File.Delete(missing); retained.Remove(missing); }
            }
            if (mode.StartsWith("count-", StringComparison.Ordinal))
            {
                int closedCount = mode == "count-1000" ? 1000 : 1001; count = closedCount + 1; entries.Clear();
                for (int id = 1; id <= closedCount; id++) { string name = Closed(id), file = Path.Combine(Path.GetDirectoryName(path)!, name); File.WriteAllText(file, "keep"); retained[file] = File.ReadAllBytes(file); entries.Add(Segment(id, name, "closed")); }
                entries.Add(Segment(count, Path.GetFileName(path), "active"));
                if (closedCount == 1000) { expected = "acquired"; }
            }
            string text = doc.ToJsonString();
            if (mode == "duplicate-key") { text = text.Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal); }
            byte[] raw = mode switch
            {
                "invalid-utf8" => [.. Encoding.UTF8.GetBytes(text[..^1] + ",\"future\":\""), 0xff, .. Encoding.UTF8.GetBytes("\"}")],
                "bom-prefix" => [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)],
                "too-large" => Encoding.UTF8.GetBytes(new string(' ', 2 * 1024 * 1024 + 1)),
                _ => Encoding.UTF8.GetBytes(text)
            };
            File.WriteAllBytes(Metadata(path), raw);
        }
        JsonObject report = await Probe("jsonl", path, later); Validate(report, expected);
        if (expected == "failed") { AssertSafe(report, code); }
        else { Assert.AreEqual(count, report["segments"]!.GetValue<int>()); Assert.AreEqual((long)count, report["segmentId"]!.GetValue<long>()); Assert.AreEqual(group, report["groupId"]!.GetValue<string>()); Assert.IsFalse(report["created"]!.GetValue<bool>()); }
        CollectionAssert.AreEqual(active, File.ReadAllBytes(path));
        foreach ((string file, byte[] bytes) in retained) { CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file)); }
        Assert.AreEqual(0, Directory.GetFileSystemEntries(later).Length);
    }

    [TestMethod]
    [DataRow("status")]
    [DataRow("runtime")]
    [DataRow("coreSha256")]
    [DataRow("groupId")]
    [DataRow("error")]
    public async Task OriginalInventoryReaderCorruptionsAreRejectedAfterPhysicalPositive(string field)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        File.WriteAllText(path + ".cis.lock", "unrelated");
        JsonObject report = await Probe("jsonl", path, root.Path); Validate(report, "failed"); AssertSafe(report, "FileOwnershipUnavailable");
        if (field == "error") { report["error"]!["inner"] = true; } else { report[field] = "damaged"; }
        string reason = field switch { "status" => "status", "runtime" => "runtime", "coreSha256" => "Core hash", "groupId" => "failed inventory facts", _ => "safe error" };
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => { Validate(report, "failed"); AssertSafe(report, "FileOwnershipUnavailable"); }).Message);
        TestContext.WriteLine("Rejected original inventory control: " + field);
    }

    private async Task<JsonObject> Probe(string format, string path, string cwd)
    {
        var preimages = new Dictionary<string, byte[]>();
        foreach (string file in new[] { path, Metadata(path) }) { if (File.Exists(file)) { preimages[file] = ReadShared(file); } }
        (JsonObject report, _) = await FixtureProcessChecks.RunAsync(["file-inventory", format, path], cwd, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: Limit, flatReport: true);
        foreach ((string file, byte[] bytes) in preimages) { CollectionAssert.AreEqual(bytes, ReadShared(file)); }
        if ((string?)report["status"] == "acquired") { ValidateAcquired(report, format, path); }
        return report;
    }

    private static void ValidateAcquired(JsonObject report, string format, string path)
    {
        byte[] raw = File.ReadAllBytes(Metadata(path)); Assert.IsFalse(raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        JsonObject stored = JsonNode.Parse(new UTF8Encoding(false, true).GetString(raw))!.AsObject();
        Assert.AreEqual(1, stored["schema"]!.GetValue<int>()); Assert.AreEqual(format, (string?)stored["format"]);
        Assert.AreEqual("utf-8", (string?)stored["encoding"]); Assert.AreEqual("lf", (string?)stored["newline"]); Assert.IsFalse(stored["bom"]!.GetValue<bool>());
        Assert.AreEqual((string?)report["groupId"], (string?)stored["groupId"]); Assert.AreEqual(Hash(Metadata(path)), (string?)report["inventorySha256"]);
        Assert.AreEqual(stored["segments"]!.AsArray().Count, report["segments"]!.GetValue<int>());
        Assert.AreEqual(stored["segments"]!.AsArray()[^1]!["id"]!.GetValue<long>(), report["segmentId"]!.GetValue<long>());
        Assert.AreEqual(format, (string?)report["format"]); Assert.AreEqual(new FileInfo(path).Length, report["length"]!.GetValue<long>());
        Assert.IsTrue(Guid.TryParseExact((string?)report["groupId"], "N", out Guid group) && group != Guid.Empty);
        Assert.AreEqual(group.ToString("N"), (string?)report["groupId"]);
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(Metadata(path)) + ".*.tmp").Length);
    }

    private static void Validate(JsonObject result, string status)
    {
        Require(status == result["status"]!.GetValue<string>(), "status"); Require(result["runtime"]!.GetValue<string>() == ".NET 10.0.12", "runtime");
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", result["os"]!.GetValue<string>());
        Assert.AreEqual(Hash(Host), result["fixtureSha256"]!.GetValue<string>()); Require(Hash(Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll")) == result["coreSha256"]!.GetValue<string>(), "Core hash");
        if (status != "acquired") { foreach (string field in new[] { "length", "created", "groupId", "segmentId", "segments", "format", "inventorySha256" }) { Require(result[field] is null, "failed inventory facts"); } }
        if (status != "failed") { Assert.IsNull(result["error"]); }
    }
    private static void AssertSafe(JsonObject result, string code)
    {
        JsonObject error = result["error"]!.AsObject(); Assert.AreEqual("file", error["output"]!.GetValue<string>()); Assert.AreEqual(code, error["code"]!.GetValue<string>());
        Require(!error["inner"]!.GetValue<bool>(), "safe error"); Assert.AreEqual(0, error["dataCount"]!.GetValue<int>()); Assert.AreEqual($"Logging output 'file' could not initialize ({code}).", error["message"]!.GetValue<string>());
    }
    private static byte[] ReadShared(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray(); }
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
