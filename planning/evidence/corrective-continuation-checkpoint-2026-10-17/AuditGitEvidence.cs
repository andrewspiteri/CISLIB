#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
string repo = Path.GetFullPath(args[0]);
var files = new List<(string Path, string Expected)>();
string review = "planning/reviews/corrective-continuation-2026-10-17";
JsonObject reviews = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repo, review, "index.json")))!.AsObject();
foreach (JsonNode? item in reviews["results"]!.AsArray())
foreach (JsonNode? file in item!["files"]!.AsArray())
    files.Add((review + "/" + file!["path"]!.GetValue<string>(), file["sha256"]!.GetValue<string>()));
string historical = "planning/evidence/corrective-historical-receipt-pins-2026-10-17";
foreach (JsonNode? file in JsonNode.Parse(File.ReadAllBytes(Path.Combine(repo, historical, "index.json")))!["files"]!.AsArray())
    files.Add((historical + "/" + file!["path"]!.GetValue<string>(), file["sha256"]!.GetValue<string>()));
string checkpoint = "planning/evidence/corrective-continuation-checkpoint-2026-10-17";
foreach (JsonNode? lane in JsonNode.Parse(File.ReadAllBytes(Path.Combine(repo, checkpoint, "audit.json")))!["checkpoints"]!.AsArray())
foreach (string kind in new[] { "summary", "validation" })
    files.Add((checkpoint + "/" + lane!["lane"]!.GetValue<string>() + "-" + kind + ".json", lane[kind + "Sha256"]!.GetValue<string>()));
foreach ((string path, string expected) in files)
{
    string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    if (Digest(File.ReadAllBytes(Path.Combine(repo, path))) != expected) throw new InvalidDataException("Working-tree pin differs: " + path);
    using var process = new Process { StartInfo = new("git") { WorkingDirectory = repo, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
    process.StartInfo.ArgumentList.Add("show"); process.StartInfo.ArgumentList.Add("HEAD:" + path);
    process.Start(); using var output = new MemoryStream(); await process.StandardOutput.BaseStream.CopyToAsync(output); await process.WaitForExitAsync();
    if (process.ExitCode != 0 || Digest(output.ToArray()) != expected) throw new InvalidDataException("Git raw pin differs: " + path);
}
Console.WriteLine(new JsonObject { ["verifiedWorktreeAndGitRawFiles"] = files.Count, ["approvedReviewsCompleted"] = reviews["totalCompletedBundles"]!.DeepClone() }.ToJsonString());
