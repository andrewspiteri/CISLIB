#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

string repo = Path.GetFullPath(args[0]);
string artifacts = Path.Combine(repo, ".artifacts/corrective/physical-pipe-bytes-2026-10-17");
string developer = Path.Combine(repo, ".artifacts/csharp/verification/Developer");
string receipt = Path.Combine(repo, "planning/evidence/corrective-physical-pipe-bytes-2026-10-17");
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var methods = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["OriginalBytesSurviveWhileTextKeepsItsExistingBomDecoding"] = 2,
    ["PhysicalReportExtractionUsesByteOffsetsAndRetainsBothOriginalStreams"] = 2,
    ["JsonlFramingRejectsTheOriginalBomOnEitherPipe"] = 2,
    ["MalformedUtf8StillFailsStrictTextDecodingOnEitherPipe"] = 2,
    ["BothPipesDrainConcurrentlyBeyondTheirNativeBufferCapacity"] = 1
};
foreach (string lane in new[] { "windows", "ubuntu" })
{
    JsonObject focus = Read(Path.Combine(artifacts, lane + "-focused.json"));
    JsonArray cases = focus["cases"]!.AsArray();
    Require(focus["passed"]!.GetValue<bool>() && cases.Count == 388 && cases.All(row => (string?)row!["Outcome"] == "Passed"), "Incomplete focused selection: " + lane);
    foreach (var method in methods)
        Require(cases.Count(row => (string?)row!["Method"] == "CIS.Tests.IntegrationTests.ProcessStreamCaptureTests." + method.Key) == method.Value, "Missing raw-byte regression: " + method.Key);
}
JsonObject summary = Read(Path.Combine(developer, "summary.json")), validation = Read(Path.Combine(developer, "validation.json"));
Require((string?)summary["runId"] == "f4dea9ad6d784539bb4234143190a334" && summary["exitCode"]!.GetValue<int>() == 0 && summary["finishedUtc"] is not null && validation["exitCode"]!.GetValue<int>() == 0 && validation["issues"]!.AsArray().Count == 0, "Developer checkpoint incomplete or failed.");
int total = 0;
foreach (string family in new[] { "unit", "integration", "architecture", "business" })
{
    JsonObject result = Read(Path.Combine(developer, "families", family, "execution.json"));
    Require(result["failed"]!.GetValue<int>() == 0 && result["skipped"]!.GetValue<int>() == 0 && result["passed"]!.GetValue<int>() == result["executed"]!.GetValue<int>(), "Native family failed: " + family);
    total += result["passed"]!.GetValue<int>();
}
Require(total == 6460, "Unexpected Developer count.");
JsonArray inputs = JsonNode.Parse(File.ReadAllBytes(Path.Combine(developer, "source-inputs.json")))!.AsArray();
string[] changed = ["tests/CIS.Core.TestSupport/ProcessRunner.cs", "tests/CIS.Core.IntegrationTests/FixtureProcessChecks.cs", "tests/CIS.Core.IntegrationTests/ProviderReportProcess.cs", "tests/CIS.Core.IntegrationTests/ProcessStreamCaptureTests.cs", "tests/fixtures/DiscoveryHost/Program.cs", "tests/fixtures/DiscoveryHost/ProcessStreamFixture.cs", "tests/fixtures/DiscoveryHost/ProviderHostFilterExpectations.cs"];
var source = new JsonArray();
foreach (string path in changed.Select(path => "implementations/csharp/" + path).Append("conformance/logging/case-inventory.json"))
{
    string hash = Hash(Path.Combine(repo, path));
    Require((string?)inputs.Single(row => (string?)row!["path"] == path)!["sha256"] == hash, "Checkpoint source differs: " + path);
    source.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
}
Require(!Directory.Exists(receipt), "Preserve the existing receipt.");
Directory.CreateDirectory(receipt);
foreach (string lane in new[] { "windows", "ubuntu" }) File.Copy(Path.Combine(artifacts, lane + "-focused.json"), Path.Combine(receipt, lane + "-physical.json"));
foreach (string file in new[] { "summary.json", "validation.json" }) File.Copy(Path.Combine(developer, file), Path.Combine(receipt, "windows-developer-" + file));
File.Copy(Path.Combine(artifacts, "RetainPhysicalBytesEvidence.cs"), Path.Combine(receipt, "RetainPhysicalBytesEvidence.cs"));
var record = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["finding"] = "PHYSICAL-BYTES", ["baseCommit"] = summary["source"]!["commit"]!.DeepClone(), ["windowsDeveloperRun"] = summary["runId"]!.DeepClone(), ["windowsDeveloperCases"] = total, ["focusedCasesPerLane"] = 388, ["newRegressionCasesPerLane"] = 9, ["source"] = source, ["limits"] = "Windows Developer and native Ubuntu focused console/provider checks. No full Ubuntu, coverage, mutation, compatibility, shared exact-package, external rereview or release qualification. Receipt metadata is later than the captured source." };
File.WriteAllText(Path.Combine(receipt, "audit.json"), record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(record.ToJsonString());
