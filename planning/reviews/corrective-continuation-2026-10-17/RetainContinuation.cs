#:property PublishAot=false
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
string repo = Path.GetFullPath(args[0]), root = Path.Combine(repo, ".artifacts/claude-review-2026-10-17-finish-approved"), target = Path.Combine(repo, "planning/reviews/corrective-continuation-2026-10-17");
const string approved = "66bea29723b9aa2a039df2934c69d3dc0c0b25f189c95ee557eb1d8a4972a46a";
void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
string manifestPath = Path.Combine(repo, "planning/reviews/corrective-review-set-2026-10-16/manifest.json");
Require(Hash(manifestPath) == approved, "Approval identity changed.");
JsonArray bundles = Read(manifestPath)["bundles"]!.AsArray(), attempts = Read(Path.Combine(root, "index.json"))["attempts"]!.AsArray();
Require(bundles.Count == 24 && attempts.Count == 7, "Scope differs.");
foreach (JsonNode? bundle in bundles)
{
    string path = Path.Combine(repo, bundle!["path"]!.GetValue<string>());
    Require(Hash(path) == (string?)bundle["sha256"] && new FileInfo(path).Length == (long?)bundle["bytes"], "Approved payload changed.");
}
var completed = Read(Path.Combine(repo, "planning/reviews/corrective-approved-2026-10-17/index.json"))["results"]!.AsArray()
    .Where(row => row!["status"]!.GetValue<string>().StartsWith("Completed", StringComparison.Ordinal)).Select(row => row!["pass"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
Require(completed.Count == 17, "Original receipt differs.");
var results = new JsonArray(); var copies = new List<(string Source, string Relative)>(); int successful = 0, refused = 0;
foreach (JsonNode? attempt in attempts)
{
    int number = attempt!["number"]!.GetValue<int>();
    Require(number is >= 17 and <= 23, "Attempt outside approved pending set.");
    JsonNode bundle = bundles[number - 1]!; string pass = bundle["pass"]!.GetValue<string>();
    string directory = Path.Combine(repo, attempt["directory"]!.GetValue<string>());
    JsonObject process = Read(Path.Combine(directory, pass + ".process.json")), response = Read(Path.Combine(directory, pass + ".response.json"));
    Require(process["finishedUtc"] is not null && (string?)process["promptSha256"] == (string?)bundle["sha256"] && (long?)process["promptBytes"] == (long?)bundle["bytes"] &&
        (string?)process["approvedManifestSha256"] == approved && (string?)process["destination"] == "Anthropic Claude" && (bool?)process["readOnly"] == true, "Execution identity differs.");
    Require(Hash(Path.Combine(directory, pass + ".response.json")) == (string?)process["responseSha256"] && (int?)response["num_turns"] == 1 && response["permission_denials"]!.AsArray().Count == 0, "Raw response or restrictions differ.");
    string[] flags = process["arguments"]!.AsArray().Select(arg => arg!.GetValue<string>()).ToArray();
    Require(flags[Array.IndexOf(flags, "--tools") + 1] == "" && flags.Contains("--restricted") && flags.Contains("--strict-mcp-config") && flags.Contains("--no-session-persistence") &&
        flags.Contains("--disable-slash-commands") && flags.Contains("--no-chrome"), "Read-only restrictions differ.");
    bool success = (int?)process["exitCode"] == 0 && (bool?)response["is_error"] == false && (string?)response["subtype"] == "success";
    if (success) { Require(File.ReadAllText(Path.Combine(directory, pass + ".review.md"), new UTF8Encoding(false, true)) == (string?)response["result"], "Review extraction differs."); successful++; completed.Add(pass); }
    else { Require(number == 20 && (string?)response["api_error"] == "usage_limit_reached" && (int?)response["api_error_status"] == 429, "Unexpected failed attempt."); refused++; }
    var files = new JsonArray();
    foreach (string suffix in success ? new[] { ".process.json", ".response.json", ".review.md", ".stderr.log" } : new[] { ".process.json", ".response.json", ".stderr.log" })
    {
        string source = Path.Combine(directory, pass + suffix), relative = "results/" + number.ToString("D2") + "-" + pass + suffix;
        copies.Add((source, relative)); files.Add(new JsonObject { ["path"] = relative, ["bytes"] = new FileInfo(source).Length, ["sha256"] = Hash(source) });
    }
    results.Add(new JsonObject { ["number"] = number, ["pass"] = pass, ["status"] = success ? "Completed scoped static review; current-code triage required" : "Weekly quota refusal; no review", ["snapshot"] = bundle["snapshot"]!.DeepClone(), ["promptSha256"] = bundle["sha256"]!.DeepClone(), ["promptBytes"] = bundle["bytes"]!.DeepClone(), ["files"] = files, ["serviceMessage"] = success ? null : response["result"]!.DeepClone() });
}
Require(successful == 6 && refused == 1 && completed.Count == 23, "Completion counts differ.");
JsonArray assigned = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repo, "planning/reviews/corrective-resume-2026-10-16/input-manifest.json")))!.AsArray();
Require(assigned.Count == 352, "Assigned scope differs.");
JsonNode?[] pending = assigned.Where(item => !completed.Contains(item!["bundle"]!.GetValue<string>())).ToArray();
Require(!Directory.Exists(target), "Preserve earlier receipt.");
Directory.CreateDirectory(Path.Combine(target, "results"));
foreach (var copy in copies) File.Copy(copy.Source, Path.Combine(target, copy.Relative));
File.Copy(Path.Combine(root, "RetainContinuation.cs"), Path.Combine(target, "RetainContinuation.cs"));
File.Copy(Path.Combine(root, "RunApprovedReview.cs"), Path.Combine(target, "RunApprovedReview.cs"));
var index = new JsonObject { ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["approvedManifestSha256"] = approved, ["destination"] = "Anthropic Claude", ["completedThisContinuation"] = 6, ["refusedThisContinuation"] = 1, ["totalCompletedBundles"] = 23, ["totalApprovedBundles"] = 24, ["assignedPaths"] = assigned.Count, ["reviewedAssignedPaths"] = assigned.Count - pending.Length, ["pendingAssignedPaths"] = pending.Length, ["pendingBundle"] = "06-verification-governance-governance-6", ["originalRoadmap"] = "Paused", ["limits"] = "Immutable previously approved snapshots only; no current-HEAD rereview or whole-change PASS. Prior refusals and all original responses remain preserved.", ["results"] = results };
File.WriteAllText(Path.Combine(target, "index.json"), index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(new JsonObject { ["completed"] = 23, ["pending"] = 1, ["reviewedAssignedPaths"] = assigned.Count - pending.Length, ["pendingAssignedPaths"] = pending.Length, ["retained"] = target }.ToJsonString());