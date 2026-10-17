#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]), artifacts = Path.Combine(repo, ".artifacts/corrective/configuration-shapes-2026-10-17"), receipt = Path.Combine(repo, "planning/evidence/corrective-configuration-shapes-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
string windows = Path.Combine(repo, ".artifacts/csharp/focused/334ff0ef67dd4827b932b8fdef3ded8e/summary.json");
foreach (string path in new[] { windows, Path.Combine(artifacts, "ubuntu-focused.json") })
{
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == 80 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete configuration selection.");
    foreach (var method in new[] { ("ConfigurationValidationProcessTests.DeclaredNullFactsCannotOmitTheirMember", 1), ("ConfigurationValidationProcessTests.ExpectedValuesCannotSilentlyUseTheWrongShape", 3), ("EnvironmentOptionsProcessTests.ContainerShapeDistinguishesMissingNullFactsAndExcludedLogging", 3) })
        Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.IntegrationTests." + method.Item1) == method.Item2, "New reader controls absent.");
}
string redPath = Path.Combine(artifacts, "before-fix.trx"); XDocument red = XDocument.Load(redPath); XNamespace ns = red.Root!.Name.Namespace;
Require((string?)red.Descendants(ns + "Counters").Single().Attribute("failed") == "7" && red.Descendants(ns + "Message").All(message => message.Value.Contains("but no exception was thrown", StringComparison.Ordinal)), "Red failures had another cause.");
var sources = new JsonArray();
foreach (string path in new[] { "implementations/csharp/tests/CIS.Core.IntegrationTests/FixtureProcessChecks.cs", "implementations/csharp/tests/CIS.Core.IntegrationTests/ConfigurationProcessTests.cs", "implementations/csharp/tests/CIS.Core.IntegrationTests/ConfigurationPathProcessTests.cs", "implementations/csharp/tests/CIS.Core.IntegrationTests/ConfigurationValidationProcessTests.cs", "implementations/csharp/tests/CIS.Core.IntegrationTests/EnvironmentOptionsProcessTests.cs", "conformance/logging/case-inventory.json" })
{
    string hash = Hash(Path.Combine(repo, path)); Require(hash == Hash(Path.Combine(ubuntu, path)), "Native input differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve existing evidence."); Directory.CreateDirectory(receipt);
File.Copy(windows, Path.Combine(receipt, "windows-configuration.json"));
File.Copy(Path.Combine(artifacts, "ubuntu-focused.json"), Path.Combine(receipt, "ubuntu-configuration.json"));
File.Copy(redPath, Path.Combine(receipt, "before-fix.trx"));
File.Copy(Path.Combine(artifacts, "RetainConfigurationShapeEvidence.cs"), Path.Combine(receipt, "RetainConfigurationShapeEvidence.cs"));
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "9341ffff", ["finding"] = "EXACT-SHAPES configuration completion", ["selectedIntegrationPerLane"] = 80, ["newReaderControlsPerLane"] = 7, ["sourcesMatchPerLane"] = sources, ["limits"] = "Seven DTO reader controls are not additional physical child scenarios. Focused native checks, not repeated full checkpoint or release gates." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
