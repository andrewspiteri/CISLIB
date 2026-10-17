#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]), artifacts = Path.Combine(repo, ".artifacts/corrective/exception-oracles-2026-10-17"), receipt = Path.Combine(repo, "planning/evidence/corrective-exception-oracles-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var copies = new List<(string Path, string Name)>();
foreach (var run in new[] { ("windows", "unit", "d7f57fbad6f2442a8738a386dae27a6f", 445), ("windows", "integration", "9afa115eef6b4a869db8d666bc089d80", 129), ("ubuntu", "unit", "", 445), ("ubuntu", "integration", "", 129) })
{
    string path = run.Item1 == "windows" ? Path.Combine(repo, ".artifacts/csharp/focused", run.Item3, "summary.json") : Path.Combine(artifacts, "ubuntu-" + run.Item2 + ".json");
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == run.Item4 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete exception selection.");
    if (run.Item2 == "unit")
    {
        foreach (var method in new[] { ("ExceptionQualificationTests.ObserverCountsRequireIntegerJsonTokens", 7), ("ExceptionQualificationTests.ErrorBindingsMustExistEvenWhenTheReportDiagnosticsAreAlsoDamaged", 5), ("ExceptionQualificationTests.NullExpectedFactsMustBePresentRatherThanImplicitlyMissing", 2), ("ExceptionContentTests.OmittedSourcesKeepTheirIndependentProbeIdentityAndZeroReads", 3) })
            Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.UnitTests." + method.Item1) == method.Item2, "New unit cases absent.");
    }
    else Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.IntegrationTests.ExceptionProjectionProcessTests.NullProjectionRequiresItsExplicitExceptionMember") == 1, "Projection control absent.");
    copies.Add((path, run.Item1 + "-" + run.Item2 + ".json"));
}
foreach (var red in new[] { ("before-unit.trx", 14), ("before-projection.trx", 1) })
{
    string path = Path.Combine(artifacts, red.Item1); XDocument report = XDocument.Load(path); XNamespace ns = report.Root!.Name.Namespace;
    Require((string?)report.Descendants(ns + "Counters").Single().Attribute("failed") == red.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture), "Red result differs.");
    copies.Add((path, red.Item1));
}
var sources = new JsonArray();
foreach (string path in new[] { "implementations/csharp/tests/fixtures/DiscoveryHost/ExceptionContentExpectations.cs", "implementations/csharp/tests/fixtures/DiscoveryHost/ExceptionQualificationExpectations.cs", "implementations/csharp/tests/CIS.Core.UnitTests/ExceptionQualificationTests.cs", "implementations/csharp/tests/CIS.Core.UnitTests/ExceptionContentTests.cs", "implementations/csharp/tests/CIS.Core.IntegrationTests/ExceptionProjectionProcessTests.cs", "conformance/logging/case-inventory.json" })
{
    string hash = Hash(Path.Combine(repo, path)); Require(hash == Hash(Path.Combine(ubuntu, path)), "Native input differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve existing evidence."); Directory.CreateDirectory(receipt);
foreach (var copy in copies) File.Copy(copy.Path, Path.Combine(receipt, copy.Name));
File.Copy(Path.Combine(artifacts, "RetainExceptionEvidence.cs"), Path.Combine(receipt, "RetainExceptionEvidence.cs"));
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "b558e774", ["finding"] = "EXCEPTION-ORACLES", ["unitPerLane"] = 445, ["integrationPerLane"] = 129, ["newUnitPerLane"] = 17, ["newIntegrationPerLane"] = 1, ["sourcesMatchPerLane"] = sources, ["limits"] = "Focused native checks, not a repeated full checkpoint, coverage, mutation, compatibility or release qualification." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
