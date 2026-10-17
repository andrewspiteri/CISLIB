#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

string repo = Path.GetFullPath(args[0]), artifacts = Path.Combine(repo, ".artifacts/corrective/lifecycle-rejection-reasons-2026-10-17"), receipt = Path.Combine(repo, "planning/evidence/corrective-lifecycle-rejection-reasons-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var copies = new List<(string Path, string Name)>();
foreach (var run in new[] { ("windows", "integration", "479f1085cf134e3588bcff1b5f017839", 177), ("windows", "unit", "fb6c719f3d1246a6801a101efb6d98a6", 47), ("ubuntu", "integration", "", 177), ("ubuntu", "unit", "", 47) })
{
    string path = run.Item1 == "windows" ? Path.Combine(repo, ".artifacts/csharp/focused", run.Item3, "summary.json") : Path.Combine(artifacts, "ubuntu-" + run.Item2 + ".json");
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == run.Item4 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete lifecycle selection.");
    if (run.Item2 == "integration") Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.IntegrationTests.StopReaderMigrationTests.MigratedShutdownReaderRejectsTheOriginalCorruption") == 116, "Not all 116 exact-reason controls executed.");
    copies.Add((path, run.Item1 + "-" + run.Item2 + ".json"));
}
var sources = new JsonArray();
foreach (string file in new[] { "CIS.Core.IntegrationTests/StopReaderMigrationTests.cs", "fixtures/DiscoveryHost/StopStartExpectations.cs", "fixtures/DiscoveryHost/StopDrainExpectations.cs", "fixtures/DiscoveryHost/StopJoinExpectations.cs", "fixtures/DiscoveryHost/StopCleanupExpectations.cs", "fixtures/DiscoveryHost/RuntimeCreateExpectations.cs" })
{
    string path = "implementations/csharp/tests/" + file, hash = Hash(Path.Combine(repo, path)); Require(hash == Hash(Path.Combine(ubuntu, path)), "Native input differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve existing evidence."); Directory.CreateDirectory(receipt);
foreach (var copy in copies) File.Copy(copy.Path, Path.Combine(receipt, copy.Name));
File.Copy(Path.Combine(artifacts, "RetainLifecycleEvidence.cs"), Path.Combine(receipt, "RetainLifecycleEvidence.cs"));
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "8c2bf707", ["finding"] = "REJECTION-REASONS shutdown/creation subset", ["integrationPerLane"] = 177, ["unitPerLane"] = 47, ["reasonControlsPerLane"] = 116, ["namedExistingAssertions"] = 119, ["newExplicitTypeGuards"] = 1, ["sourcesMatchPerLane"] = sources, ["limits"] = "Focused native checks; separate T05 lifecycle reader reasons remain pending. No repeated full checkpoint, coverage, mutation, compatibility or release qualification." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
