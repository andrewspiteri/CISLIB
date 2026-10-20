using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 4) { throw new ArgumentException("Expected final native report, current Core source root, diagnostic config and new output directory."); }
string reportPath = Path.GetFullPath(args[0]), sourceRoot = Path.GetFullPath(args[1]), configPath = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
if (Directory.Exists(output)) { throw new IOException("Use a new immutable output directory."); }
Directory.CreateDirectory(output);
using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
using var config = JsonDocument.Parse(File.ReadAllBytes(configPath));
var selected = config.RootElement.GetProperty("stryker-config").GetProperty("mutate").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal);
var allowed = new[] { "Killed", "Survived", "NoCoverage", "Timeout", "CompileError", "Ignored" }.ToHashSet(StringComparer.Ordinal);
var found = new HashSet<string>(StringComparer.Ordinal);
var ids = new HashSet<string>(StringComparer.Ordinal);
var all = new List<Entry>();
foreach (var file in report.RootElement.GetProperty("files").EnumerateObject())
{
    const string marker = "/src/CIS.Core/";
    string path = file.Name.Replace('\\', '/'); int offset = path.LastIndexOf(marker, StringComparison.Ordinal);
    if (offset < 0) { throw new InvalidDataException("Unmapped native source: " + file.Name); }
    string relative = path[(offset + marker.Length)..], sourcePath = Path.GetFullPath(relative, sourceRoot);
    if (!sourcePath.StartsWith(Path.TrimEndingDirectorySeparator(sourceRoot) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !found.Add(relative))
    { throw new InvalidDataException("Duplicate or escaped source."); }
    string source = File.ReadAllText(sourcePath);
    if (source != file.Value.GetProperty("source").GetString()) { throw new InvalidDataException("Current product source differs: " + relative); }
    string sourceHash = Hash(sourcePath);
    var starts = new List<int> { 0 };
    for (int i = 0; i < source.Length; i++) { if (source[i] == '\n') { starts.Add(i + 1); } }
    int Position(JsonElement point) => starts[point.GetProperty("line").GetInt32() - 1] + point.GetProperty("column").GetInt32() - 1;
    foreach (var mutant in file.Value.GetProperty("mutants").EnumerateArray())
    {
        string id = mutant.GetProperty("id").GetString()!, status = mutant.GetProperty("status").GetString()!;
        if (!ids.Add(id) || !allowed.Contains(status)) { throw new InvalidDataException("Duplicate ID, incomplete or unknown native status: " + id + ":" + status); }
        var location = mutant.GetProperty("location");
        int start = Position(location.GetProperty("start")), end = Position(location.GetProperty("end"));
        all.Add(new(id, relative, status, sourceHash, mutant.GetProperty("mutatorName").GetString()!, location.Clone(), source[start..end], mutant.GetProperty("replacement").GetString()!));
    }
}
var current = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
    .Where(p => !Path.GetRelativePath(sourceRoot, p).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
    .Select(p => Path.GetRelativePath(sourceRoot, p).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
if (!current.SetEquals(found) || !selected.IsSubsetOf(found)) { throw new InvalidDataException("Native/current source or diagnostic selection membership differs."); }
object Counts(IEnumerable<Entry> entries)
{
    var counts = entries.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count());
    long denominator = new[] { "Killed", "Survived", "NoCoverage", "Timeout" }.Sum(s => (long)counts.GetValueOrDefault(s));
    return new { total = counts.Values.Sum(), counts, strictScore = denominator == 0 ? (decimal?)null : 100m * counts.GetValueOrDefault("Killed") / denominator };
}
Entry probe = all.Single(p => p.Id == "2984");
bool matchedProbe = probe.File == "Logging/Runtime/ConsoleOutputWriter.cs" && probe.OriginalText == "!writable" && probe.Replacement == "writable" &&
    probe.SourceSha256 == "84f05f14c44feb1bfc20a2a852d12311f01929a84084d9cab52bd4aff87b4fcc";
if (!matchedProbe) { throw new InvalidDataException("Logged probe source/selection differs from the final native report."); }
var options = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(Path.Combine(output, "review.json"), JsonSerializer.Serialize(new
{
    scope = "Source/status inventory and diagnostic subset calculation, not approval of non-killed or filtered mutations. Native scores/statuses are unchanged.",
    nativeReportSha256 = Hash(reportPath), verifierSha256 = Hash(typeof(Program).Assembly.Location), currentSourceFiles = found.Count,
    currentSourceMembershipAndTextMatch = true, completeRecognizedStatusInventory = true,
    all = Counts(all), diagnosticSubset = new { files = selected.Count, observation = Counts(all.Where(p => selected.Contains(p.File))) },
    compilerProbeFinalNativeMatch = new { matched = matchedProbe, entry = probe },
    modules = all.GroupBy(p => p.File.Split('/')[0]).OrderBy(g => g.Key).Select(g => new { module = g.Key, observation = Counts(g) }),
    files = all.GroupBy(p => p.File).OrderByDescending(g => g.Count(p => p.Status is "Survived" or "NoCoverage" or "Timeout")).Select(g => new { file = g.Key, observation = Counts(g) })
}, options));
File.WriteAllText(Path.Combine(output, "non-killed.json"), JsonSerializer.Serialize(new
{ nativeReportSha256 = Hash(reportPath), metricAdjusted = false, entries = all.Where(p => p.Status != "Killed") }, options));
Console.WriteLine(JsonSerializer.Serialize(new { sourceFiles = found.Count, total = all.Count, compilerProbeStatus = probe.Status, sourceMatch = true, subset = Counts(all.Where(p => selected.Contains(p.File))) }));

static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
internal sealed record Entry(string Id, string File, string Status, string SourceSha256, string MutatorName, JsonElement Location, string OriginalText, string Replacement);
