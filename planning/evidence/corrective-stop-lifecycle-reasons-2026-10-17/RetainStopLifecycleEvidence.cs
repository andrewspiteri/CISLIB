#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repo = Path.GetFullPath(args[0]), artifacts = Path.Combine(repo, ".artifacts/corrective/stop-lifecycle-reasons-2026-10-17"), receipt = Path.Combine(repo, "planning/evidence/corrective-stop-lifecycle-reasons-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var copies = new List<(string Path, string Name)>(); var controls = new JsonObject();
foreach (var run in new[] { ("windows", "integration", "01b1360f48af4ae0a0cf209561310ad6", 11), ("windows", "unit", "1959de2b47934150bfb96df6c3ff046c", 4), ("ubuntu", "integration", "", 11), ("ubuntu", "unit", "", 4) })
{
    string path = run.Item1 == "windows" ? Path.Combine(repo, ".artifacts/csharp/focused", run.Item3, "summary.json") : Path.Combine(artifacts, "ubuntu-" + run.Item2 + ".json");
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == run.Item4 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete lifecycle selection.");
    copies.Add((path, run.Item1 + "-" + run.Item2 + ".json"));
    if (run.Item2 != "integration") continue;
    string trx = run.Item1 == "windows" ? Path.Combine(Path.GetDirectoryName(path)!, "selected.trx") : Path.Combine(artifacts, "ubuntu-integration.trx");
    XDocument report = XDocument.Load(trx); XNamespace ns = report.Root!.Name.Namespace;
    string[] ids = report.Descendants(ns + "StdOut").SelectMany(output => Regex.Matches(output.Value, @"Rejected lifecycle control: ([^\r\n]+)").Select(match => match.Groups[1].Value.Trim())).ToArray();
    Require(ids.Length == 41 && ids.Distinct(StringComparer.Ordinal).Count() == 41, "Not all 41 distinct reason controls executed.");
    controls[run.Item1] = new JsonArray(ids.Order(StringComparer.Ordinal).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
}
Require(JsonNode.DeepEquals(controls["windows"], controls["ubuntu"]), "Native control sets differ.");
var sources = new JsonArray();
foreach (string file in new[] { "CIS.Core.IntegrationTests/StopLifecycleReaderControls.cs", "fixtures/DiscoveryHost/StopLifecycleExpectations.cs" })
{
    string path = "implementations/csharp/tests/" + file, hash = Hash(Path.Combine(repo, path)); Require(hash == Hash(Path.Combine(ubuntu, path)), "Native input differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve existing evidence."); Directory.CreateDirectory(receipt);
foreach (var copy in copies) File.Copy(copy.Path, Path.Combine(receipt, copy.Name));
File.Copy(Path.Combine(artifacts, "RetainStopLifecycleEvidence.cs"), Path.Combine(receipt, "RetainStopLifecycleEvidence.cs"));
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "40591f0b", ["finding"] = "REJECTION-REASONS T05 completion", ["integrationPerLane"] = 11, ["unitPerLane"] = 4, ["reasonControlsPerLane"] = 41, ["namedExistingAssertions"] = 67, ["executedControlIds"] = controls, ["sourcesMatchPerLane"] = sources, ["limits"] = "Focused native checks; no repeated full checkpoint, coverage, mutation, compatibility or release qualification." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
