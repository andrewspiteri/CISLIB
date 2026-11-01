using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRestartProcessTests
{
#if !CIS_MUTATION_TESTS
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> ReaderPositives = new(StringComparer.Ordinal);
#endif
    public TestContext TestContext { get; set; } = null!;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [TestMethod]
    [DataRow("intact")]
    [DataRow("partial-json")]
    [DataRow("partial-utf8")]
    [DataRow("rotation-intent")]
    [DataRow("rotation-renamed")]
    [DataRow("rotation-published")]
    [DataRow("rotation-created")]
    [DataRow("recovery-intent")]
    [DataRow("recovery-renamed")]
    [DataRow("recovery-published")]
    [DataRow("recovery-created")]
    [DataRow("collision-file")]
    [DataRow("collision-directory")]
    [DataRow("unproven-gap")]
    [DataRow("ambiguous-both")]
    public async Task SeparateProducerAndRestartProcessesPreserveWholeSegmentsAndSafeFailures(string mode)
    { using var root = new TemporaryDirectory(); await Scenario(mode, root.Path); }

    [TestMethod]
    [DataRow("original")]
    [DataRow("stage")]
    [DataRow("pending")]
    [DataRow("failure-code")]
    [DataRow("failure-growth")]
    [DataRow("lease")]
    [DataRow("accounting")]
    [DataRow("archive")]
    [DataRow("neighbor")]
    [DataRow("record")]
    [DataRow("inventory")]
    [DataRow("probe")]
    public async Task OriginalRestartReaderCorruptionsRejectAfterPhysicalPositive(string key)
    {
        string mode = key.StartsWith("failure", StringComparison.Ordinal) ? "collision-file" : key == "probe" ? "rotation-published" : "recovery-intent";
        (JsonObject seed, JsonObject resume, JsonObject? probe) = await ReadPositive(mode);
        FileRestartExpectations.Validate(mode, seed, resume, probe);
        void Edit(JsonNode container, string name, Action<JsonObject> mutation)
        { JsonObject value = JsonNode.Parse(Convert.FromBase64String((string)container[name]!))!.AsObject(); mutation(value); container[name] = Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToJsonString() + "\n")); }
        const string active = FileRestartExpectations.Active;
        switch (key)
        {
            case "original": JsonObject original = JsonNode.Parse(Convert.FromBase64String((string)seed["seedRecords"]![0]!))!.AsObject(); original["event_name"] = "other"; seed["seedRecords"]![0] = Convert.ToBase64String(Encoding.UTF8.GetBytes(original.ToJsonString() + "\n")); break;
            case "stage": seed["files"]!["neighbor.bin"] = Convert.ToBase64String("changed"u8); break;
            case "pending": Edit(seed["files"]!, active + ".cis.transition.json", value => value["originalSha256"] = new string('0', 64)); break;
            case "failure-code": resume["error"]!["code"] = "Other"; break;
            case "failure-growth": resume["after"]![active] = Convert.ToBase64String("changed"u8); break;
            case "lease": resume["leaseReleased"] = false; break;
            case "accounting": resume["final"]!["written"] = 0L; break;
            case "archive": string archive = resume["after"]!.AsObject().Select(p => p.Key).Single(k => k.EndsWith("00000000000000000003.damaged", StringComparison.Ordinal)); resume["after"]![archive] = Convert.ToBase64String("changed"u8); break;
            case "neighbor": resume["after"]!["neighbor.bin"] = Convert.ToBase64String("changed"u8); break;
            case "record": Edit(resume["after"]!, active, value => value["attributes"]!["producer_pid"] = 0); break;
            case "inventory": Edit(resume["after"]!, active + ".cis.inventory.json", value => value["segments"]!.AsArray()[^1]!["id"] = 99); break;
            case "probe": probe!["after"]!["neighbor.bin"] = Convert.ToBase64String("changed"u8); break;
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => FileRestartExpectations.Validate(mode, seed, resume, probe));
        string reason = key switch { "original" or "record" => "actual record producer", "stage" => "literal stage preimage", "pending" => "pending identity", "failure-code" => "safe failure", "failure-growth" => "unchanged restart failure", "lease" => "owned process/lease", "accounting" => "current accounting", "inventory" => "next inventory", "probe" => "held probe preservation", _ => "exact restart transition" };
        Assert.AreEqual(reason, error.Message); TestContext.WriteLine("Rejected original restart control: " + key);
    }

    private async Task<(JsonObject Seed, JsonObject Resume, JsonObject? Probe)> ReadPositive(string mode)
    {
#if CIS_MUTATION_TESTS
        using var root = new TemporaryDirectory();
        return await Scenario(mode, root.Path);
#else
        string text = await ReaderPositives.GetOrAdd(mode, value => new Lazy<Task<string>>(() => CapturePositive(value))).Value;
        JsonObject reports = JsonNode.Parse(text)!.AsObject();
        return (reports["seed"]!.AsObject(), reports["resume"]!.AsObject(), reports["probe"]?.AsObject());
#endif
    }

#if !CIS_MUTATION_TESTS
    private async Task<string> CapturePositive(string mode)
    {
        (JsonObject seed, JsonObject resume, JsonObject? probe) reports;
        using (var root = new TemporaryDirectory()) { reports = await Scenario(mode, root.Path); }
        return new JsonObject { ["seed"] = reports.seed, ["resume"] = reports.resume, ["probe"] = reports.probe }.ToJsonString();
    }
#endif

    private async Task<(JsonObject Seed, JsonObject Resume, JsonObject? Probe)> Scenario(string mode, string root)
    {
        JsonObject seed; JsonObject? probe = null;
        await using (var producer = new HeldProducer(mode, root, TestContext))
        {
            seed = await producer.Read();
            if (mode is "rotation-published" or "recovery-intent") { probe = await Run(mode, "probe", root); }
            await producer.Kill();
        }
        JsonObject resume = await Run(mode, "resume", root);
        FileRestartExpectations.Validate(mode, seed, resume, probe);
        string active = Path.Combine(root, "logs", "Ω-🙂.jsonl");
        Assert.AreEqual(active, (string?)seed["path"]); Assert.AreEqual(active, (string?)resume["path"]);
        var physical = new JsonObject();
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".cis.lock", StringComparison.Ordinal)))
        { physical.Add(Path.GetRelativePath(root, file).Replace('\\', '/'), Convert.ToBase64String(File.ReadAllBytes(file))); }
        Assert.IsTrue(JsonNode.DeepEquals(physical, resume["after"]));
        CollectionAssert.AreEqual(resume["namesAfter"]!.AsArray().Select(n => (string)n!).ToArray(), Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(n => Path.GetRelativePath(root, n).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray());
        await Selector(mode, active, root, seed, resume);
        return (seed, resume, probe);
    }

    private async Task<JsonObject> Run(string mode, string phase, string root)
    {
        (JsonObject report, _) = await FixtureProcessChecks.RunAsync(["file-restart", mode, phase, root], root, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: Limit, flatReport: true);
        Assert.AreEqual(phase, (string?)report["phase"]); return report;
    }


    private async Task Selector(string mode, string active, string root, JsonObject seed, JsonObject resume)
    {
        string repository = Path.GetFullPath("../..", Path.GetDirectoryName(FixtureProcessChecks.Find("case-inventory.json"))!);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string tool = Path.Combine(repository, "implementations/csharp/eng/CIS.DependencyChecker/bin", configuration, "net10.0/CIS.DependencyChecker.dll"), beforeHash = Hash(tool), cwd = Directory.GetCurrentDirectory();
        Dictionary<string, string?> parent = Parent();
        string[] command = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", tool, "select-jsonl-inputs", active];
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", command, root, Limit, outputEncoding: new UTF8Encoding(false, true));
        TestContext.WriteLine("Restart selector child: " + child.ProcessId + "; " + string.Join(" ", command) + "; sha256=" + beforeHash); TestContext.WriteLine(child.StandardOutput); TestContext.WriteLine(child.StandardError);
        Assert.AreEqual(beforeHash, Hash(tool)); Assert.AreEqual(cwd, Directory.GetCurrentDirectory());
        Dictionary<string, string?> after = Parent(); CollectionAssert.AreEquivalent(parent.Keys.ToArray(), after.Keys.ToArray()); Assert.IsTrue(parent.All(p => after[p.Key] == p.Value));
        bool failure = mode.StartsWith("collision", StringComparison.Ordinal) || mode is "unproven-gap" or "ambiguous-both";
        Assert.AreEqual(failure ? 2 : 0, child.ExitCode);
        if (failure) { Assert.AreEqual("", child.StandardOutput); Assert.AreEqual("Cannot select a consistent stopped JSONL group." + Environment.NewLine, child.StandardError); return; }
        Assert.AreEqual("", child.StandardError);
        JsonObject inventory = JsonNode.Parse(Convert.FromBase64String((string)resume["after"]![FileRestartExpectations.Active + ".cis.inventory.json"]!))!.AsObject();
        string[] expected = inventory["segments"]!.AsArray().Where(m => (string?)m!["state"] is "active" or "closed").Select(m => Path.Combine(root, "logs", (string)m!["fileName"]!)).ToArray();
        string[] selected = JsonSerializer.Deserialize<string[]>(child.StandardOutput)!;
        CollectionAssert.AreEqual(expected, selected); Assert.AreEqual(mode.StartsWith("rotation", StringComparison.Ordinal) ? 3 : mode.StartsWith("recovery", StringComparison.Ordinal) ? 2 : 1, selected.Length);
        foreach (string path in selected)
        {
            foreach (byte[] line in FileRestartExpectations.Split(File.ReadAllBytes(path)))
            {
                JsonObject record = JsonNode.Parse(line)!.AsObject(); string name = (string)record["event_name"]!; int pid = record["attributes"]!["producer_pid"]!.GetValue<int>();
                Assert.IsTrue(name == "restart.resume" && pid == resume["pid"]!.GetValue<int>() || name is "restart.seed.0" or "restart.seed.1" && pid == seed["pid"]!.GetValue<int>());
                FileRestartExpectations.Record(line, name, pid);
            }
        }
    }

    private sealed class HeldProducer : IAsyncDisposable
    {
        private readonly Process child; private readonly Task<string> errors; private readonly string hostHash, coreHash, cwd; private readonly Dictionary<string, string?> parent; private readonly TestContext context;
        internal HeldProducer(string mode, string root, TestContext context)
        {
            this.context = context; hostHash = Hash(Host); coreHash = Hash(Core); cwd = Directory.GetCurrentDirectory(); parent = Parent();
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true) };
            string[] command = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, "file-restart", mode, "hold", root];
            foreach (string argument in command) { start.ArgumentList.Add(argument); }
            start.Environment.Remove("DOTNET_SYSTEM_IO_DISABLEFILELOCKING");
            child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start owned producer."); errors = child.StandardError.ReadToEndAsync();
            context.WriteLine("Held restart producer: " + child.Id + "; " + string.Join(" ", command));
        }
        internal async Task<JsonObject> Read()
        {
            string line = await child.StandardOutput.ReadLineAsync().WaitAsync(Limit) ?? throw new InvalidDataException("Missing held producer report.");
            Assert.IsFalse(child.HasExited); Assert.IsFalse(line.StartsWith('\uFEFF')); context.WriteLine(line);
            JsonObject report = JsonNode.Parse(line)!.AsObject();
            Assert.AreEqual(child.Id, report["pid"]!.GetValue<int>()); Assert.AreEqual(".NET 10.0.12", (string?)report["runtime"]); Assert.AreEqual(FixtureProcessChecks.Family, (string?)report["os"]);
            Assert.AreEqual(hostHash, (string?)report["fixtureSha256"]); Assert.AreEqual(coreHash, (string?)report["coreSha256"]); return report;
        }
        internal async Task Kill()
        {
            Assert.IsFalse(child.HasExited); child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(Limit); Assert.IsTrue(child.HasExited);
            Assert.AreEqual("", await child.StandardOutput.ReadToEndAsync().WaitAsync(Limit)); Assert.AreEqual("", await errors.WaitAsync(Limit));
            context.WriteLine("Forced owned producer exit: " + child.Id + "; exit=" + child.ExitCode);
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); }
                await child.WaitForExitAsync().WaitAsync(Limit); await errors.WaitAsync(Limit);
                Assert.AreEqual(hostHash, Hash(Host)); Assert.AreEqual(coreHash, Hash(Core)); Assert.AreEqual(cwd, Directory.GetCurrentDirectory());
                Dictionary<string, string?> after = Parent(); CollectionAssert.AreEquivalent(parent.Keys.ToArray(), after.Keys.ToArray()); Assert.IsTrue(parent.All(p => after[p.Key] == p.Value));
            }
            finally { child.Dispose(); }
        }
    }
    private static string Host => Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
    private static string Core => Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll");
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string?> Parent() => Environment.GetEnvironmentVariables().Keys.Cast<string>().ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
}
