#:property PublishAot=false
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
string repo = Path.GetFullPath(args[0]);
string receipt = Path.Combine(repo, "planning/reviews/corrective-approved-2026-10-17");
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
void Require(bool value, string label) { if (!value) throw new InvalidDataException(label); }
JsonObject index = JsonNode.Parse(File.ReadAllBytes(Path.Combine(receipt, "index.json")))!.AsObject();
Require(index["completedBundles"]!.GetValue<int>() == 17 && index["refusedBundles"]!.GetValue<int>() == 2 && index["notAttemptedBundles"]!.GetValue<int>() == 5, "Counts differ.");
Require(Hash(Path.Combine(receipt, "approved-manifest.json")) == index["approvedManifestSha256"]!.GetValue<string>(), "Retained manifest differs.");
int verified = 0, completed = 0;
foreach (JsonNode? result in index["results"]!.AsArray())
{
    foreach (JsonNode? file in result!["files"]?.AsArray() ?? [])
    {
        string path = Path.Combine(receipt, file!["path"]!.GetValue<string>());
        Require(Hash(path) == file["sha256"]!.GetValue<string>() && new FileInfo(path).Length == file["bytes"]!.GetValue<long>(), "Retained result differs: " + path);
        verified++;
    }
    if (!result!["status"]!.GetValue<string>().StartsWith("Completed", StringComparison.Ordinal)) continue;
    string responsePath = result["files"]!.AsArray().Single(item => item!["path"]!.GetValue<string>().EndsWith(".response.json", StringComparison.Ordinal))!["path"]!.GetValue<string>();
    JsonObject response = JsonNode.Parse(File.ReadAllBytes(Path.Combine(receipt, responsePath)))!.AsObject();
    Require(response["subagent_stats"]!["spawned"]!.GetValue<int>() == 0 && response["subagent_stats"]!["completed"]!.GetValue<int>() == 0, "Unexpected external delegation.");
    Require(response["modelUsage"]!.AsObject().All(model => model.Value!["webSearchRequests"]!.GetValue<int>() == 0), "Unexpected external web use.");
    completed++;
}
var preserved = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["docs/cis/graph-context.md"] = "b07d0d4742f1917dd7c3c2403d2a9bdaf4dda8ab83c52c9d2cca93a733ced166",
    ["docs/cis/references/repository-profile.md"] = "f206536f3663d1eff7223c36671d4300e8d38c55ab5323d0f224920d086853e4",
    [".cis/graph-inputs.json"] = "936121e0029d84495615214c095ba44fb81f94f8862286a71d5bb450575657dc",
    ["implementations/csharp/examples/type-manager/Web/Properties/launchSettings.json"] = "1509170e5a2d67d067d16e8a3c69db9e67755f5761bc0b5413fe11558054b6be",
    ["planning/cis/evidence/graph-fixes-2026-10-15.json"] = "80bca3253915b74c5b3215f871aa012d9186b34b03fec765f3580ba9fa3d0218"
};
foreach (var file in preserved) Require(Hash(Path.Combine(repo, file.Key)) == file.Value, "User-owned file changed: " + file.Key);
Require(completed == 17 && verified == 74, "Result file count differs.");
string json = JsonSerializer.Serialize(new { recordedUtc = DateTimeOffset.UtcNow, verified = true, approvedManifestSha256 = index["approvedManifestSha256"]!.GetValue<string>(), completedReviews = completed, retainedResultFiles = verified, userOwnedFilesUnchanged = preserved.Count, graphStatus = "fresh; 10997 nodes; 54285 edges; zero diagnostics", graphBuild = "b4c40cd3f6aa7a0196697c5db2858cffaa643858aa327ee1c77edcd3b6902bda", scope = "Review evidence integrity and preservation only; no maintained implementation changes or new Developer/release qualification." }, new JsonSerializerOptions { WriteIndented = true }) + "\n";
File.WriteAllText(Path.Combine(receipt, "integrity-audit.json"), json, new UTF8Encoding(false));
Console.WriteLine(json);
