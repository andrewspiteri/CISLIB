#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;


string repo = Path.GetFullPath(args[0]);
string native = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
string work = Path.Combine(repo, ".artifacts/corrective/shipping-coverage-collection-migration-2026-10-18");
string receipt = Path.Combine(repo, "planning/evidence/corrective-shipping-coverage-collection-migration-2026-10-18");
void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
void CopyTree(string source, string target)
{
    Require(!Directory.Exists(target), "Preserve existing archive: " + target);
    Directory.CreateDirectory(target);
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string destination = Path.Combine(target, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(file, destination);
    }
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
    foreach ((string id, int methods, int executions) in new[] { ("ARCH-NATIVE-SHIPPING-COVERAGE", 7, 40) })
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
Require(selected.Count == 872, "Unexpected executable input selection.");
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
    File.Copy(Path.Combine(original.Root, "summary.json"), Path.Combine(receipt, original.Lane + "-summary.json"));
    File.Copy(Path.Combine(original.Root, "validation.json"), Path.Combine(receipt, original.Lane + "-validation.json"));
}
// This retained audit is executed directly from its canonical planning path.
File.WriteAllText(Path.Combine(receipt, "matching-executable-inputs.json"), selected.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
JsonObject launch = Read(Path.Combine(work, "windows-coverage-final-launch.json"));
Require((string?)launch["environment"]!["UseSharedCompilation"] == "false" &&
    (string?)launch["observedUseSharedCompilation"] == "false" && (int?)launch["exitCode"] == 2,
    "Windows compiler isolation was not observed or collection did not complete.");
File.Copy(Path.Combine(work, "windows-coverage-final-launch.json"), Path.Combine(receipt, "windows-coverage-launch.json"));
var timeoutControls = new JsonArray();
foreach ((string name, int expectedFailed, int expectedPassed) in new[] {
    ("windows-timeout-repro", 2, 0), ("windows-compiler-isolation-repro", 0, 2) })
{
    string path = Path.Combine(work, name, "report.trx");
    var xml = System.Xml.Linq.XDocument.Load(path);
    var counters = xml.Descendants().Single(element => element.Name.LocalName == "Counters");
    Require((int?)counters.Attribute("failed") == expectedFailed && (int?)counters.Attribute("passed") == expectedPassed &&
        (int?)counters.Attribute("total") == 2, "Compiler isolation control differs.");
    timeoutControls.Add(new JsonObject { ["id"] = name, ["failed"] = expectedFailed, ["passed"] = expectedPassed, ["trxSha256"] = Hash(path) });
}
var coverage = new JsonArray();
foreach ((string lane, string owner) in new[] { ("windows", repo), ("ubuntu", native) })
{
    string root = Path.Combine(owner, ".artifacts/corrective/shipping-coverage-collection-migration-2026-10-18", lane == "windows" ? "windows-coverage-final" : "ubuntu-coverage");
    JsonObject report = Read(Path.Combine(root, "coverage.json")), candidate = Read(Path.Combine(root, "candidate.json"));
    int exit = Read(Path.Combine(root, "collection-exit.json"))["exitCode"]!.GetValue<int>();
    Require((string?)report["schema"] == "cis.csharp.shipping-coverage-observation/v1" &&
        (string?)report["policySha256"] == Hash(Path.Combine(owner, "implementations/csharp/verification/coverage/shipping.json")), "Coverage policy differs.");
    JsonObject policy = Read(Path.Combine(owner, "implementations/csharp/verification/coverage/shipping.json"));
    Require((double?)policy["minimumLineRate"] == 0.95 && (double?)policy["minimumBranchRate"] == 0.9, "Preserve shipping thresholds.");
    string package = Path.Combine(owner, candidate["path"]!.GetValue<string>());
    Require(Hash(package) == (string?)candidate["sha256"], "Coverage candidate package changed.");
    JsonObject summary = Read(Path.Combine(owner, ".artifacts/csharp/verification/Developer/summary.json"));
    JsonObject inspection = Read(Path.Combine(owner, ".artifacts/csharp/verification/Developer", summary["architectureInspection"]!["path"]!.GetValue<string>()));
    Require((string?)inspection["package"]!["sha256"] == (string?)candidate["sha256"], "Coverage and Developer packages differ.");
    JsonArray commands = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "commands.json")))!.AsArray();
    string[] families = ["unit", "integration", "architecture", "business"];
    Require(commands.Count == 4 && commands.Select(row => row!["family"]!.GetValue<string>()).SequenceEqual(families) &&
        commands.All(row => (int?)row!["exitCode"] == 0 && row["argv"]!.AsArray().Any(argument => (string?)argument == "--no-build") &&
            row["argv"]!.AsArray().Any(argument => (string?)argument == "--no-restore")), "Incomplete native collector commands.");
    JsonArray reports = report["reports"]!.AsArray();
    Require(reports.Count == 4, "Missing native coverage report.");
    long total = 0;
    for (int index = 0; index < families.Length; index++)
    {
        JsonNode observed = reports[index]!;
        string trx = Path.Combine(root, families[index], "report.trx");
        Require(Hash(trx) == (string?)observed["trxSha256"], "Coverage TRX pin differs.");
        string filename = Path.GetFileName(observed["cobertura"]!.GetValue<string>());
        string[] files = Directory.GetFiles(Path.Combine(root, families[index]), filename, SearchOption.AllDirectories);
        Require(files.Length > 0 && files.All(file => Hash(file) == (string?)observed["coberturaSha256"]), "Coverage attachment bytes differ.");
        JsonObject execution = Read(Path.Combine(owner, ".artifacts/csharp/verification/Developer/families", families[index], "execution.json"));
        long passed = observed["testsPassed"]!.GetValue<long>();
        Require(passed > 0 && passed == (int?)execution["passed"], "Coverage did not execute the complete Developer family.");
        total += passed;
    }
    JsonArray pins = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "inputs.json")))!.AsArray();
    foreach (JsonNode? pin in pins)
    {
        string path = Path.GetFullPath(pin!["path"]!.GetValue<string>(), Path.Combine(owner, "implementations/csharp"));
        Require(Hash(path) == (string?)pin["sha256"], "Pinned coverage input changed before retention: " + path);
    }
    JsonArray assemblies = report["assemblies"]!.AsArray();
    Require(assemblies.Count == 2 && assemblies.Select(row => row!["assembly"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(["CIS.Core", "CIS.TypeManager.Build"]), "Shipping coverage scope differs.");
    foreach (JsonNode? assembly in assemblies)
    {
        long lines = assembly!["linesTotal"]!.GetValue<long>(), branches = assembly["branchesTotal"]!.GetValue<long>();
        bool passed = lines > 0 && branches > 0 && assembly["linesCovered"]!.GetValue<long>() >= 0.95 * lines &&
            assembly["branchesCoveredLowerBound"]!.GetValue<long>() >= 0.9 * branches;
        Require((bool?)assembly["thresholdPassed"] == passed, "Per-assembly gate differs from unchanged thresholds.");
    }
    bool qualified = assemblies.All(assembly => (bool?)assembly!["thresholdPassed"] == true);
    Require((bool?)report["qualified"] == qualified && exit == (qualified ? 0 : 2), "Native threshold result/exit differs.");
    if (lane == "ubuntu") CopyTree(root, Path.Combine(work, "ubuntu-coverage"));
    foreach (string name in new[] { "coverage.json", "candidate.json", "environment.json", "commands.json", "collection-exit.json" })
        File.Copy(Path.Combine(root, name), Path.Combine(receipt, lane + "-" + name));
    coverage.Add(new JsonObject
    {
        ["lane"] = lane, ["testsPassed"] = total, ["policyCases"] = 40, ["qualified"] = qualified, ["exitCode"] = exit,
        ["reportSha256"] = Hash(Path.Combine(root, "coverage.json")), ["inputPins"] = pins.Count,
        ["inputsSha256"] = Hash(Path.Combine(root, "inputs.json")), ["sourceInputsSha256"] = Hash(Path.Combine(root, "source-inputs.json")),
        ["packageSha256"] = candidate["sha256"]!.DeepClone(),
        ["assemblies"] = new JsonArray(assemblies.Select(assembly => (JsonNode)new JsonObject
        {
            ["assembly"] = assembly!["assembly"]!.DeepClone(), ["lineRate"] = assembly["lineRate"]!.DeepClone(),
            ["branchRateLowerBound"] = assembly["branchRateLowerBound"]!.DeepClone(), ["thresholdPassed"] = assembly["thresholdPassed"]!.DeepClone()
        }).ToArray())
    });
}
var audit = new JsonObject
{
    ["shippingCoverage"] = coverage, ["compilerIsolationControls"] = timeoutControls, ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
    ["windowsBaseCommit"] = "aae140d9b054bba8bd9cebe5e72748f0ab43d40b", ["taskChangesBoundByInputHashes"] = true,
    ["matchingCodeBuildConfigurationInputs"] = selected.Count, ["checkpoints"] = evidence, ["canonicalPublicationInputs"] = publicationInputs,
    ["archive"] = ".artifacts/corrective/shipping-coverage-collection-migration-2026-10-18",
    ["limits"] = "Native worktrees retain different Git metadata and lane-local packages; this is not one-package cross-lane release qualification. Markdown, planning evidence and the user-owned launchSettings.json are outside executable-input equality. Coverage separately binds each lane's Developer package, complete native families and source/binary/reference pins. A complete threshold-failing measurement remains unqualified. Full shipping mutation, cumulative published compatibility and complete Release profiles were not run. The original roadmap remains paused; Claude was not used."
};
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
