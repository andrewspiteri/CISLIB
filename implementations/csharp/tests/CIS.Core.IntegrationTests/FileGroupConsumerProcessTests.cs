using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileGroupConsumerProcessTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    public TestContext TestContext { get; set; } = null!;
    private sealed class Child : IAsyncDisposable
    {
        internal readonly Process Process;
        private readonly Task<string> errors;
        private readonly TestContext context;
        private readonly string mode, hostHash, coreHash, parentDirectory;
        private readonly Dictionary<string, string?> parent;
        private bool finished;

        internal Child(TestContext context, string mode, string path, string cwd)
        {
            this.context = context; this.mode = mode; hostHash = Hash(Host); coreHash = Hash(Core);
            parentDirectory = Directory.GetCurrentDirectory(); parent = ParentEnvironment();
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true) };
            foreach (string arg in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-runtime-group", mode, path }) { start.ArgumentList.Add(arg); }
            start.Environment.Remove("DOTNET_SYSTEM_IO_DISABLEFILELOCKING");
            Process = Process.Start(start)!; errors = Process.StandardError.ReadToEndAsync();
            context.WriteLine($"Physical group child: {Process.Id}; {mode}; {path}; cwd={cwd}; fixture={hostHash}; core={coreHash}");
        }
        internal async Task<JsonObject> Read()
        {
            string? line = await Process.StandardOutput.ReadLineAsync().WaitAsync(Limit); Assert.IsNotNull(line, "Child ended before observation."); Assert.IsFalse(line.StartsWith('\uFEFF')); context.WriteLine(line);
            JsonObject value = JsonNode.Parse(line)!.AsObject();
            Assert.AreEqual(Process.Id, value["pid"]!.GetValue<int>()); Assert.AreEqual(".NET 10.0.12", (string?)value["runtime"]); Assert.AreEqual(FixtureProcessChecks.Family, (string?)value["os"]);
            Assert.AreEqual(hostHash, (string?)value["fixtureSha256"]); Assert.AreEqual(coreHash, (string?)value["coreSha256"]);
            if ((string?)value["status"] is "ready" or "appended" or "released" or "completed") { Inventory((string)value["capturedPath"]!, mode.EndsWith("human", StringComparison.Ordinal) ? "human" : "jsonl"); }
            return value;
        }
        internal async Task<JsonObject> Command(string command) { await Process.StandardInput.WriteLineAsync(command); await Process.StandardInput.FlushAsync(); return await Read(); }
        internal async Task Finish()
        {
            await Process.WaitForExitAsync().WaitAsync(Limit); Assert.AreEqual(0, Process.ExitCode);
            Assert.AreEqual("", await errors.WaitAsync(Limit)); Assert.AreEqual("", await Process.StandardOutput.ReadToEndAsync().WaitAsync(Limit)); finished = true;
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!Process.HasExited) { Process.Kill(entireProcessTree: true); }
                await Process.WaitForExitAsync().WaitAsync(Limit);
                if (!finished) { Assert.AreEqual("", await errors.WaitAsync(Limit)); Assert.AreEqual("", await Process.StandardOutput.ReadToEndAsync().WaitAsync(Limit)); }
                Assert.AreEqual(hostHash, Hash(Host)); Assert.AreEqual(coreHash, Hash(Core)); Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
                Dictionary<string, string?> after = ParentEnvironment(); CollectionAssert.AreEquivalent(parent.Keys.ToArray(), after.Keys.ToArray()); Assert.IsTrue(parent.All(p => after[p.Key] == p.Value));
                context.WriteLine($"Group child joined: {Process.Id}; exit={Process.ExitCode}");
            }
            finally { Process.Dispose(); }
        }
    }

    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task LiveProcessesExcludeSameGroupAndAppendIndependentlyToDistinctGroups(string format)
    {
        using var root = new TemporaryDirectory(); string a = Path.Combine(root.Path, "A.jsonl"), b = Path.Combine(root.Path, "B.jsonl");
        await using var first = new Child(TestContext, "hold-" + format, a, root.Path); JsonObject readyA = await first.Read(); Validate(readyA, "ready", a, 1);
        byte[] prior = ReadShared(a), inventory = File.ReadAllBytes(a + ".cis.inventory.json");
        await using (var blocked = new Child(TestContext, "probe-" + format, a, root.Path)) { Validate(await blocked.Read(), "failed", a, 0, "FileOwnershipUnavailable"); await blocked.Finish(); }
        await using (var alias = new Child(TestContext, "probe-" + format, Path.Combine(root.Path, ".", "A.jsonl"), root.Path)) { Validate(await alias.Read(), "failed", a, 0, "FileOwnershipUnavailable"); await alias.Finish(); }
        CollectionAssert.AreEqual(prior, ReadShared(a)); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(a + ".cis.inventory.json"));
        await using var second = new Child(TestContext, "hold-" + format, b, root.Path); Validate(await second.Read(), "ready", b, 1);
        Assert.AreNotEqual((string?)JsonNode.Parse(inventory)!["groupId"], (string?)JsonNode.Parse(Inventory(b, format))!["groupId"]);
        Validate(await first.Command("append"), "appended", a, 2); FileGroupConsumerExpectations.Records(ReadShared(b), format == "human", "B.jsonl", [("group.initial", second.Process.Id)]);
        Validate(await second.Command("append"), "appended", b, 2);
        FileGroupConsumerExpectations.Records(ReadShared(a), format == "human", "A.jsonl", [("group.initial", first.Process.Id), ("group.after.contender", first.Process.Id)]);
        FileGroupConsumerExpectations.Records(ReadShared(b), format == "human", "B.jsonl", [("group.initial", second.Process.Id), ("group.after.contender", second.Process.Id)]);
        Validate(await first.Command("release"), "released", a, 2); await first.Finish(); Validate(await second.Command("release"), "released", b, 2); await second.Finish();
        await using var restarted = new Child(TestContext, "probe-" + format, a, root.Path); Validate(await restarted.Read(), "completed", a, 1); await restarted.Finish();
        FileGroupConsumerExpectations.Records(ReadShared(a), format == "human", "A.jsonl", [("group.initial", first.Process.Id), ("group.after.contender", first.Process.Id), ("group.initial", restarted.Process.Id)]); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(a + ".cis.inventory.json"));
    }
    [TestMethod]
    public async Task KilledRuntimeReleasesOwnershipAndNextProcessAppends()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "killed.jsonl"); await using var first = new Child(TestContext, "hold-jsonl", path, root.Path); Validate(await first.Read(), "ready", path, 1);
        byte[] inventory = File.ReadAllBytes(path + ".cis.inventory.json"); first.Process.Kill(entireProcessTree: true); await first.Process.WaitForExitAsync().WaitAsync(Limit); Assert.AreNotEqual(0, first.Process.ExitCode);
        await using var next = new Child(TestContext, "probe-jsonl", path, root.Path); Validate(await next.Read(), "completed", path, 1); await next.Finish();
        FileGroupConsumerExpectations.Records(ReadShared(path), false, "killed.jsonl", [("group.initial", first.Process.Id), ("group.initial", next.Process.Id)]); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json"));
    }
    [TestMethod]
    public async Task RelativeUnicodePathIsPreparedBeforeIoAndSurvivesLaterDirectoryChange()
    {
        using var root = new TemporaryDirectory(); string origin = Path.Combine(root.Path, "origin"), later = Path.Combine(root.Path, "later"); Directory.CreateDirectory(origin); Directory.CreateDirectory(later);
        string relative = Path.Combine("missing", "\u03a9-\U0001f642-e\u0301.jsonl"), path = Path.Combine(origin, relative);
        await using var child = new Child(TestContext, "capture-jsonl", relative, origin); Validate(await child.Read(), "prepared", path, 0); Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(path))); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(later).Count());
        JsonObject actual = await child.Command("start:" + later); Validate(actual, "completed", path, 1); Assert.AreEqual(later, actual["cwd"]!.GetValue<string>()); await child.Finish();
        FileGroupConsumerExpectations.Records(ReadShared(path), false, Path.GetFileName(path), [("group.initial", child.Process.Id)]); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(later).Count());
    }
    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task FormatSwitchFailsWithoutChangingExistingActiveOrInventory(string format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "format.jsonl");
        await using (var seed = new Child(TestContext, "probe-" + format, path, root.Path)) { Validate(await seed.Read(), "completed", path, 1); await seed.Finish(); }
        byte[] active = ReadShared(path), inventory = File.ReadAllBytes(path + ".cis.inventory.json");
        await using var rejected = new Child(TestContext, "probe-" + (format == "jsonl" ? "human" : "jsonl"), path, root.Path); Validate(await rejected.Read(), "failed", path, 0, "FileInventoryIncompatible"); await rejected.Finish();
        CollectionAssert.AreEqual(active, ReadShared(path)); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json"));
    }
    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task MissingInventoryRejectsExistingBytesAndReleasesThePathLease(string format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "missing.jsonl"); byte[] before = Encoding.UTF8.GetBytes("{\"prior\":true}\n"); File.WriteAllBytes(path, before);
        for (int attempt = 0; attempt < 2; attempt++) { await using var rejected = new Child(TestContext, "probe-" + format, path, root.Path); Validate(await rejected.Read(), "failed", path, 0, "FileInventoryMissing"); await rejected.Finish(); CollectionAssert.AreEqual(before, ReadShared(path)); }
        using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); Assert.IsTrue(lease.CanWrite); Assert.IsFalse(File.Exists(path + ".cis.inventory.json"));
    }
    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task EmptyExistingGroupsPreserveTheirFormatAndIdentity(string format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "empty.log"); File.WriteAllBytes(path, []);
        var inventory = new JsonObject
        {
            ["schema"] = 1,
            ["groupId"] = new string('2', 32),
            ["format"] = format,
            ["encoding"] = "utf-8",
            ["bom"] = false,
            ["newline"] = "lf",
            ["segments"] = new JsonArray(new JsonObject { ["id"] = 1, ["fileName"] = "empty.log", ["state"] = "active" })
        };
        byte[] before = Encoding.UTF8.GetBytes(inventory.ToJsonString()); File.WriteAllBytes(path + ".cis.inventory.json", before);
        await Probe("probe-" + (format == "human" ? "jsonl" : "human"), path, root.Path, "failed", "FileInventoryIncompatible");
        Assert.AreEqual(0L, new FileInfo(path).Length); CollectionAssert.AreEqual(before, File.ReadAllBytes(path + ".cis.inventory.json"));
        JsonObject matching = await Probe("probe-" + format, path, root.Path);
        FileGroupConsumerExpectations.Records(ReadShared(path), format == "human", Path.GetFileName(path), [("group.initial", matching["pid"]!.GetValue<int>())]);
        CollectionAssert.AreEqual(before, Inventory(path, format));
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("active")]
    [DataRow("inventory")]
    [DataRow("lease")]
    public async Task ActualDeniedOperationsFailSafelyAndRestoreOwnedPermissions(string kind)
    {
        using var root = new TemporaryDirectory(); string parent = Path.Combine(root.Path, "denied"); Directory.CreateDirectory(parent);
        string path = kind == "directory" ? Path.Combine(parent, "missing", "active.jsonl") : Path.Combine(parent, "active.jsonl");
        string keep = Path.Combine(parent, "keep.txt"); File.WriteAllText(keep, "keep");
        if (kind != "directory") { await Probe("probe-jsonl", path, root.Path); }
        string target = kind switch { "directory" => parent, "inventory" => path + ".cis.inventory.json", "lease" => path + ".cis.lock", _ => path };
        var before = Directory.GetFiles(parent).ToDictionary(p => p, File.ReadAllBytes, StringComparer.Ordinal);
        using (var denied = new OwnedFilePermissions(root.Path, target, OperatingSystem.IsWindows() ? kind == "inventory" ? FileSystemRights.ReadData : FileSystemRights.Write : default,
            kind == "inventory" ? 0 : kind == "directory" ? UnixFileMode.UserRead | UnixFileMode.UserExecute : UnixFileMode.UserRead))
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            {
                if (kind == "directory") { Directory.CreateDirectory(Path.Combine(target, "missing")); }
                else { using var attempt = new FileStream(target, FileMode.Open, kind == "inventory" ? FileAccess.Read : FileAccess.ReadWrite, FileShare.ReadWrite); }
            });
            TestContext.WriteLine("Actual group OS operation denied: " + kind);
            JsonObject failed = await Probe("probe-jsonl", path, root.Path, "failed", kind == "inventory" ? "FileInventoryInitializationFailed" : "FileInitializationFailed");
            if (kind == "active") { Assert.IsTrue(failed["leaseReleased"]!.GetValue<bool>()); }
        }
        foreach ((string file, byte[] bytes) in before) { CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file)); }
        TestContext.WriteLine("Owned group permissions restored: " + kind);
        await Probe("probe-jsonl", path, root.Path); Assert.AreEqual("keep", File.ReadAllText(keep));
    }

    [TestMethod]
    [DataRow("joined")]
    [DataRow("borrowedDisposes")]
    [DataRow("error")]
    [DataRow("capturedPath")]
    [DataRow("factories")]
    [DataRow("leaseReleased")]
    public async Task OriginalGroupReaderCorruptionsAreRejectedAfterPhysicalPositive(string field)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        string code;
        if (field == "leaseReleased") { await Probe("probe-jsonl", path, root.Path); code = "FileInventoryIncompatible"; }
        else { File.WriteAllText(path + ".cis.lock", "unrelated"); code = "FileOwnershipUnavailable"; }
        JsonObject report = await Probe(field == "leaseReleased" ? "probe-human" : "probe-jsonl", path, root.Path, "failed", code);
        switch (field)
        {
            case "joined": report[field] = false; break;
            case "borrowedDisposes" or "factories": report[field] = 1; break;
            case "error": report["error"]!["message"] = "private-path"; break;
            case "capturedPath": report[field] = "wrong-path"; break;
            case "leaseReleased": report[field] = false; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Validate(report, "failed", path, 0, code));
        TestContext.WriteLine("Rejected original group control: " + field);
    }

    private async Task<JsonObject> Probe(string mode, string path, string cwd, string status = "completed", string? code = null)
    {
        await using var child = new Child(TestContext, mode, path, cwd); JsonObject report = await child.Read();
        Validate(report, status, path, code is null ? 1 : 0, code); await child.Finish(); return report;
    }


    private static string Core => Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll");
    private static Dictionary<string, string?> ParentEnvironment() => Environment.GetEnvironmentVariables().Keys.Cast<string>().ToDictionary(k => k, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
    private static byte[] Inventory(string path, string format)
    {
        byte[] raw = File.ReadAllBytes(path + ".cis.inventory.json"); Assert.IsFalse(raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        JsonObject inventory = JsonNode.Parse(new UTF8Encoding(false, true).GetString(raw))!.AsObject();
        string group = (string)inventory["groupId"]!; Assert.IsTrue(Guid.TryParseExact(group, "N", out Guid id) && id != Guid.Empty); Assert.AreEqual(id.ToString("N"), group);
        var wanted = new JsonObject
        {
            ["schema"] = 1,
            ["groupId"] = group,
            ["format"] = format,
            ["encoding"] = "utf-8",
            ["bom"] = false,
            ["newline"] = "lf",
            ["segments"] = new JsonArray(new JsonObject { ["id"] = 1, ["fileName"] = Path.GetFileName(path), ["state"] = "active" })
        };
        Assert.IsTrue(JsonNode.DeepEquals(wanted, inventory)); return raw;
    }
    private static void Validate(JsonObject actual, string status, string path, int count, string? code = null)
    {
        FileGroupConsumerExpectations.Validate(actual, status, path, count, code);
        if (status is "prepared" or "failed") { return; }
        var expected = new List<(string Event, int Pid)> { ("group.initial", actual["pid"]!.GetValue<int>()) };
        for (int i = 1; i < count; i++) { expected.Add(("group.after.contender", actual["pid"]!.GetValue<int>())); }
        FileGroupConsumerExpectations.Records(Convert.FromBase64String((string)actual["consoleBytes"]!), false, Path.GetFileName(path), expected.ToArray());
        Assert.AreEqual(Hash(path + ".cis.inventory.json"), (string?)actual["inventorySha256"]);
        CollectionAssert.AreEqual(ReadShared(path), Convert.FromBase64String((string)actual["fileBytes"]!));
    }

    private static string Host => Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static byte[] ReadShared(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var result = new MemoryStream(); file.CopyTo(result); return result.ToArray(); }
}
