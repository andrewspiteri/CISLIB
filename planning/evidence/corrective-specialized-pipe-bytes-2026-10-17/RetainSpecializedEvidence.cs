#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

string repo = Path.GetFullPath(args[0]);
string artifacts = Path.Combine(repo, ".artifacts/corrective/specialized-pipe-bytes-2026-10-17");
string receipt = Path.Combine(repo, "planning/evidence/corrective-specialized-pipe-bytes-2026-10-17");
string ubuntu = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
string windows = Path.Combine(repo, ".artifacts/csharp/focused/7866e824dad843a481e2f07cf2444cec/summary.json");
var native = new JsonArray();
foreach (string path in new[] { windows, Path.Combine(artifacts, "ubuntu-focused.json") })
{
    JsonObject result = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    JsonArray cases = result["cases"]!.AsArray();
    Require(result["passed"]!.GetValue<bool>() && cases.Count == 40 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete specialized selection.");
    Require(cases.Count(row => row!["Method"]!.GetValue<string>().StartsWith("CIS.Tests.IntegrationTests.ProcessStreamCaptureTests.", StringComparison.Ordinal)) == 9, "Native byte regressions absent.");
    native.Add(new JsonObject { ["summary"] = Path.GetFileName(Path.GetDirectoryName(path)), ["cases"] = cases.Count, ["methods"] = new JsonObject(cases.GroupBy(row => row!["Method"]!.GetValue<string>()).Select(group => new KeyValuePair<string, JsonNode?>(group.Key, JsonValue.Create(group.Count())))) });
}
var sources = new JsonArray();
foreach (string file in new[] { "CIS.Core.IntegrationTests/CandidatePackageFixture.cs", "CIS.Core.IntegrationTests/CandidateLoggingProcessTests.cs", "CIS.Core.IntegrationTests/StopLifecycleProcessTests.cs", "CIS.Core.IntegrationTests/StopLifecycleReaderControls.cs", "fixtures/DiscoveryHost/StopLifecycleExpectations.cs" })
{
    string path = "implementations/csharp/tests/" + file, hash = Hash(Path.Combine(repo, path));
    Require(hash == Hash(Path.Combine(ubuntu, path)), "Native source differs: " + path);
    sources.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve existing evidence.");
Directory.CreateDirectory(receipt);
File.Copy(windows, Path.Combine(receipt, "windows-specialized.json"));
File.Copy(Path.Combine(artifacts, "ubuntu-focused.json"), Path.Combine(receipt, "ubuntu-specialized.json"));
File.Copy(Path.Combine(artifacts, "RetainSpecializedEvidence.cs"), Path.Combine(receipt, "RetainSpecializedEvidence.cs"));
var audit = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["baseCommit"] = "b4a520c3", ["finding"] = "PHYSICAL-BYTES specialized follow-up", ["casesPerLane"] = 40, ["sourcesMatchPerLane"] = sources, ["nativeMethods"] = native, ["limits"] = "Scoped native Release integration tests with locally repacked Core consumers. Full Windows Developer passed immediately before these five test-only edits; no new full checkpoint, cross-lane identical package or release qualification is claimed." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), audit.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(audit.ToJsonString());
