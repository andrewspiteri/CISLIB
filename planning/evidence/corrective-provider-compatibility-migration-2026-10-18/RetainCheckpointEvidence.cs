#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;


string repo = Path.GetFullPath(args[0]);
string native = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
string work = Path.Combine(repo, ".artifacts/corrective/provider-compatibility-migration-2026-10-18");
string receipt = Path.Combine(repo, "planning/evidence/corrective-provider-compatibility-migration-2026-10-18");
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
    foreach ((string id, int methods, int executions) in new[] { ("ARCH-NATIVE-PROVIDER-COMPATIBILITY", 6, 42) })
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
Require(selected.Count == 870, "Unexpected executable input selection.");
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
var focused = new JsonArray();
foreach ((string lane, string owner) in new[] { ("windows", repo), ("ubuntu", native) })
{
    string root = Path.Combine(owner, ".artifacts/corrective/provider-compatibility-migration-2026-10-18/compatibility-final");
    string path = Path.Combine(root, "results.json");
    JsonObject report = Read(path);
    JsonArray rows = report["results"]!.AsArray(), frozen = report["frozenInputs"]!.AsArray(), runtime = report["runtimeInputs"]!.AsArray();
    Require((bool?)report["completed"] == true && (bool?)report["sameRuntimePayloadForBothScenarios"] == true &&
        (int?)report["historicalComparisons"] == 0 && (int?)report["publicationRows"] == 0 && (string?)report["historyStatus"] == "NoHistoricalPublications", "Incorrect scope/history completion: " + lane);
    Require(rows.Count == 3 && rows.All(row => (bool?)row!["passed"] == true) &&
        rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal).SequenceEqual(new[] { "CompleteUpgrade", "CoreReplacement", "Source" }), "Missing provider scenarios.");
    Require(frozen.Count == 4 && frozen.Select(row => row!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(
        new[] { ".dll", ".pdb", ".deps.json", ".runtimeconfig.json" }.Select(suffix => "CIS.Logging.ProviderContractConsumer" + suffix)), "Incomplete frozen consumer set.");
    Require(runtime.Count == 3 && runtime.Select(row => row!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(
        new[] { "CIS.Core.dll", "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll" }), "Incorrect runtime input set.");
    Require((string?)report["runnerSha256"] == Hash(Path.Combine(owner, "implementations/csharp/eng/CIS.DependencyChecker/MicrosoftLoggingCompatibilityChecks.cs")) &&
        (string?)report["wrapperSha256"] == Hash(Path.Combine(owner, "implementations/csharp/eng/check-microsoft-logging-compatibility.ps1")) &&
        (string?)report["toolchainSha256"] == Hash(Path.Combine(owner, "implementations/csharp/eng/toolchain.json")) &&
        (string?)report["ledgerSha256"] == Hash(Path.Combine(owner, "compatibility/csharp/publications/ledger.json")), "Provider runner/configuration pin differs.");
    Require(report["sourceInputs"]!.AsArray().Count == 3, "Fixture input set differs.");
    foreach (JsonNode? item in report["sourceInputs"]!.AsArray())
    { Require(Hash(Path.Combine(owner, "implementations/csharp/verification/compatibility/microsoft-logging", item!["path"]!.GetValue<string>())) == (string?)item["sha256"], "Fixture input changed."); }
    foreach (JsonNode? item in frozen)
    {
        string file = item!["name"]!.GetValue<string>(), hash = item["sha256"]!.GetValue<string>();
        foreach (string directory in new[] { "frozen", "source/bin/Release/net10.0", "CoreReplacement", "CompleteUpgrade" })
        { Require(Hash(Path.Combine(root, directory, file)) == hash, "Frozen consumer bytes differ."); }
    }
    foreach (JsonNode? row in rows)
    {
        string id = row!["id"]!.GetValue<string>(), directory = id == "Source" ? "source/bin/Release/net10.0" : id;
        JsonObject actual = row["actual"]!.AsObject();
        Require((string?)actual["contract"] == "passed" && (int?)actual["records"] == 3 && (string?)actual["runtime"] == "10.0.12" &&
            (string?)actual["core"]!["sha256"] == (string?)report["coreSha256"], "Wrong observed provider result.");
        Require((string?)row["consumerSha256"] == Hash(Path.Combine(root, directory, "CIS.Logging.ProviderContractConsumer.dll")), "Observed consumer differs.");
        foreach ((string key, string file) in new[] { ("core", "CIS.Core.dll"), ("logging", "Microsoft.Extensions.Logging.Abstractions.dll"), ("dependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"), ("consumer", "CIS.Logging.ProviderContractConsumer.dll") })
        {
            Require((string?)actual[key]!["sha256"] == Hash(Path.Combine(root, directory, file)), "Observed loaded bytes differ.");
            if (key != "consumer") { Require((string?)runtime.Single(item => (string?)item!["name"] == file)!["sha256"] == (string?)actual[key]!["sha256"], "Observed runtime input differs from its selected pin."); }
            string expected = lane == "windows" ? Path.GetFullPath(Path.Combine(root, directory, file)) :
                "/home/user/cislib-verification/ubuntu-developer-396e87d6-archive/source/.artifacts/corrective/provider-compatibility-migration-2026-10-18/compatibility-final/" + directory + "/" + file;
            Require((string?)actual[key]!["path"] == expected, "Observed loaded path differs.");
        }
        Require(new FileInfo(Path.Combine(root, id + ".stderr.txt")).Length == 0, "Consumer stderr was not empty.");
    }
    string destination = Path.Combine(receipt, lane + "-provider-compatibility.json");
    Require(!File.Exists(destination), "Preserve retained focused report.");
    File.Copy(path, destination);
    focused.Add(new JsonObject { ["lane"] = lane, ["scenarios"] = 3, ["policyCases"] = 42, ["reportSha256"] = Hash(path), ["unchangedConsumerInputs"] = 4, ["historicalComparisons"] = 0 });
}

var audit = new JsonObject { ["focusedProviderCompatibility"] = focused, ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["windowsBaseCommit"] = "683670ef2526fe2a1b78b957f16ceecdb8d8b6c4", ["taskChangesBoundByInputHashes"] = true, ["matchingCodeBuildConfigurationInputs"] = selected.Count, ["checkpoints"] = evidence, ["canonicalPublicationInputs"] = publicationInputs, ["archive"] = ".artifacts/corrective/provider-compatibility-migration-2026-10-18", ["limits"] = "Native worktrees retain different Git metadata and lane-local packages; this is not one-package cross-lane release qualification. Markdown documentation and the user-owned launchSettings.json are outside executable-input equality. Coverage, full shipping mutation and cumulative published compatibility were not repeated. The three explicit provider fixture scenarios are separate from Developer and do not qualify released historical compatibility." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());