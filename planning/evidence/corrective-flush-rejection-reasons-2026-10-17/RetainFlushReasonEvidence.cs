#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]);
string artifacts = Path.Combine(repo, ".artifacts/corrective/flush-rejection-reasons-2026-10-17");
string receipt = Path.Combine(repo, "planning/evidence/corrective-flush-rejection-reasons-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var copies = new List<(string Path, string Name)>();
foreach (var selection in new[] { ("windows", "integration", "add5a28e14c14e2c850cad82533d53de", 129), ("windows", "unit", "3bfea1eb3bf44b7b8940f6208e831905", 63), ("ubuntu", "integration", "", 129), ("ubuntu", "unit", "", 63) })
{
    string path = selection.Item1 == "windows" ? Path.Combine(repo, ".artifacts/csharp/focused", selection.Item3, "summary.json") : Path.Combine(artifacts, "ubuntu-" + selection.Item2 + ".json");
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == selection.Item4 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete flush selection.");
    if (selection.Item2 == "integration") Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.IntegrationTests.FlushReaderMigrationTests.OriginalFlushReaderCorruptionsAreRejectedAfterPositivePhysicalOutput") == 88, "Not all 88 reason controls executed.");
    copies.Add((path, selection.Item1 + "-" + selection.Item2 + ".json"));
}
string redPath = Path.Combine(artifacts, "before-named-reasons.trx");
XDocument red = XDocument.Load(redPath); XNamespace ns = red.Root!.Name.Namespace;
Require((string?)red.Descendants(ns + "Counters").Single().Attribute("failed") == "31", "Red result differs.");
var failures = new JsonArray(); int generic = 0;
foreach (XElement row in red.Descendants(ns + "UnitTestResult").Where(row => (string?)row.Attribute("outcome") == "Failed"))
{
    string message = row.Descendants(ns + "Message").Single().Value;
    string actual = Regex.Match(message, "actual:\\s+\"([^\"]+)\"").Groups[1].Value;
    if (actual is "Public flush expectation failed." or "Flush stress report expectation failed.") generic++;
    else Require(((string?)row.Attribute("testName"))!.Contains("\"freeze\"", StringComparison.Ordinal) && actual == "covered dispositions", "Unexpected red failure.");
    failures.Add(new JsonObject { ["name"] = (string?)row.Attribute("testName"), ["actualReason"] = actual });
}
Require(generic == 30, "Expected 30 generic reason counterexamples.");
var sources = new JsonArray();
foreach (string file in new[] { "CIS.Core.IntegrationTests/FlushReaderMigrationTests.cs", "fixtures/DiscoveryHost/FlushResultExpectations.cs", "fixtures/DiscoveryHost/FlushStressExpectations.cs" })
{
    string path = "implementations/csharp/tests/" + file, hash = Hash(Path.Combine(repo, path)); Require(hash == Hash(Path.Combine(ubuntu, path)), "Native input differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve existing evidence."); Directory.CreateDirectory(receipt);
foreach (var copy in copies) File.Copy(copy.Path, Path.Combine(receipt, copy.Name));
File.Copy(Path.Combine(artifacts, "RetainFlushReasonEvidence.cs"), Path.Combine(receipt, "RetainFlushReasonEvidence.cs"));
File.WriteAllText(Path.Combine(receipt, "before-named-reasons.json"), new JsonObject { ["rawTrxSha256"] = Hash(redPath), ["failed"] = 31, ["passed"] = 57, ["genericMessageFailures"] = generic, ["expectationLabelCorrection"] = "The freeze control changes an original result disposition and is now bound to covered dispositions; its initial expected label was incorrect.", ["failures"] = failures }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "71fada22", ["finding"] = "REJECTION-REASONS flush subset", ["nativeIntegrationPerLane"] = 129, ["nativeUnitPerLane"] = 63, ["reasonControlsPerLane"] = 88, ["namedExistingAssertions"] = 47, ["sourcesMatchPerLane"] = sources, ["limits"] = "Focused native tests; lifecycle/cleanup/creation reason corrections remain pending. No repeated full checkpoint, coverage, mutation, compatibility or release qualification." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
