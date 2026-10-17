#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

string repo = Path.GetFullPath(args[0]);
string native = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
string work = Path.Combine(repo, ".artifacts/corrective/final-confirmed-findings-2026-10-17");
string receipt = Path.Combine(repo, "planning/evidence/corrective-confirmed-findings-checkpoint-2026-10-17");
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
foreach ((string lane, string owner, string runId) in new[] { ("windows", repo, "62600e40937143a5aa54ba83a7e6edc8"), ("ubuntu", native, "70cff1a75ea14830858ddc2aad641b87") })
{
    string root = Path.Combine(owner, ".artifacts/csharp/verification/Developer");
    JsonObject summary = Read(Path.Combine(root, "summary.json"));
    Require((string?)summary["runId"] == runId && summary["finishedUtc"] is not null && (int?)summary["exitCode"] == 0 && summary["issues"]!.AsArray().Count == 0, "Incomplete or failed checkpoint: " + lane);
    Require((string?)summary["reconciliation"]!["status"] == "Produced", "Missing required-case reconciliation.");
    Require(summary["stages"]!.AsArray().All(stage => (string?)stage!["status"] == "Passed" && (int?)stage["exitCode"] == 0), "Failed stage.");
    int total = 0, executedFamilies = 0;
    var families = new JsonArray();
    foreach (JsonNode? family in summary["families"]!.AsArray())
    {
        string relative = family!["execution"]!["path"]!.GetValue<string>();
        string path = Path.Combine(root, relative);
        Require(Hash(path) == (string?)family["execution"]!["sha256"], "Execution reference hash differs.");
        JsonObject execution = Read(path);
        string id = family["id"]!.GetValue<string>();
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
foreach (JsonNode? item in inputs)
{
    string path = item!["path"]!.GetValue<string>();
    bool maintained = path.StartsWith("implementations/csharp/", StringComparison.Ordinal) || path.StartsWith("eng/", StringComparison.Ordinal);
    bool executableInput = new[] { ".cs", ".csproj", ".props", ".targets", ".ps1", ".json", ".slnx" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    if (!maintained || !executableInput || path.EndsWith("/Properties/launchSettings.json", StringComparison.Ordinal)) continue;
    string expected = item["sha256"]!.GetValue<string>();
    Require(Hash(Path.Combine(repo, path)) == expected && Hash(Path.Combine(native, path)) == expected, "Code/build/configuration inputs differ: " + path);
    selected.Add(item.DeepClone());
}
Require(selected.Count == 749, "Unexpected executable input selection.");
Require(!Directory.Exists(receipt), "Preserve previous receipt.");
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
File.Copy(Path.Combine(work, "RetainCheckpointEvidence.cs"), Path.Combine(receipt, "RetainCheckpointEvidence.cs"));
File.WriteAllText(Path.Combine(receipt, "matching-executable-inputs.json"), selected.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["codeCommit"] = "d550252c7c772e208170480f7c4b1005ab08f7ab", ["matchingCodeBuildConfigurationInputs"] = selected.Count, ["checkpoints"] = evidence, ["archive"] = ".artifacts/corrective/final-confirmed-findings-2026-10-17", ["limits"] = "Native worktrees retain different Git metadata and lane-local packages; this is not one-package cross-lane release qualification. Planning documentation and the user-owned launchSettings.json are outside executable-input equality. Coverage, mutation and historical compatibility were not repeated." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());