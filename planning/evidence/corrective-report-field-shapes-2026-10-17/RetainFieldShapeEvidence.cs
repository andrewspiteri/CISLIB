#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]), artifacts = Path.Combine(repo, ".artifacts/corrective/report-field-shapes-2026-10-17"), receipt = Path.Combine(repo, "planning/evidence/corrective-report-field-shapes-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var copies = new List<(string Path, string Name)>();
foreach (var run in new[] { ("windows", "unit", "44e4526ced5443a09a4a0c234f0fb1ca", 78), ("windows", "integration", "3463d387cd034c86a7098165ee20c3e9", 135), ("ubuntu", "unit", "", 78), ("ubuntu", "integration", "", 135) })
{
    string path = run.Item1 == "windows" ? Path.Combine(repo, ".artifacts/csharp/focused", run.Item3, "summary.json") : Path.Combine(artifacts, "ubuntu-" + run.Item2 + ".json");
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == run.Item4 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete shape selection.");
    if (run.Item2 == "unit") Require(cases.Count(row => row!["Method"]!.GetValue<string>().Contains(".RenamingANullFaultCannotPreserve", StringComparison.Ordinal)) == 2, "Null substitution controls absent.");
    else Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.IntegrationTests.ProviderReaderCatalogTests.ProtectedIdentityMustRemainPresentAndNonNull") == 8, "Protected identity controls absent.");
    copies.Add((path, run.Item1 + "-" + run.Item2 + ".json"));
}
foreach (var red in new[] { ("before-unit.trx", 2), ("before-provider.trx", 8) })
{
    string path = Path.Combine(artifacts, red.Item1); XDocument report = XDocument.Load(path); XNamespace ns = report.Root!.Name.Namespace;
    Require((string?)report.Descendants(ns + "Counters").Single().Attribute("failed") == red.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture) && report.Descendants(ns + "Message").All(message => message.Value.Contains("but no exception was thrown", StringComparison.Ordinal)), "Counterexample had another cause.");
    copies.Add((path, red.Item1));
}
var sources = new JsonArray();
foreach (string path in new[] { "implementations/csharp/tests/CIS.Core.UnitTests/DiagnosticsTests.cs", "implementations/csharp/tests/CIS.Core.UnitTests/OutputStateTests.cs", "implementations/csharp/tests/CIS.Core.IntegrationTests/ProviderReaderCatalogTests.cs", "implementations/csharp/tests/fixtures/DiscoveryHost/DiagnosticsExpectations.cs", "implementations/csharp/tests/fixtures/DiscoveryHost/OutputStateExpectations.cs", "implementations/csharp/tests/fixtures/DiscoveryHost/ProviderOperationExpectations.cs", "implementations/csharp/tests/fixtures/DiscoveryHost/ProviderHostStoryExpectations.cs", "conformance/logging/case-inventory.json" })
{
    string hash = Hash(Path.Combine(repo, path)); Require(hash == Hash(Path.Combine(ubuntu, path)), "Native input differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve existing evidence."); Directory.CreateDirectory(receipt);
foreach (var copy in copies) File.Copy(copy.Path, Path.Combine(receipt, copy.Name));
File.Copy(Path.Combine(artifacts, "RetainFieldShapeEvidence.cs"), Path.Combine(receipt, "RetainFieldShapeEvidence.cs"));
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "b46857f5", ["finding"] = "EXACT-SHAPES diagnostic/state/provider subset", ["unitPerLane"] = 78, ["integrationPerLane"] = 135, ["newUnitPerLane"] = 2, ["newIntegrationPerLane"] = 8, ["sourcesMatchPerLane"] = sources, ["limits"] = "Configuration shape checks remain a separate subset. Focused native checks, not repeated full checkpoint or release gates." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
