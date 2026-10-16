#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]);
string artifacts = Path.Combine(repo, ".artifacts/corrective/source-inventory-alias-2026-10-17");
string receipt = Path.Combine(repo, "planning/evidence/corrective-source-inventory-alias-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
string windows = Path.Combine(repo, ".artifacts/csharp/focused/7f67d8351e204779ae2cc2ac99b8cb0b/summary.json");
foreach (string path in new[] { windows, Path.Combine(artifacts, "ubuntu-focused.json") })
{
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == 7 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete inventory selection.");
    Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.ArchitectureTests.SourceInventoryTests.AliasToGitRootKeepsIgnoredFilesExcludedAndIndexBytesUnchanged") == 1, "Alias case absent.");
}
XDocument red = XDocument.Load(Path.Combine(artifacts, "ubuntu-before-fix.trx"));
XNamespace ns = red.Root!.Name.Namespace;
Require((string?)red.Descendants(ns + "Counters").Single().Attribute("failed") == "1" && red.Descendants(ns + "UnitTestResult").Single().Value.Contains("ignored.txt", StringComparison.Ordinal), "Pre-fix failure differs.");
var sources = new JsonArray();
foreach (string path in new[] { "implementations/csharp/eng/CIS.DependencyChecker/SourceInventory.cs", "implementations/csharp/tests/CIS.Core.ArchitectureTests/SourceInventoryTests.cs", "conformance/logging/case-inventory.json" })
{
    string hash = Hash(Path.Combine(repo, path)); Require(hash == Hash(Path.Combine(ubuntu, path)), "Native input differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
JsonObject group = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repo, "conformance/logging/case-inventory.json")))!["cases"]!.AsArray().Single(row => (string?)row!["id"] == "ARCH-VERIFICATION-SOURCE-INVENTORY")!.AsObject();
Require(group["minimumExecutions"]!.GetValue<int>() == 7 && group["bindings"]!.AsArray().Count == 7, "Required native inventory bindings differ.");
Require(!Directory.Exists(receipt), "Preserve existing evidence."); Directory.CreateDirectory(receipt);
File.Copy(windows, Path.Combine(receipt, "windows-inventory.json"));
File.Copy(Path.Combine(artifacts, "ubuntu-focused.json"), Path.Combine(receipt, "ubuntu-inventory.json"));
File.Copy(Path.Combine(artifacts, "ubuntu-before-fix.trx"), Path.Combine(receipt, "ubuntu-before-fix.trx"));
File.Copy(Path.Combine(artifacts, "RetainInventoryAliasEvidence.cs"), Path.Combine(receipt, "RetainInventoryAliasEvidence.cs"));
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "19f4b9c9", ["finding"] = "INVENTORY-ALIAS", ["casesPerLane"] = 7, ["newAliasCasePerLane"] = 1, ["sourcesMatchPerLane"] = sources, ["limits"] = "Native focused architecture tests and tool/bootstrap controls. No repeated complete checkpoint or release qualification. Receipt/status documentation follows test execution." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
