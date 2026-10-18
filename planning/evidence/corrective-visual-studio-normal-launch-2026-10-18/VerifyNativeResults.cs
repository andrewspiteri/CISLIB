#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

string root = Path.GetFullPath(args.Single());
string artifacts = Path.Combine(root, ".artifacts/corrective/visual-studio-normal-launch-2026-10-18");
string evidence = Path.Combine(root, "planning/evidence/corrective-visual-studio-normal-launch-2026-10-18");
XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
XElement[] Results(string path, int expected, string outcome)
{
    XDocument doc = XDocument.Load(path);
    Require(doc.Root?.Name == ns + "TestRun", "Unexpected native report schema: " + path);
    XElement[] rows = doc.Descendants(ns + "UnitTestResult").ToArray();
    XElement counters = doc.Descendants(ns + "Counters").Single();
    Require(rows.Length == expected && rows.All(row => (string?)row.Attribute("outcome") == outcome), "Unexpected native cases: " + path);
    Require((int)counters.Attribute("total")! == expected && (int)counters.Attribute("executed")! == expected && (int)counters.Attribute("notExecuted")! == 0, "Unexpected native counters: " + path);
    Require((int)counters.Attribute(outcome == "Passed" ? "passed" : "failed")! == expected, "Unexpected outcome count: " + path);
    Require(rows.Select(row => (string?)row.Attribute("executionId")).Distinct().Count() == expected, "Duplicate native executions");
    return rows;
}
string Name(XElement row) => (string)row.Attribute("testName")!;
var before = Results(Path.Combine(evidence, "before-unit.trx"), 4, "Failed");
Require(before.All(row => row.Value.Contains("start process 'pwsh'", StringComparison.Ordinal)), "Baseline failed for another reason");
var after = Results(Path.Combine(evidence, "after-unit.trx"), 4, "Passed");
Require(before.Select(Name).Order(StringComparer.Ordinal).SequenceEqual(after.Select(Name).Order(StringComparer.Ordinal)), "Before/after native selections differ");
var ubuntu = Results(Path.Combine(evidence, "ubuntu-support.trx"), 11, "Passed");
string native = Path.Combine(artifacts, "normal-vs-all-final/normal-vs-all.trx");
var all = Results(native, 6858, "Passed");
XDocument full = XDocument.Load(native);
var definitions = full.Descendants(ns + "UnitTest").ToDictionary(row => (string)row.Attribute("id")!);
var suites = all.GroupBy(row => Path.GetFileNameWithoutExtension((string)definitions[(string)row.Attribute("testId")!].Attribute("storage")!))
    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
foreach (var expected in new Dictionary<string, int> { ["CIS.Core.UnitTests"] = 3750, ["CIS.Core.IntegrationTests"] = 2120, ["CIS.Core.ArchitectureTests"] = 853, ["CIS.Core.CompatibilityTests"] = 111, ["CIS.Core.BusinessCases"] = 24 })
{
    Require(suites.GetValueOrDefault(expected.Key) == expected.Value, "Unexpected suite inventory: " + expected.Key);
}
Require(suites.Count == 5, "Unexpected test suite");
using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "planning/evidence/corrective-visual-studio-environment-2026-10-18/verification.json")));
var savedNames = saved.RootElement.GetProperty("nativeReports").EnumerateArray().SelectMany(report => report.GetProperty("names").EnumerateArray()).Select(name => name.GetString()!).ToArray();
Require(savedNames.Length == 20 && savedNames.All(name => all.Count(row => Name(row) == name) == 1), "Saved IDE failure cases missing from the native run");
using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "normal-vs-runner-launch.json")));
var initial = launch.RootElement;
Require(initial.GetProperty("parentPathIsMachineOnly").GetBoolean() && !initial.GetProperty("parentHasUserAliasDirectory").GetBoolean(), "Native run used a repaired caller PATH");
Require(initial.GetProperty("useSharedCompilation").ValueKind == JsonValueKind.Null && initial.GetProperty("disableNodeReuse").ValueKind == JsonValueKind.Null, "Native run used external compiler overrides");
Require(Hash(Path.Combine(root, "implementations/csharp/tests/TestAssembly.cs")) == initial.GetProperty("testAssemblySetupSha256").GetString(), "Test startup source changed during verification");
foreach (var file in initial.GetProperty("sources").EnumerateArray()) { Require(Hash(file.GetProperty("path").GetString()!) == file.GetProperty("sha256").GetString(), "Native test assembly changed during execution"); }
using var exit = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifacts, "normal-vs-runner-exit.json")));
Require(exit.RootElement.GetProperty("exitCode").GetInt32() == 0, "Native runner exit failure");
var record = new
{
    recordedUtc = DateTime.UtcNow,
    baselineFailed = before.Length,
    baselineAllMissingPwsh = true,
    correctedUnitPassed = after.Length,
    normalVsRunnerPassed = all.Length,
    normalVsRunnerFailed = 0,
    normalVsRunnerSkipped = 0,
    normalVsRunnerSuites = suites,
    nativeReport = Path.GetRelativePath(root, native).Replace(Path.DirectorySeparatorChar, '/'),
    nativeReportSha256 = Hash(native),
    allTwentySavedFailuresPassed = true,
    callerPathIsUnrepairedMachineOnly = true,
    callerCompilerOverridesAbsent = true,
    testStartupSourceSha256 = Hash(Path.Combine(root, "implementations/csharp/tests/TestAssembly.cs")),
    ubuntuSupportPassed = ubuntu.Length,
    visualStudioUiExecuted = false,
    roadmapPaused = true
};
File.WriteAllText(Path.Combine(evidence, "verification.json"), JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Verified {all.Length} native Visual Studio runner cases and {ubuntu.Length} Ubuntu support cases; all 20 saved failures pass.");
