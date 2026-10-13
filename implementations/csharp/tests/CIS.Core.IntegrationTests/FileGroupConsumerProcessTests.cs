using System.Diagnostics;
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
    private sealed class Child : IAsyncDisposable
    {
        internal readonly Process Process; private readonly Task<string> errors;
        internal Child(string mode, string path, string cwd)
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true) };
            foreach (string arg in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-runtime-group", mode, path }) { start.ArgumentList.Add(arg); }
            Process = Process.Start(start)!; errors = Process.StandardError.ReadToEndAsync();
        }
        internal async Task<JsonObject> Read()
        {
            string? line = await Process.StandardOutput.ReadLineAsync().WaitAsync(Limit); Assert.IsNotNull(line, "Child ended before observation."); JsonObject value = JsonNode.Parse(line)!.AsObject();
            Assert.AreEqual(Process.Id, value["pid"]!.GetValue<int>()); Assert.AreEqual(".NET 10.0.12", value["runtime"]!.GetValue<string>()); Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", value["os"]!.GetValue<string>());
            Assert.AreEqual(Hash(Host), value["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll")), value["coreSha256"]!.GetValue<string>()); return value;
        }
        internal async Task<JsonObject> Command(string command) { await Process.StandardInput.WriteLineAsync(command); await Process.StandardInput.FlushAsync(); return await Read(); }
        internal async Task Finish() { await Process.WaitForExitAsync().WaitAsync(Limit); Assert.AreEqual(0, Process.ExitCode); Assert.AreEqual("", await errors.WaitAsync(Limit)); Assert.AreEqual("", await Process.StandardOutput.ReadToEndAsync().WaitAsync(Limit)); }
        public async ValueTask DisposeAsync() { try { if (!Process.HasExited) { Process.Kill(entireProcessTree: true); } await Process.WaitForExitAsync().WaitAsync(Limit); await errors.WaitAsync(Limit); } finally { Process.Dispose(); } }
    }
    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task LiveProcessesExcludeSameGroupAndAppendIndependentlyToDistinctGroups(string format)
    {
        using var root = new TemporaryDirectory(); string a = Path.Combine(root.Path, "A.jsonl"), b = Path.Combine(root.Path, "B.jsonl");
        await using var first = new Child("hold-" + format, a, root.Path); JsonObject readyA = await first.Read(); FileGroupConsumerExpectations.Validate(readyA, "ready", a, 1);
        byte[] prior = ReadShared(a), inventory = File.ReadAllBytes(a + ".cis.inventory.json");
        await using (var blocked = new Child("probe-" + format, a, root.Path)) { FileGroupConsumerExpectations.Validate(await blocked.Read(), "failed", a, 0, "FileOwnershipUnavailable"); await blocked.Finish(); }
        CollectionAssert.AreEqual(prior, ReadShared(a)); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(a + ".cis.inventory.json"));
        await using var second = new Child("hold-" + format, b, root.Path); FileGroupConsumerExpectations.Validate(await second.Read(), "ready", b, 1);
        FileGroupConsumerExpectations.Validate(await first.Command("append"), "appended", a, 2); FileGroupConsumerExpectations.Records(ReadShared(b), format == "human", "B.jsonl", [("group.initial", second.Process.Id)]);
        FileGroupConsumerExpectations.Validate(await second.Command("append"), "appended", b, 2);
        FileGroupConsumerExpectations.Records(ReadShared(a), format == "human", "A.jsonl", [("group.initial", first.Process.Id), ("group.after.contender", first.Process.Id)]);
        FileGroupConsumerExpectations.Records(ReadShared(b), format == "human", "B.jsonl", [("group.initial", second.Process.Id), ("group.after.contender", second.Process.Id)]);
        FileGroupConsumerExpectations.Validate(await first.Command("release"), "released", a, 2); await first.Finish(); FileGroupConsumerExpectations.Validate(await second.Command("release"), "released", b, 2); await second.Finish();
        await using var restarted = new Child("probe-" + format, a, root.Path); FileGroupConsumerExpectations.Validate(await restarted.Read(), "completed", a, 1); await restarted.Finish();
        FileGroupConsumerExpectations.Records(ReadShared(a), format == "human", "A.jsonl", [("group.initial", first.Process.Id), ("group.after.contender", first.Process.Id), ("group.initial", restarted.Process.Id)]); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(a + ".cis.inventory.json"));
    }
    [TestMethod]
    public async Task KilledRuntimeReleasesOwnershipAndNextProcessAppends()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "killed.jsonl"); await using var first = new Child("hold-jsonl", path, root.Path); FileGroupConsumerExpectations.Validate(await first.Read(), "ready", path, 1);
        byte[] inventory = File.ReadAllBytes(path + ".cis.inventory.json"); first.Process.Kill(entireProcessTree: true); await first.Process.WaitForExitAsync().WaitAsync(Limit);
        await using var next = new Child("probe-jsonl", path, root.Path); FileGroupConsumerExpectations.Validate(await next.Read(), "completed", path, 1); await next.Finish();
        FileGroupConsumerExpectations.Records(ReadShared(path), false, "killed.jsonl", [("group.initial", first.Process.Id), ("group.initial", next.Process.Id)]); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json"));
    }
    [TestMethod]
    public async Task RelativeUnicodePathIsPreparedBeforeIoAndSurvivesLaterDirectoryChange()
    {
        using var root = new TemporaryDirectory(); string origin = Path.Combine(root.Path, "origin"), later = Path.Combine(root.Path, "later"); Directory.CreateDirectory(origin); Directory.CreateDirectory(later);
        string relative = Path.Combine("missing", "\u03a9-\U0001f642-e\u0301.jsonl"), path = Path.Combine(origin, relative);
        await using var child = new Child("capture-jsonl", relative, origin); FileGroupConsumerExpectations.Validate(await child.Read(), "prepared", path, 0); Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(path))); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(later).Count());
        JsonObject actual = await child.Command("start:" + later); FileGroupConsumerExpectations.Validate(actual, "completed", path, 1); Assert.AreEqual(later, actual["cwd"]!.GetValue<string>()); await child.Finish();
        FileGroupConsumerExpectations.Records(ReadShared(path), false, Path.GetFileName(path), [("group.initial", child.Process.Id)]); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(later).Count());
    }
    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task FormatSwitchFailsWithoutChangingExistingActiveOrInventory(string format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "format.jsonl");
        await using (var seed = new Child("probe-" + format, path, root.Path)) { FileGroupConsumerExpectations.Validate(await seed.Read(), "completed", path, 1); await seed.Finish(); }
        byte[] active = ReadShared(path), inventory = File.ReadAllBytes(path + ".cis.inventory.json");
        await using var rejected = new Child("probe-" + (format == "jsonl" ? "human" : "jsonl"), path, root.Path); FileGroupConsumerExpectations.Validate(await rejected.Read(), "failed", path, 0, "FileInventoryIncompatible"); await rejected.Finish();
        CollectionAssert.AreEqual(active, ReadShared(path)); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json"));
    }
    [TestMethod]
    [DataRow("jsonl")]
    [DataRow("human")]
    public async Task MissingInventoryRejectsExistingBytesAndReleasesThePathLease(string format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "missing.jsonl"); byte[] before = Encoding.UTF8.GetBytes("{\"prior\":true}\n"); File.WriteAllBytes(path, before);
        for (int attempt = 0; attempt < 2; attempt++) { await using var rejected = new Child("probe-" + format, path, root.Path); FileGroupConsumerExpectations.Validate(await rejected.Read(), "failed", path, 0, "FileInventoryMissing"); await rejected.Finish(); CollectionAssert.AreEqual(before, ReadShared(path)); }
        using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); Assert.IsTrue(lease.CanWrite); Assert.IsFalse(File.Exists(path + ".cis.inventory.json"));
    }
    private static string Host => Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static byte[] ReadShared(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var result = new MemoryStream(); file.CopyTo(result); return result.ToArray(); }
}
