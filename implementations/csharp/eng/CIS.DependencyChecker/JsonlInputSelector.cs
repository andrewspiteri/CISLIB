using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CIS.Verification;

// Read-only development example. The application must stop the managed group first.
internal static class JsonlInputSelector
{
    internal const int InventoryBytes = 2 * 1024 * 1024;
    internal const int EntryBytes = 16 * 1024 * 1024;
    private const string Error = "Cannot select a consistent stopped JSONL group.";

    internal static int Run(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: select-jsonl-inputs <active-path>"); return 2; }
        try { Console.WriteLine(JsonSerializer.Serialize(Select(args[0]))); return 0; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or System.Security.SecurityException)
        { Console.Error.WriteLine(Error); return 2; }
    }

    internal static string[] Select(string activePath)
    {
        string active = Path.GetFullPath(activePath), metadata = active + ".cis.inventory.json", pending = active + ".cis.transition.json";
        Require(!ExistsOrLink(pending) && IsRegular(metadata));
        using JsonDocument document = Parse(ReadBounded(metadata, InventoryBytes));
        JsonElement root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object && Integer(Get(root, "schema")) == 1
            && Text(Get(root, "format")) == "jsonl" && Text(Get(root, "encoding")) == "utf-8"
            && Get(root, "bom").ValueKind == JsonValueKind.False && Text(Get(root, "newline")) == "lf");
        string? group = Text(Get(root, "groupId"));
        Require(Guid.TryParseExact(group, "N", out Guid groupId) && groupId != Guid.Empty && groupId.ToString("N") == group);
        JsonElement members = Get(root, "segments");
        Require(members.ValueKind == JsonValueKind.Array && members.GetArrayLength() is >= 1 and <= 1001);
        var result = new List<string>(); long previous = 0;
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement member in members.EnumerateArray())
        {
            Require(member.ValueKind == JsonValueKind.Object);
            long identity = Integer(Get(member, "id")); string? state = Text(Get(member, "state")), name = Text(Get(member, "fileName"));
            bool last = ++index == members.GetArrayLength();
            Require(identity > previous && state is "active" or "closed" or "damaged" && last == (state == "active"));
            string expected = last ? Path.GetFileName(active) : ".cis-log-" + group + "-" + identity.ToString("D20", CultureInfo.InvariantCulture) + "." + state;
            Require(name == expected && names.Add(expected));
            string path = Path.Combine(Path.GetDirectoryName(active)!, expected); Require(IsRegular(path));
            if (state != "damaged")
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var line = new MemoryStream(); bool first = true;
                int value;
                while ((value = stream.ReadByte()) != -1)
                {
                    Require(line.Length < EntryBytes && value != 13); line.WriteByte((byte)value);
                    if (value != 10) { continue; }
                    byte[] raw = line.ToArray();
                    Require(!first || !raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
                    using JsonDocument record = Parse(raw); Require(record.RootElement.ValueKind == JsonValueKind.Object);
                    first = false; line.SetLength(0);
                }
                Require(line.Length == 0); result.Add(path);
            }
            previous = identity;
        }
        return result.ToArray();
    }

    private static JsonDocument Parse(byte[] raw)
    {
        _ = new UTF8Encoding(false, true).GetString(raw);
        Require(!raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        JsonDocument document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 1024 });
        try
        {
            var pending = new Stack<JsonElement>(); pending.Push(document.RootElement);
            while (pending.TryPop(out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty property in value.EnumerateObject()) { Require(names.Add(property.Name)); pending.Push(property.Value); }
                }
                else if (value.ValueKind == JsonValueKind.Array) { foreach (JsonElement item in value.EnumerateArray()) { pending.Push(item); } }
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var bytes = new MemoryStream(); byte[] buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, maximum + 1 - checked((int)bytes.Length)))) != 0)
        { bytes.Write(buffer, 0, read); Require(bytes.Length <= maximum); }
        return bytes.ToArray();
    }

    private static bool ExistsOrLink(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;
    private static bool IsRegular(string path) => File.Exists(path) && new FileInfo(path).LinkTarget is null && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    private static JsonElement Get(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property) ? property : default;
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long Integer(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number) || value.GetRawText().Any(c => c is < '0' or > '9')) { throw new InvalidDataException(Error); }
        return number;
    }
    private static void Require(bool value) { if (!value) { throw new InvalidDataException(Error); } }
}
