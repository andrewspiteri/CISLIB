using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]), task = Path.GetFullPath(args[1]), nativeReports = Path.GetFullPath(args[2]), evidence = Path.GetFullPath(args[3]);
string windowsReports = Path.Combine(task, "reviewed-work/.artifacts/csharp/verification/Developer");
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path))!;
void Require(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
void Copy(string source, string target) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, overwrite: false); }
JsonNode inputs = Read(Path.Combine(task, "reviewed-final-inputs.json"));
var expectedInputs = inputs["files"]!.AsArray().ToDictionary(item => item!["path"]!.GetValue<string>(), item => item!["sha256"]!.GetValue<string>(), StringComparer.Ordinal);
var lanes = new List<object>();
foreach ((string name, string reports, string package) in new[] {
    ("windows-x64", windowsReports, Path.Combine(task, "reviewed-work/.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg")),
    ("linux-ubuntu2404-x64", nativeReports, Path.Combine(task, "reviewed-final-native-candidate.nupkg")) })
{
    JsonNode summary = Read(Path.Combine(reports, "summary.json"));
    Require(summary["exitCode"]!.GetValue<int>() == 0, name + " Developer failed.");
    Require(summary["stages"]!.AsArray().Count == 16 && summary["stages"]!.AsArray().All(stage => stage!["status"]!.GetValue<string>() == "Passed"), name + " stage mismatch.");
    Require(summary["source"]!["commit"]!.GetValue<string>() == inputs["snapshotCommit"]!.GetValue<string>(), name + " commit mismatch.");
    var rawInputs = Read(Path.Combine(reports, "source-inputs.json")).AsArray().ToDictionary(item => item!["path"]!.GetValue<string>(), item => item!["sha256"]!.GetValue<string>(), StringComparer.Ordinal);
    Require(rawInputs.Count == expectedInputs.Count && rawInputs.All(item => expectedInputs.TryGetValue(item.Key, out string? hash) && hash == item.Value), name + " maintained inputs differ.");
    var families = new List<object>();
    int total = 0;
    foreach (JsonNode family in summary["families"]!.AsArray().Where(family => family!["status"]!.GetValue<string>() == "Executed").OfType<JsonNode>())
    {
        string trx = Path.Combine(reports, family["report"]!["path"]!.GetValue<string>());
        Require(Hash(trx) == family["report"]!["sha256"]!.GetValue<string>(), "TRX hash mismatch.");
        XElement counters = XDocument.Load(trx).Descendants().Single(element => element.Name.LocalName == "Counters");
        int count = int.Parse(counters.Attribute("total")!.Value);
        Require(count > 0 && counters.Attribute("passed")!.Value == count.ToString() && counters.Attribute("failed")!.Value == "0", name + " test failure/skip.");
        total += count;
        families.Add(new { family = family["id"]!.GetValue<string>(), passed = count, trxSha256 = Hash(trx) });
    }
    Require(total == 5348 && families.Count == 4, name + " unexpected total.");
    JsonNode reconciliationReference = summary["reconciliation"]!["report"]!;
    Require(summary["reconciliation"]!["status"]!.GetValue<string>() == "Produced", "Missing reconciliation.");
    string reconciliationPath = Path.Combine(reports, reconciliationReference["path"]!.GetValue<string>());
    Require(Hash(reconciliationPath) == reconciliationReference["sha256"]!.GetValue<string>(), "Reconciliation hash mismatch.");
    JsonNode reconciliation = Read(reconciliationPath);
    Require(reconciliation["runId"]!.GetValue<string>() == summary["runId"]!.GetValue<string>(), "Stale reconciliation.");
    string architecturePath = Path.Combine(reports, summary["architectureInspection"]!["path"]!.GetValue<string>());
    Require(Hash(architecturePath) == summary["architectureInspection"]!["sha256"]!.GetValue<string>(), "Architecture hash mismatch.");
    JsonNode architecture = Read(architecturePath);
    Require(architecture["projects"]!.AsArray().Count == 27 && architecture["projects"]!.AsArray().Sum(project => project!["targets"]!.AsArray().Count) == 33, "Project/target inventory mismatch.");
    Require(Hash(package) == architecture["package"]!["sha256"]!.GetValue<string>(), "Candidate hash mismatch.");
    var assemblies = new List<object>();
    using (ZipArchive archive = ZipFile.OpenRead(package))
    {
        var dlls = archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        Require(dlls.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(architecture["assemblies"]!.AsArray().Select(assembly => assembly!["packageEntry"]!.GetValue<string>())), "Missing package DLL inspection.");
        foreach (JsonNode assembly in architecture["assemblies"]!.AsArray().OfType<JsonNode>())
        {
            JsonNode facts = assembly["facts"]!;
            using Stream stream = dlls[assembly["packageEntry"]!.GetValue<string>()].Open();
            string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            Require(hash == facts["sha256"]!.GetValue<string>() && hash == assembly["builtAssembly"]!["sha256"]!.GetValue<string>(), "Packed/origin metadata drift.");
            Require(facts["customAttributes"]!["total"]!.GetValue<int>() == facts["customAttributes"]!["visited"]!.GetValue<int>(), "Omitted custom attribute metadata.");
            assemblies.Add(new { name = facts["name"]!.GetValue<string>(), packageEntry = assembly["packageEntry"]!.GetValue<string>(), sha256 = hash,
                types = facts["types"]!.AsArray().Count, attributesVisited = facts["customAttributes"]!["visited"]!.GetValue<int>(), typeUses = facts["typeUses"]!.AsArray().Count,
                internalEdges = facts["internalDependencies"]!.AsArray().Count, namespaceEdges = facts["namespaceDependencies"]!.AsArray().Count });
        }
    }
    lanes.Add(new { lane = name, runId = summary["runId"]!.GetValue<string>(), stagesPassed = 16, testsPassed = total, families,
        maintainedInputCount = rawInputs.Count, projects = 27, targetGraphs = 33, assemblies, candidateSha256 = Hash(package), architectureReportSha256 = Hash(architecturePath) });
}
Require(Read(Path.Combine(task, "reviewed-release-summary.json"))["exitCode"]!.GetValue<int>() == 2, "Release unexpectedly qualified.");
JsonNode baseline = Read(Path.Combine(task, "baseline.json"));
foreach ((string path, JsonNode? value) in baseline["protected"]!.AsObject()) { Require(Hash(Path.Combine(repo, path)) == value!.GetValue<string>(), "Protected source changed: " + path); }
string foreign = baseline["foreign"]!["path"]!.GetValue<string>();
Require(Hash(Path.Combine(repo, foreign)) == baseline["foreign"]!["sha256"]!.GetValue<string>(), "Foreign profile changed.");
Require(!Directory.Exists(evidence), "Use a fresh evidence root.");
Directory.CreateDirectory(evidence);
string[] extensions = [".json", ".trx", ".log", ".xml", ".txt", ".cs", ".csproj", ".props", ".targets"];
foreach ((string name, string root) in new[] { ("windows/Developer", windowsReports), ("ubuntu/Developer", nativeReports), ("coverage/pre-review-candidate", Path.Combine(task, "coverage-current")) })
{
    foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(root, path);
        if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj") || !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) { continue; }
        Copy(path, Path.Combine(evidence, name, relative));
    }
}
foreach (string name in new[] { "baseline.json", "reviewed-release-summary.json", "reviewed-release-validation.json", "reviewed-final-inputs.json", "audit-evidence.cs", "approved-t04-review-input.json", "approved-t04-review.json", "approved-t04-findings.txt", "approved-t01-inputs.json", "claude-t01-approved.json", "claude-t01-findings.txt", "claude-t01-resolutions.json", "review-ready-manifest.json", "coverage-controls.cs", "coverage-current.exit", "reviewed-focused-inspection.log", "graph-final-build.log", "graph-final-status.log", "graph-final-find.log" })
{
    Copy(Path.Combine(task, name), Path.Combine(evidence, "task", name));
}
foreach (string path in Directory.EnumerateFiles(Path.Combine(task, "coverage-preflight-controls"), "*", SearchOption.TopDirectoryOnly)) { Copy(path, Path.Combine(evidence, "controls", Path.GetFileName(path))); }
Copy(Path.Combine(task, "reviewed-focused-inspection/reviewed-inspection.trx"), Path.Combine(evidence, "controls/reviewed-inspection.trx"));
JsonNode coverage = Read(Path.Combine(task, "coverage-current/coverage.json"));
Require(coverage["qualified"]!.GetValue<bool>() == false && File.ReadAllText(Path.Combine(task, "coverage-current.exit")).Trim() == "2", "Coverage must remain unqualified.");
var receipt = new { task = "WP07-S02-T01", baseCommit = baseline["head"]!.GetValue<string>(), scope = "Five-level architecture inspection; stronger policy and comprehensive violations remain T02/T03/T04. No full release qualification.",
    sourceSnapshotCommit = inputs["snapshotCommit"]!.GetValue<string>(), lanes, protectedInputsUnchanged = baseline["protected"]!.AsObject().Count, foreignFilePreserved = foreign,
    review = new { originalT04 = "Exact approved 9-file 39,528-byte snapshot reviewed; six findings corrected.", currentT01 = "Exact approved 9-file 77,675-byte snapshot reviewed; all four findings addressed. Post-review changes verified locally; no unapproved follow-up transfer or external PASS claim." },
    coverage = new { qualified = false, scope = "Corrected collector against the pre-review Windows candidate with unchanged protected shipping sources; not final reviewed-candidate coverage qualification.", report = "coverage/pre-review-candidate/coverage.json", rejectionControlsPassed = 2 },
    release = new { exitCode = 2, qualification = "Pending original roadmap obligations; unchanged publication ledger." },
    graph = "Build succeeds with partial context/warnings; status/find fail with read-only SQLite cache. No semantic completeness claim.",
    retention = "Generated reports/hashes retained; candidate binaries remain task-local. Not a complete offline replay kit.", goalNextAction = "Pause after this task's verified commit, as requested by user." };
File.WriteAllText(Path.Combine(evidence, "verification.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
var files = Directory.EnumerateFiles(evidence, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => new { path = Path.GetRelativePath(evidence, path).Replace('\\', '/'), bytes = new FileInfo(path).Length, sha256 = Hash(path) }).ToArray();
File.WriteAllText(Path.Combine(evidence, "files.json"), JsonSerializer.Serialize(new { files }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Final independent audit passed: {lanes.Count} lanes, 5348 tests each, 27 projects/33 targets/2 assemblies; {files.Length} evidence files.");
