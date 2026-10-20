using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 3) { throw new ArgumentException("Expected original snapshot inventory, snapshot source root and new export root."); }
string inventory = Path.GetFullPath(args[0]), sourceRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) { throw new IOException("Use a new export directory."); }
Directory.CreateDirectory(output);
const string userFile = "implementations/csharp/examples/type-manager/Web/Properties/launchSettings.json";
using var original = JsonDocument.Parse(File.ReadAllBytes(inventory));
var selected = original.RootElement.EnumerateArray().Where(p => p.GetProperty("path").GetString() != userFile).ToArray();
var paths = new HashSet<string>(StringComparer.Ordinal);
foreach (var pin in selected)
{
    string path = pin.GetProperty("path").GetString()!, full = Path.GetFullPath(path, sourceRoot);
    if (!full.StartsWith(Path.TrimEndingDirectorySeparator(sourceRoot) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
        !paths.Add(path) || Hash(full) != pin.GetProperty("sha256").GetString())
    { throw new InvalidDataException("Snapshot path, membership or bytes differ: " + path); }
}
File.WriteAllBytes(Path.Combine(output, "members.nul"), Encoding.UTF8.GetBytes(string.Join('\0', paths.Order(StringComparer.Ordinal)) + '\0'));
File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(new
{
    scope = "Exact language/conformance/contracts/compatibility input closure from the final helper snapshot; not an offline release kit.",
    originalSnapshotInventorySha256 = Hash(inventory), verifierSha256 = Hash(typeof(Program).Assembly.Location),
    omitted = new[] { new { path = userFile, reason = "Unrelated user-owned, untracked launch settings; preserved in the workspace and omitted from retained source content." } },
    count = selected.Length, inputs = selected
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Verified {selected.Length} retained source inputs.");

static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
