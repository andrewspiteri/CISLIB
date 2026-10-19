#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;


string repo = Path.GetFullPath(args[0]);
string native = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
string work = Path.Combine(repo, ".artifacts/corrective/microsoft-logging-bundle-migration-2026-10-19");
string receipt = Path.Combine(repo, "planning/evidence/corrective-microsoft-logging-bundle-migration-2026-10-19");
void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
void CopyTree(string source, string target)
{
    // Resume interrupted retention only when every existing byte still matches.
    Directory.CreateDirectory(target);
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string destination = Path.Combine(target, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        RetainFile(file, destination);
    }
}
void RetainFile(string source, string destination)
{
    if (File.Exists(destination)) { Require(Hash(source) == Hash(destination), "Preserve differing retained bytes: " + destination); }
    else { File.Copy(source, destination); }
}
JsonArray declaredFamilies = Read(Path.Combine(repo, "implementations/csharp/verification/families.json"))["families"]!.AsArray();
Require(declaredFamilies.Count == 6, "Preserve all six declared families.");
var evidence = new JsonArray();
var originals = new List<(string Lane, string Root)>();
foreach ((string lane, string owner, string runId) in new[] { ("windows", repo, args[1]), ("ubuntu", native, args[2]) })
{
    string root = Path.Combine(owner, ".artifacts/csharp/verification/Developer");
    JsonObject summary = Read(Path.Combine(root, "summary.json"));
    Require((string?)summary["runId"] == runId && summary["finishedUtc"] is not null && (int?)summary["exitCode"] == 0 && summary["issues"]!.AsArray().Count == 0, "Incomplete or failed checkpoint: " + lane);
    Require((string?)summary["reconciliation"]!["status"] == "Produced", "Missing required-case reconciliation.");
    JsonObject validation = Read(Path.Combine(root, "validation.json"));
    Require((int?)validation["exitCode"] == 0 && validation["issues"]!.AsArray().Count == 0, "Validation did not pass.");
    Require(Hash(Path.Combine(root, "source-inputs.json")) == (string?)summary["source"]!["inputInventory"]!["sha256"], "Source input reference differs.");
    string reconciliationPath = Path.Combine(root, summary["reconciliation"]!["report"]!["path"]!.GetValue<string>());
    Require(Hash(reconciliationPath) == (string?)summary["reconciliation"]!["report"]!["sha256"], "Reconciliation reference differs.");
    JsonObject reconciliation = Read(reconciliationPath);
    Require((string?)reconciliation["runId"] == runId && (string?)reconciliation["profile"] == "Developer" && (string?)reconciliation["lane"] == (string?)summary["lane"], "Reconciliation identity differs.");
    foreach ((string id, int methods, int executions) in new[] { ("ARCH-NATIVE-MICROSOFT-LOGGING-BUNDLE", 4, 31) })
    {
        JsonNode migratedCase = reconciliation["families"]!.AsArray().Single(item => (string?)item!["id"] == "architecture")!["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!;
        Require(migratedCase["bindings"]!.AsArray().Count == methods && migratedCase["bindings"]!.AsArray().Sum(binding => binding!["passed"]!.GetValue<int>()) == executions &&
            migratedCase["bindings"]!.AsArray().All(binding => (int?)binding!["passed"] >= (int?)binding["minimumExecutions"] && (int?)binding!["failed"] == 0 && (int?)binding!["skipped"] == 0), "Migrated case bindings are incomplete: " + id);
    }
    Require(summary["stages"]!.AsArray().All(stage => (string?)stage!["status"] == "Passed" && (int?)stage["exitCode"] == 0), "Failed stage.");
    string inspectionPath = Path.Combine(root, summary["architectureInspection"]!["path"]!.GetValue<string>());
    Require(Hash(inspectionPath) == (string?)summary["architectureInspection"]!["sha256"], "Inspection pin differs.");
    JsonObject inspection = Read(inspectionPath);
    Require(!string.IsNullOrWhiteSpace((string?)inspection["repositoryHeadCommit"]) && inspection["sourceCommit"] is null &&
        ((string?)inspection["sourceIdentityScope"])?.StartsWith("Repository HEAD only", StringComparison.Ordinal) == true,
        "Fresh inspection must label HEAD-only source identity.");
    Require(Hash(Path.Combine(owner, ".artifacts/csharp/packages", Path.GetFileName(inspection["package"]!["path"]!.GetValue<string>()))) == (string?)inspection["package"]!["sha256"], "Package differs from the inspected pin.");
    int total = 0, executedFamilies = 0;
    var families = new JsonArray();
    var seenFamilies = new HashSet<string>(StringComparer.Ordinal);
    foreach (JsonNode? family in summary["families"]!.AsArray())
    {
        string relative = family!["execution"]!["path"]!.GetValue<string>();
        string path = Path.Combine(root, relative);
        Require(Hash(path) == (string?)family["execution"]!["sha256"], "Execution reference hash differs.");
        JsonObject execution = Read(path);
        string id = family["id"]!.GetValue<string>();
        Require(seenFamilies.Add(id), "Repeated family identity.");
        JsonNode policy = declaredFamilies.Single(item => (string?)item!["id"] == id)!;
        if (!policy["developerEnabled"]!.GetValue<bool>())
        {
            Require((string?)family["status"] == "NotRun" && (string?)execution["status"] == "NotRun" &&
                family["report"] is null && (int?)execution["executed"] == 0 && (int?)execution["passed"] == 0 &&
                (int?)execution["failed"] == 0 && (int?)execution["skipped"] == 0 &&
                (string?)family["reason"] == (string?)policy["developerReason"] &&
                (string?)execution["reason"] == (string?)policy["developerReason"], "Preserve explicit unexecuted family and reason.");
            families.Add(new JsonObject { ["family"] = id, ["status"] = "NotRun", ["reason"] = family["reason"]!.DeepClone(), ["passed"] = 0 });
            continue;
        }
        Require((string?)family["status"] == "Executed" && (string?)execution["status"] == "Executed", "Required Developer family unexecuted.");
        executedFamilies++;
        foreach (string reference in new[] { "discovery", "report" })
        { Require(Hash(Path.Combine(root, family[reference]!["path"]!.GetValue<string>())) == (string?)family[reference]!["sha256"], "Native/discovery reference differs."); }
        int passed = execution["passed"]!.GetValue<int>();
        Require(passed > 0 && passed == (int?)execution["executed"] && (int?)execution["failed"] == 0 && (int?)execution["skipped"] == 0, "Invalid execution counters.");
        total += passed;
        families.Add(new JsonObject { ["family"] = family["id"]!.DeepClone(), ["status"] = "Executed", ["passed"] = passed });
    }
    Require(total >= 6500 && executedFamilies == 4 && families.Count == 6, "Incomplete Developer execution or omitted family records.");
    evidence.Add(new JsonObject { ["lane"] = lane, ["runId"] = runId, ["passed"] = total, ["families"] = families, ["summarySha256"] = Hash(Path.Combine(root, "summary.json")), ["validationSha256"] = Hash(Path.Combine(root, "validation.json")), ["inputInventorySha256"] = Hash(Path.Combine(root, "source-inputs.json")) });
    originals.Add((lane, root));
}
JsonArray inputs = JsonNode.Parse(File.ReadAllBytes(Path.Combine(originals[0].Root, "source-inputs.json")))!.AsArray();
var selected = new JsonArray();
var laneInputs = originals.ToDictionary(row => row.Lane, row => JsonNode.Parse(File.ReadAllBytes(Path.Combine(row.Root, "source-inputs.json")))!.AsArray().ToDictionary(entry => entry!["path"]!.GetValue<string>(), entry => entry!["sha256"]!.GetValue<string>(), StringComparer.Ordinal));
var publicationInputs = new JsonArray();
foreach (string name in new[] { "ledger.schema.json", "baseline.schema.json", "ledger.json" })
{
    string relative = "compatibility/csharp/publications/" + name;
    string expected = Hash(Path.Combine(repo, relative));
    Require(Hash(Path.Combine(native, relative)) == expected && laneInputs.Values.All(inventory => inventory.TryGetValue(relative, out string? recorded) && recorded == expected), "Canonical publication inputs differ: " + relative);
    publicationInputs.Add(new JsonObject { ["path"] = relative, ["sha256"] = expected });
}
foreach (JsonNode? item in inputs)
{
    string path = item!["path"]!.GetValue<string>();
    bool maintained = path.StartsWith("implementations/csharp/", StringComparison.Ordinal) || path.StartsWith("eng/", StringComparison.Ordinal) || path.StartsWith("contracts/", StringComparison.Ordinal) || path.StartsWith("conformance/", StringComparison.Ordinal);
    bool executableInput = new[] { ".cs", ".csproj", ".props", ".targets", ".ps1", ".psm1", ".json", ".slnx", ".runsettings" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    if (!maintained || !executableInput || path.EndsWith("/Properties/launchSettings.json", StringComparison.Ordinal)) continue;
    string expected = item["sha256"]!.GetValue<string>();
    Require(Hash(Path.Combine(repo, path)) == expected && Hash(Path.Combine(native, path)) == expected, "Code/build/configuration inputs differ: " + path);
    foreach (var observation in originals)
    {
        Require(laneInputs[observation.Lane].TryGetValue(path, out string? recordedHash) && recordedHash == expected, "Lane did not record the same input: " + observation.Lane + " " + path);
    }
    selected.Add(item.DeepClone());
}
Require(selected.Count == 882, "Unexpected executable input selection.");
var selectedPaths = selected.Select(item => item!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
foreach (var lane in laneInputs)
{
    string[] recordedSelection = lane.Value.Keys.Where(path =>
        new[] { "implementations/csharp/", "eng/", "contracts/", "conformance/" }.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)) &&
        new[] { ".cs", ".csproj", ".props", ".targets", ".ps1", ".psm1", ".json", ".slnx", ".runsettings" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
        !path.EndsWith("/Properties/launchSettings.json", StringComparison.Ordinal)).ToArray();
    Require(selectedPaths.SetEquals(recordedSelection), "Lane has a different executable input set: " + lane.Key);
}
Require(!File.Exists(Path.Combine(receipt, "audit.json")), "Preserve previous receipt audit.");
foreach (var original in originals)
{
    CopyTree(original.Root, Path.Combine(work, original.Lane + "-Developer"));
    CopyTree(Path.Combine(original.Lane == "windows" ? repo : native, ".artifacts/csharp/packages"), Path.Combine(work, original.Lane + "-packages"));
}
Directory.CreateDirectory(receipt);
foreach (var original in originals)
{
    RetainFile(Path.Combine(original.Root, "summary.json"), Path.Combine(receipt, original.Lane + "-summary.json"));
    RetainFile(Path.Combine(original.Root, "validation.json"), Path.Combine(receipt, original.Lane + "-validation.json"));
}
// This retained audit is executed directly from its canonical planning path.
File.WriteAllText(Path.Combine(receipt, "matching-executable-inputs.json"), selected.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
var controls = new JsonArray();
foreach (string lane in new[] { "windows", "ubuntu" })
{
    string root = Path.Combine(work, lane + "-cli");
    JsonObject process = Read(Path.Combine(root, "process.json"));
    Require((int?)process["exitCode"] == (lane == "windows" ? 0 : 1), "Unexpected actual bundle CLI exit.");
    foreach (string stream in new[] { "stdout", "stderr" })
    {
        Require(Hash(Path.Combine(root, stream + ".bin")) == (string?)process[stream + "Sha256"], "Actual CLI diagnostic bytes changed.");
        RetainFile(Path.Combine(root, stream + ".bin"), Path.Combine(receipt, lane + "-cli-" + stream + ".bin"));
    }
    RetainFile(Path.Combine(root, "process.json"), Path.Combine(receipt, lane + "-cli-process.json"));
    controls.Add(new JsonObject { ["lane"] = lane, ["exitCode"] = process["exitCode"]!.DeepClone(), ["processSha256"] = Hash(Path.Combine(root, "process.json")) });
}
string draft = Path.Combine(work, "windows-draft"), manifestPath = Path.Combine(draft, "manifest.json");
JsonObject manifest = Read(manifestPath);
Require((string?)manifest["schema"] == "cis.csharp.verification-bundle-draft/v1" && (string?)manifest["state"] == "IncompleteReleaseDraft" &&
    (bool?)manifest["releaseQualified"] == false && manifest["releaseAreas"]!.AsArray().Count == 9 &&
    manifest["releaseAreas"]!.AsArray().All(area => (string?)area!["fullReleaseState"] == "Pending") && manifest["pendingReleaseRequirements"]!.AsArray().Count == 4,
    "Draft must not claim release qualification.");
Require(File.ReadAllText(Path.Combine(draft, "manifest.sha256")).Trim() == Hash(manifestPath), "External manifest hash differs.");
foreach (JsonNode? item in manifest["files"]!.AsArray())
{
    string path = Path.Combine(repo, item!["path"]!.GetValue<string>());
    Require(Hash(path) == (string?)item["sha256"] && new FileInfo(path).Length == (long?)item["bytes"], "Indexed draft input differs: " + path);
}
JsonNode product = manifest["product"]!;
Require((string?)product["packageId"] == "CIS.Core" && Hash(Path.Combine(draft, product["retainedPackage"]!.GetValue<string>())) == (string?)product["packageSha256"] &&
    Hash(Path.Combine(draft, product["assembly"]!.GetValue<string>())) == (string?)product["assemblySha256"] &&
    Hash(Path.Combine(draft, product["apiDocumentation"]!.GetValue<string>())) == (string?)product["apiDocumentationSha256"], "Retained draft products differ.");
Require((string?)manifest["developer"]!["runId"] == args[1], "Draft must record the actual completed Windows checkpoint.");
Require(manifest["reportGroups"]!.AsArray().Count == 14, "Preserve all fourteen historical provider evidence roots.");
string absent = ".artifacts/csharp/tasks/wp06-s03-t03/mutation-qualified/mutation/reports/mutation-report.json";
Require(!File.Exists(Path.Combine(native, absent)) && !Directory.Exists(Path.Combine(native, ".artifacts/corrective/microsoft-logging-bundle-migration-2026-10-19/ubuntu-draft")), "Ubuntu must preserve its missing-historical-evidence refusal.");
Require(File.ReadAllText(Path.Combine(work, "ubuntu-cli/stderr.bin")).Contains("Missing required inventory input: " + absent, StringComparison.Ordinal), "Ubuntu refusal is for a different cause.");
RetainFile(manifestPath, Path.Combine(receipt, "windows-draft-manifest.json"));
RetainFile(Path.Combine(draft, "manifest.sha256"), Path.Combine(receipt, "windows-draft-manifest.sha256"));
var audit = new JsonObject
{
    ["scope"] = "Final assessed C# verification-policy migration; bundle remains an incomplete inventory draft.",
    ["checkpoints"] = evidence, ["matchingExecutableInputs"] = selected.Count,
    ["matchingCSharpInputs"] = selected.Count(item => item!["path"]!.GetValue<string>().EndsWith(".cs", StringComparison.Ordinal)),
    ["publicationInputs"] = publicationInputs, ["actualBundleControls"] = controls, ["draftManifestSha256"] = Hash(manifestPath),
    ["indexedFiles"] = manifest["files"]!.AsArray().Count, ["retainedProduct"] = product.DeepClone(),
    ["remainingAssessedSubstantivePowerShellEntries"] = 0, ["roadmap"] = "Paused", ["claudeUsed"] = false
};
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Retained {evidence.Count} passing Developer checkpoints, {selected.Count} matching inputs and both actual bundle CLI outcomes; releaseQualified=false.");
