using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) { throw new ArgumentException("Expected two packages and a new comparison report."); }
string leftPath = Path.GetFullPath(args[0]), rightPath = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (File.Exists(output)) { throw new IOException("Use a new immutable comparison report."); }
var left = Members(leftPath); var right = Members(rightPath);
bool membership = left.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(right.Keys);
bool payloads = membership && left.All(pair => pair.Value.Hash == right[pair.Key].Hash);
var result = new
{
    scope = "Archive member inventory and exact uncompressed bytes; distinct container hashes are preserved. This comparison does not execute consumers or qualify Release gates.",
    leftPath, rightPath, leftPackageSha256 = Hash(leftPath), rightPackageSha256 = Hash(rightPath),
    verifierSha256 = Hash(typeof(Program).Assembly.Location), memberCount = left.Count,
    memberInventoryEqual = membership, memberBytesEqual = payloads,
    members = left.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
    {
        name = pair.Key, leftSha256 = pair.Value.Hash,
        rightSha256 = right.TryGetValue(pair.Key, out var entry) ? entry.Hash : null,
        leftTimestamp = pair.Value.Time, rightTimestamp = entry.Time
    }).ToArray()
};
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { membership, payloads, members = left.Count, left = Hash(leftPath), right = Hash(rightPath) }));
return membership && payloads ? 0 : 1;

static Dictionary<string, (string Hash, DateTimeOffset Time)> Members(string path)
{
    using var archive = ZipFile.OpenRead(path);
    var result = new Dictionary<string, (string, DateTimeOffset)>(StringComparer.Ordinal);
    foreach (var entry in archive.Entries)
    {
        using var stream = entry.Open();
        if (!result.TryAdd(entry.FullName, (Convert.ToHexStringLower(SHA256.HashData(stream)), entry.LastWriteTime)))
        { throw new InvalidDataException("Duplicate archive member: " + entry.FullName); }
    }
    if (result.Count == 0) { throw new InvalidDataException("Empty package."); }
    return result;
}
static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
