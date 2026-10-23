using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

const string lane = "C:/miscwork/portfolio/cislib/planning/healthcheck/evidence/HC12/execution-repair-2026-10-23/capture72";
string actual = Path.Combine(lane, "actual61");
string[] expected = ["descriptor.json", "origin-validation.json", "wrapper-controls.json", "outer/started", "outer/finished", "outer/exit", "outer/console.log", "outer/inputs-before.sha256", "outer/inputs-after.sha256"];
var imported = new List<object>();
string[] lines = File.ReadAllLines(Path.Combine(actual, "physical-pins.sha256"));
Need(lines.Length == expected.Length, "Wrong compact physical-pin count.");
foreach (string line in lines)
{
    string hash = line[..64], relative = line[66..]; Need(expected.Contains(relative, StringComparer.Ordinal), "Unexpected compact import member.");
    string path = Path.Combine(actual, relative); Need(Hash(path) == hash, "Compact import physical pin differs."); imported.Add(Pin(path));
}
Need(lines.Select(line => line[66..]).Distinct(StringComparer.Ordinal).Count() == expected.Length, "Duplicate compact member.");
Need(File.ReadAllLines(Path.Combine(actual, "outer/inputs-before.sha256")).Order(StringComparer.Ordinal)
    .SequenceEqual(File.ReadAllLines(Path.Combine(actual, "outer/inputs-after.sha256")).Order(StringComparer.Ordinal)), "Producer/tool pins changed.");
Need(File.ReadAllText(Path.Combine(actual, "outer/exit")).Trim() == "0", "Actual remote driver exit was not zero.");
var origin = JsonNode.Parse(File.ReadAllBytes(Path.Combine(actual, "origin-validation.json")))!.AsObject();
var wrappers = JsonNode.Parse(File.ReadAllBytes(Path.Combine(actual, "wrapper-controls.json")))!.AsArray();
Need((long)origin["rawBytesEachPass"]! == 552913184 && (long)origin["rawRowsEachPass"]! == 841444 && (int)origin["verificationPasses"]! == 2,
    "Actual primary scan totals differ.");
Need(wrappers.Count == 11 && wrappers.All(node => (bool)node!["passed"]!), "Actual wrapper controls did not all pass.");
JsonObject proof = wrappers[0]!["proof"]!.AsObject();
Need((int)proof["hostPid"]! == 1028708 && (int)proof["controllerPid"]! == 1028659
    && (string)proof["processEvidenceProfile"]! == "verified-derived-process-origin", "Actual discovery provenance differs.");
string trxPath = Path.Combine(lane, "controls/results08/controls08.trx"); XDocument trx = XDocument.Load(trxPath); XNamespace ns = trx.Root!.Name.Namespace;
XElement counters = trx.Descendants(ns + "Counters").Single();
Need((string?)counters.Attribute("total") == "38" && (string?)counters.Attribute("passed") == "38"
    && (string?)counters.Attribute("failed") == "0" && (string?)counters.Attribute("notExecuted") == "0", "Native receiving cohort differs.");
string[] sourcePaths = ["Program.cs", "Capture72.csproj", "proposed/HealthcheckMutationNativeProcessProjection.cs", "proposed/HealthcheckMutationNativeKernelEvidence.cs", "proposed/HealthcheckMutationUnionKernelInputs.cs", "proposed/HealthcheckMutationNativeProcessProjectionTests.cs", "bin-ready04/Capture72.dll", "bin-ready04/Capture72.deps.json", "bin-ready04/Capture72.runtimeconfig.json", "build04.log", "controls/results08/controls08.trx", "controls/run08.log", "adoption.json", "remaining-ipc-contract.json", "README.md"];
Need(Hash(Path.Combine(lane, "bin-ready04/Capture72.dll")) == "a107dc48fce8bc2603fbf2627ef98d7348ab548881bd47148e301dd752395cd4", "Proven driver changed.");
Need(Hash(Path.Combine(lane, "adoption.json")) == "00028b4d06dfc046d9e921b37fe3f7e1c2bf841d01e2b02ab06f1f9fc382a90a", "Exact adoption record changed.");
string output = Path.Combine(lane, "proof-index.json");
using (var file = new FileStream(output, FileMode.CreateNew))
{
    JsonSerializer.Serialize(file, new { schema = "cis.healthcheck.capture72-proof-index/v1", observedUtc = DateTimeOffset.UtcNow,
        scope = "Closed pilot61 process-origin primitive and actual union-wrapper receiving controls; no complete union or current55 execution.",
        reviewedSourcesAndControls = sourcePaths.Select(path => Pin(Path.Combine(lane, path))).ToArray(), compactImportedEvidence = imported,
        nativeReceiving = new { passed = 38, failed = 0, skipped = 0 }, actualRemote = new { exit = 0, rawBytesEachReaderPass = 552913184, rawRowsEachReaderPass = 841444, readerPasses = 2,
            derivedFiles = 725, processRows = 1911, wrapperPositives = 1, wrapperNegatives = 10, nativeRoot = "/data/cis-healthcheck-20261021-01a15a72/unit-wire-pilot61-process-origin72" },
        current55Read = false, wholeIpcCompleteness = false, executionOutcomeCredit = 0, aggregateRecalculated = false,
        holds = new[] { "Incomplete/forced/native-failed producer is ineligible", "All original source/cohort/restoration/native/child/integrity holds remain", "Original primary omitted unshare; no whole FD-table/IPC acceptance" }
    }, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true);
}
Console.WriteLine(JsonSerializer.Serialize(new { path = output, sha256 = Hash(output), importedBytes = expected.Sum(path => new FileInfo(Path.Combine(actual, path)).Length) }));
static object Pin(string path) => new { path, bytes = new FileInfo(path).Length, sha256 = Hash(path) };
static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
static void Need(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
