using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CIS.Logging.Runtime;

internal enum FileSegmentState { Active, Closed, Damaged }
internal sealed record FileInventorySegment(long Id, string FileName, FileSegmentState State);

// Format belongs to the entire group, including every closed/damaged segment.
internal sealed class FileGroupInventory
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    internal Guid GroupId { get; }
    internal LogOutputFormat Format { get; }
    internal IReadOnlyList<FileInventorySegment> Segments { get; }
    internal FileInventorySegment Active => Segments[^1];

    private FileGroupInventory(Guid groupId, LogOutputFormat format, FileInventorySegment[] segments)
    {
        GroupId = groupId;
        Format = format;
        Segments = Array.AsReadOnly(segments);
    }

    internal static FileGroupInventory OpenOrCreate(FileOutputOwner owner, LogOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!Enum.IsDefined(format)) { throw new ArgumentOutOfRangeException(nameof(format)); }
        owner.RequireOwnership();
        string path = owner.AbsolutePath + ".cis.inventory.json";
        try
        {
            FileStream? existing;
            try { existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (FileNotFoundException) { existing = null; }
            if (existing is not null)
            {
                using (existing)
                {
                    if (owner.ActiveWasCreated) { throw Incompatible(); }
                    if (existing.Length > MaximumBytes) { throw Incompatible(); }
                    byte[] bytes = new byte[checked((int)existing.Length)]; existing.ReadExactly(bytes);
                    if (existing.ReadByte() != -1) { throw Incompatible(); }
                    _ = new UTF8Encoding(false, true).GetCharCount(bytes);
                    using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
                    return Inspect(document.RootElement, owner.AbsolutePath, format);
                }
            }
            if (owner.ActiveFile.Length != 0) { throw new CoreLogInitializationException("file", "FileInventoryMissing"); }
            var created = new FileGroupInventory(Guid.NewGuid(), format, [new(1, Path.GetFileName(owner.AbsolutePath), FileSegmentState.Active)]);
            PublishNew(path, created);
            return created;
        }
        catch (JsonException) { throw Incompatible(); }
        catch (DecoderFallbackException) { throw Incompatible(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            throw new CoreLogInitializationException("file", "FileInventoryInitializationFailed");
        }
    }

    private static FileGroupInventory Inspect(JsonElement root, string activePath, LogOutputFormat format)
    {
        RequireObject(root);
        if (!root.TryGetProperty("schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != 1
            || !Guid.TryParseExact(Text(root, "groupId"), "N", out Guid groupId) || groupId == Guid.Empty
            || Text(root, "format") != FormatName(format) || Text(root, "encoding") != "utf-8"
            || !root.TryGetProperty("bom", out JsonElement bom) || bom.ValueKind != JsonValueKind.False
            || Text(root, "newline") != "lf" || !root.TryGetProperty("segments", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array
            || entries.GetArrayLength() is < 1 or > LoggingOptions.MaximumFileRetainedFiles + 1)
        {
            throw Incompatible();
        }
        var segments = new FileInventorySegment[entries.GetArrayLength()];
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string activeName = Path.GetFileName(activePath); long previousId = 0; int index = 0;
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            RequireObject(entry);
            if (!entry.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long segmentId) || segmentId <= previousId) { throw Incompatible(); }
            string name = Text(entry, "fileName"), stateName = Text(entry, "state");
            FileSegmentState state = stateName switch
            {
                "active" => FileSegmentState.Active,
                "closed" => FileSegmentState.Closed,
                "damaged" => FileSegmentState.Damaged,
                _ => throw Incompatible()
            };
            bool last = index == segments.Length - 1;
            if (last != (state == FileSegmentState.Active) || !names.Add(name)) { throw Incompatible(); }
            string expected = last ? activeName : ClosedName(groupId, segmentId, state);
            if (!string.Equals(name, expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { throw Incompatible(); }
            if (!last)
            {
                // Inspect only the exact named group members. Missing members fail unchanged.
                try
                {
                    using var segment = new FileStream(Path.Combine(Path.GetDirectoryName(activePath)!, name), FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                catch (FileNotFoundException) { throw Incompatible(); }
            }
            segments[index++] = new(segmentId, name, state); previousId = segmentId;
        }
        return new FileGroupInventory(groupId, format, segments);
    }

    internal static string ClosedName(Guid groupId, long segmentId, FileSegmentState state)
        => ".cis-log-" + groupId.ToString("N") + "-" + segmentId.ToString("D20", CultureInfo.InvariantCulture)
            + (state == FileSegmentState.Damaged ? ".damaged" : ".closed");

    private static void PublishNew(string path, FileGroupInventory inventory)
    {
        // Publish complete metadata without overwriting a collision. Failed private candidates
        // are preserved; startup rollback never deletes pre-existing/nonempty artifacts.
        string candidate = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var writer = new Utf8JsonWriter(file))
            {
                writer.WriteStartObject(); writer.WriteNumber("schema", 1); writer.WriteString("groupId", inventory.GroupId.ToString("N"));
                writer.WriteString("format", FormatName(inventory.Format)); writer.WriteString("encoding", "utf-8"); writer.WriteBoolean("bom", false); writer.WriteString("newline", "lf");
                writer.WriteStartArray("segments"); writer.WriteStartObject(); writer.WriteNumber("id", inventory.Active.Id);
                writer.WriteString("fileName", inventory.Active.FileName); writer.WriteString("state", "active");
                writer.WriteEndObject(); writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
            }
            file.WriteByte((byte)'\n'); file.Flush(flushToDisk: true);
        }
        File.Move(candidate, path, overwrite: false);
    }

    private static string FormatName(LogOutputFormat format) => format == LogOutputFormat.JsonLines ? "jsonl" : "human";
    private static string Text(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) { throw Incompatible(); }
        return value.GetString()!;
    }
    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) { throw Incompatible(); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty member in value.EnumerateObject()) { if (!names.Add(member.Name)) { throw Incompatible(); } }
    }
    private static CoreLogInitializationException Incompatible() => new("file", "FileInventoryIncompatible");
}
