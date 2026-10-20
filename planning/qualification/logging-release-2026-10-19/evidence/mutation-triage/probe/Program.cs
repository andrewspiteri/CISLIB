using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length == 4 && args[0] == "ledger") { Ledger.Write(args[1], args[2], args[3]); return; }

if (args.Length == 3 && args[0] == "serial")
{
    string original = File.ReadAllText(args[1]);
    const string before = "[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]";
    if (original.Split(before).Length != 2 || File.Exists(args[2])) { throw new InvalidDataException("Unexpected serial input or existing output."); }
    File.WriteAllText(args[2], original.Replace(before, "[assembly: DoNotParallelize]", StringComparison.Ordinal), new UTF8Encoding(false));
    Console.WriteLine("Prepared isolated serial test assembly input.");
    return;
}

if (args.Length < 3) { throw new ArgumentException("Expected report, new output directory and mutant IDs."); }
string reportPath = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) { throw new IOException("Output already exists."); }
using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
var requested = args.Skip(2).ToHashSet(StringComparer.Ordinal);
var entries = new List<object>();
Directory.CreateDirectory(output);
foreach (var file in report.RootElement.GetProperty("files").EnumerateObject())
{
    string source = file.Value.GetProperty("source").GetString()!;
    foreach (var mutant in file.Value.GetProperty("mutants").EnumerateArray())
    {
        string id = mutant.GetProperty("id").GetString()!;
        if (!requested.Remove(id)) { continue; }
        var location = mutant.GetProperty("location");
        int Position(JsonElement position)
        {
            int line = position.GetProperty("line").GetInt32();
            int offset = 0;
            for (int index = 1; index < line; index++)
            { offset = source.IndexOf('\n', offset) + 1; if (offset == 0) { throw new InvalidDataException("Invalid line."); } }
            return offset + position.GetProperty("column").GetInt32() - 1;
        }
        int start = Position(location.GetProperty("start")), end = Position(location.GetProperty("end"));
        string replacement = mutant.GetProperty("replacement").GetString()!;
        string changed = source[..start] + replacement + source[end..];
        string path = Path.Combine(output, id + ".cs");
        File.WriteAllText(path, changed, new UTF8Encoding(false));
        string original = Path.Combine(output, id + ".original.cs");
        File.WriteAllText(original, source, new UTF8Encoding(false));
        entries.Add(new { id, relativePath = "implementations/" + file.Name.Split("/implementations/", 2)[1],
            originalText = source[start..end], replacement, nativeStatus = mutant.GetProperty("status").GetString(),
            originalSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(original))),
            mutantSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) });
    }
}
if (requested.Count != 0) { throw new InvalidDataException("Missing IDs: " + string.Join(",", requested)); }
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
{
    reportSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(reportPath))), entries
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Exported {entries.Count} source-bound mutants.");
