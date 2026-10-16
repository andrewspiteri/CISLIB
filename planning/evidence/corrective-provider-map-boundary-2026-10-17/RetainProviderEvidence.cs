#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]);
string artifacts = Path.Combine(repo, ".artifacts/corrective/provider-map-boundary-2026-10-17");
string developer = Path.Combine(repo, ".artifacts/csharp/verification/Developer");
string receipt = Path.Combine(repo, "planning/evidence/corrective-provider-map-boundary-2026-10-17");
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
string windowsFocus = Path.Combine(repo, ".artifacts/csharp/focused/114cefef1e604c2d9700dd3e776721ae/summary.json");
string ubuntuFocus = Path.Combine(artifacts, "ubuntu-focused.json");
var methods = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["ExhaustedMapBudgetOmitsInvalidKeysAndPreservesTheRetainedPrefix"] = 4,
    ["MaximumMapDepthOmitsInvalidKeysBeforeInspectingTheirValues"] = 2,
    ["InvalidRetainedMapKeysStillRejectCapture"] = 2
};
foreach (string path in new[] { windowsFocus, ubuntuFocus })
{
    JsonObject focus = Read(path);
    Require(focus["passed"]!.GetValue<bool>() && focus["cases"]!.AsArray().Count == 208, "Provider selection did not pass completely.");
    foreach (var method in methods)
        Require(focus["cases"]!.AsArray().Count(row => row!["Method"]!.GetValue<string>() == "CIS.Tests.UnitTests.ProviderStateBoundaryTests." + method.Key && row["Outcome"]!.GetValue<string>() == "Passed") == method.Value, "Boundary cases missing: " + method.Key);
}
string red = Path.Combine(repo, ".artifacts/csharp/focused/0bd47744bd8a45168dc24bad8f7266cb/selected.trx");
XDocument redReport = XDocument.Load(red);
XNamespace ns = redReport.Root!.Name.Namespace;
XElement redCounts = redReport.Descendants(ns + "Counters").Single();
Require((string?)redCounts.Attribute("failed") == "4" && (string?)redCounts.Attribute("passed") == "4" && (string?)redCounts.Attribute("total") == "8", "Pre-fix counterexample differs.");
Require(redReport.Descendants(ns + "UnitTestResult").Where(row => (string?)row.Attribute("outcome") == "Failed").All(row => row.Value.Contains("Use valid ordinal attribute keys.", StringComparison.Ordinal)), "Pre-fix failures have another cause.");
JsonObject summary = Read(Path.Combine(developer, "summary.json")), validation = Read(Path.Combine(developer, "validation.json"));
Require(summary["runId"]!.GetValue<string>() == "880f143009fb4bef9f2886e7dfac9add" && summary["exitCode"]!.GetValue<int>() == 0 && summary["finishedUtc"] is not null && validation["exitCode"]!.GetValue<int>() == 0, "Developer checkpoint incomplete or failed.");
JsonArray inputs = JsonNode.Parse(File.ReadAllBytes(Path.Combine(developer, "source-inputs.json")))!.AsArray();
string[] changed = ["implementations/csharp/src/CIS.Core/Logging/MicrosoftLogger/ProviderStateCapture.cs", "implementations/csharp/tests/CIS.Core.UnitTests/ProviderStateBoundaryTests.cs", "conformance/logging/case-inventory.json"];
var source = new JsonArray();
foreach (string path in changed)
{
    string hash = Hash(Path.Combine(repo, path));
    Require(inputs.Single(row => row!["path"]!.GetValue<string>() == path)!["sha256"]!.GetValue<string>() == hash, "Checkpoint source differs: " + path);
    source.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Receipt already exists; preserve it.");
Directory.CreateDirectory(receipt);
foreach (var file in new[] { (windowsFocus, "windows-provider.json"), (ubuntuFocus, "ubuntu-provider.json"), (red, "before-fix.trx"), (Path.Combine(developer, "summary.json"), "windows-developer-summary.json"), (Path.Combine(developer, "validation.json"), "windows-developer-validation.json") })
    File.Copy(file.Item1, Path.Combine(receipt, file.Item2));
File.Copy(Path.Combine(artifacts, "RetainProviderEvidence.cs"), Path.Combine(receipt, "RetainProviderEvidence.cs"));
JsonObject record = new() { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["finding"] = "PROVIDER-BUDGET", ["baseCommit"] = "da992dfff9e77963b269ed2a121ecfed517a0e0a", ["windowsDeveloperRun"] = summary["runId"]!.DeepClone(), ["focusedProviderCasesPerLane"] = 208, ["boundaryCasesPerLane"] = 8, ["preFixFailed"] = 4, ["preFixPassed"] = 4, ["source"] = source, ["limits"] = "Windows Developer plus native Ubuntu focused provider cases. No new full Ubuntu, coverage, mutation, shared exact-package, external rereview or release qualification. Receipt metadata is later than captured source." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(record.ToJsonString());
