#:property PublishAot=false
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

string repo = Path.GetFullPath(args[0]);
const string approvedHash = "66bea29723b9aa2a039df2934c69d3dc0c0b25f189c95ee557eb1d8a4972a46a";
string execution = Path.GetFullPath(Path.Combine(repo, ".artifacts/claude-review-2026-10-17-approved"));
string target = Path.Combine(repo, "planning/reviews/corrective-approved-2026-10-17");
string manifestPath = Path.Combine(repo, "planning/reviews/corrective-review-set-2026-10-16/manifest.json");
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
Require(Hash(manifestPath) == approvedHash, "Approved manifest changed.");
JsonObject manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath))!.AsObject();
JsonObject executionIndex = JsonNode.Parse(File.ReadAllBytes(Path.Combine(execution, "execution-index.json")))!.AsObject();
JsonArray bundles = manifest["bundles"]!.AsArray(), entries = executionIndex["results"]!.AsArray();
Require(bundles.Count == 24 && entries.Count == 24 && bundles.Sum(item => item!["bytes"]!.GetValue<long>()) == 4923824, "Approved scope differs.");
var results = new JsonArray();
var copies = new List<(string Source, string Relative)>();
var models = new HashSet<string>(StringComparer.Ordinal);
var completedPasses = new HashSet<string>(StringComparer.Ordinal);
int refused = 0, notAttempted = 0;
foreach (JsonNode? entry in entries)
{
    int number = entry!["number"]!.GetValue<int>();
    string pass = entry["pass"]!.GetValue<string>();
    JsonNode bundle = bundles[number - 1]!;
    Require(bundle["pass"]!.GetValue<string>() == pass, "Execution assignment differs.");
    string prompt = Path.GetFullPath(Path.Combine(repo, bundle["path"]!.GetValue<string>()));
    Require(new FileInfo(prompt).Length == bundle["bytes"]!.GetValue<long>() && Hash(prompt) == bundle["sha256"]!.GetValue<string>(), "Approved prompt changed: " + pass);
    string directory = Path.GetFullPath(Path.Combine(repo, entry["directory"]!.GetValue<string>()));
    Require(directory.StartsWith(execution + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Execution path escapes owned root.");
    var result = new JsonObject { ["number"] = number, ["pass"] = pass, ["snapshot"] = bundle["snapshot"]!.DeepClone(), ["promptBytes"] = bundle["bytes"]!.DeepClone(), ["promptSha256"] = Hash(prompt) };
    string processPath = Path.Combine(directory, pass + ".process.json"), responsePath = Path.Combine(directory, pass + ".response.json"), reviewPath = Path.Combine(directory, pass + ".review.md");
    if (!File.Exists(processPath))
    {
        Require(!File.Exists(responsePath), "Response without process observation: " + pass);
        result["status"] = "Not attempted after service quota refusal";
        notAttempted++;
        results.Add(result);
        continue;
    }
    JsonObject process = JsonNode.Parse(File.ReadAllBytes(processPath))!.AsObject(), response = JsonNode.Parse(File.ReadAllBytes(responsePath))!.AsObject();
    Require(process["finishedUtc"] is not null, "Review still running: " + pass);
    Require(process["promptSha256"]!.GetValue<string>() == Hash(prompt) && process["promptBytes"]!.GetValue<long>() == new FileInfo(prompt).Length, "Submitted payload differs: " + pass);
    Require(process["responseSha256"]!.GetValue<string>() == Hash(responsePath), "Raw response changed: " + pass);
    Require(response["permission_denials"]!.AsArray().Count == 0 && response["num_turns"]!.GetValue<int>() == 1, "Unexpected review execution: " + pass);
    string[] cliArgs = process["arguments"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
    int toolsIndex = Array.IndexOf(cliArgs, "--tools");
    Require(toolsIndex >= 0 && cliArgs[toolsIndex + 1] == "" && cliArgs.Contains("--restricted") && cliArgs.Contains("--strict-mcp-config") && cliArgs.Contains("--no-session-persistence"), "Review restrictions differ: " + pass);
    bool success = process["exitCode"]!.GetValue<int>() == 0 && !response["is_error"]!.GetValue<bool>() && response["subtype"]!.GetValue<string>() == "success";
    if (success)
    {
        Require(File.ReadAllText(reviewPath, new UTF8Encoding(false, true)) == response["result"]!.GetValue<string>(), "Extracted review differs: " + pass);
        completedPasses.Add(pass);
        foreach (var model in response["modelUsage"]!.AsObject()) models.Add(model.Key);
        result["status"] = "Completed scoped static review; findings require local disposition";
    }
    else
    {
        Require(response["is_error"]!.GetValue<bool>() && response["api_error_status"]!.GetValue<int>() == 429 && response["api_error"]!.GetValue<string>() == "usage_limit_reached", "Unexpected failure: " + pass);
        result["status"] = "Service refused; no review";
        result["serviceMessage"] = response["result"]!.DeepClone();
        refused++;
    }
    var files = new JsonArray();
    foreach (string suffix in success ? new[] { ".process.json", ".response.json", ".review.md", ".stderr.log" } : new[] { ".process.json", ".response.json", ".stderr.log" })
    {
        string source = Path.Combine(directory, pass + suffix), relative = "results/" + number.ToString("D2") + "-" + pass + suffix;
        copies.Add((source, relative));
        files.Add(new JsonObject { ["path"] = relative, ["bytes"] = new FileInfo(source).Length, ["sha256"] = Hash(source) });
    }
    result["startedUtc"] = process["startedUtc"]!.DeepClone(); result["finishedUtc"] = process["finishedUtc"]!.DeepClone();
    result["durationMs"] = response["duration_ms"]!.DeepClone(); result["files"] = files;
    results.Add(result);
}
Require(completedPasses.Count == 17 && refused == 2 && notAttempted == 5, "Observed completion counts differ; reassess before retaining.");
string assignmentsPath = Path.Combine(repo, "planning/reviews/corrective-resume-2026-10-16/input-manifest.json");
JsonArray assignments = JsonNode.Parse(File.ReadAllBytes(assignmentsPath))!.AsArray();
Require(assignments.Count == 352 && assignments.Select(item => item!["path"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == 352, "Assignment scope differs.");
JsonNode?[] pending = assignments.Where(item => !completedPasses.Contains(item!["bundle"]!.GetValue<string>())).ToArray();
var record = new JsonObject { ["schemaVersion"] = 1, ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["authorization"] = "Human explicitly answered Approve this exact 24-bundle review set", ["destination"] = "Anthropic Claude", ["approvedManifestSha256"] = approvedHash, ["approvedPayloadBytes"] = 4923824, ["completedBundles"] = completedPasses.Count, ["refusedBundles"] = refused, ["notAttemptedBundles"] = notAttempted, ["pendingBundles"] = 24 - completedPasses.Count, ["canonicalTriageSnapshot"] = "ba6eb62d", ["models"] = new JsonArray(models.Order(StringComparer.Ordinal).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()), ["assignedPathsInEarlier23Prompts"] = 352, ["reviewedAssignedPaths"] = 352 - pending.Length, ["pendingAssignedPaths"] = pending.Length, ["pendingOriginalAssignedPaths"] = pending.Count(item => item!["wasPending"]!.GetValue<bool>()), ["pendingAdditionalCorrectivePaths"] = pending.Count(item => !item!["wasPending"]!.GetValue<bool>()), ["originalRoadmapGoal"] = "Paused", ["limits"] = "Partial scoped static review only. Older snapshots are retained. Local triage is separate. No implementation changes, Developer rerun, whole-change PASS or release qualification implied.", ["results"] = results };
Require(!Directory.Exists(target), "Receipt target exists; do not overwrite retained evidence.");
Directory.CreateDirectory(Path.Combine(target, "results"));
foreach (var copy in copies) File.Copy(copy.Source, Path.Combine(target, copy.Relative));
foreach (string name in new[] { "execution-index.json", "triage-notes.md", "source-inventory-probes.json", "configuration-alias-probe.json", "exception-reader-probes.json", "provider-map-probes.json", "process-bom-probe.json", "ProbeInventory.cs", "ProbeConfigurationAliases.cs", "ProbeExceptionReader.cs", "ProbeProviderMap.cs", "ProbeProcessBom.cs", "probe-native.sh", "RetainReview.cs" })
{
    string source = Path.Combine(execution, name);
    Require(File.Exists(source), "Missing local evidence: " + name);
    File.Copy(source, Path.Combine(target, name));
}
File.Copy(manifestPath, Path.Combine(target, "approved-manifest.json"));
File.Copy(assignmentsPath, Path.Combine(target, "assigned-inputs.json"));
File.WriteAllText(Path.Combine(target, "index.json"), record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
Console.WriteLine(JsonSerializer.Serialize(new { retained = true, completed = completedPasses.Count, refused, notAttempted, reviewedAssignedPaths = 352 - pending.Length, pendingAssignedPaths = pending.Length, target, approvedManifestSha256 = approvedHash }));
