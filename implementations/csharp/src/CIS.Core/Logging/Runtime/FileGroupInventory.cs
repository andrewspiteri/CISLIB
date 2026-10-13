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
            Publish(path, created, overwrite: false);
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

    internal FileGroupInventory AfterRotation(string activePath, int maximumRetainedFiles)
    {
        if (maximumRetainedFiles is < LoggingOptions.MinimumFileRetainedFiles or > LoggingOptions.MaximumFileRetainedFiles)
        { throw new ArgumentOutOfRangeException(nameof(maximumRetainedFiles)); }
        // Keep the newest listed closed/damaged identities plus this closing active file.
        // Segment IDs, rather than timestamps or directory order, survive restarts.
        if (Active.Id == long.MaxValue) { throw new IOException("File segment identity is exhausted."); }
        int removed = Math.Max(0, Segments.Count - maximumRetainedFiles);
        var next = new FileInventorySegment[Segments.Count + 1 - removed];
        for (int index = removed; index < Segments.Count - 1; index++) { next[index - removed] = Segments[index]; }
        next[^2] = new(Active.Id, ClosedName(GroupId, Active.Id, FileSegmentState.Closed), FileSegmentState.Closed);
        next[^1] = new(Active.Id + 1, Path.GetFileName(activePath), FileSegmentState.Active);
        return new FileGroupInventory(GroupId, Format, next);
    }

    internal void PublishReplacement(string activePath)
        => Publish(activePath + ".cis.inventory.json", this, overwrite: true);

    private static void Publish(string path, FileGroupInventory inventory, bool overwrite)
    {
        // Publish complete same-directory metadata. Preserve failed private candidates;
        // a collision or interruption is never repaired by deleting pre-existing data.
        string candidate = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var writer = new Utf8JsonWriter(file))
            {
                writer.WriteStartObject(); writer.WriteNumber("schema", 1); writer.WriteString("groupId", inventory.GroupId.ToString("N"));
                writer.WriteString("format", FormatName(inventory.Format)); writer.WriteString("encoding", "utf-8"); writer.WriteBoolean("bom", false); writer.WriteString("newline", "lf");
                writer.WriteStartArray("segments");
                foreach (FileInventorySegment segment in inventory.Segments)
                {
                    writer.WriteStartObject(); writer.WriteNumber("id", segment.Id); writer.WriteString("fileName", segment.FileName);
                    writer.WriteString("state", segment.State switch { FileSegmentState.Active => "active", FileSegmentState.Closed => "closed", FileSegmentState.Damaged => "damaged", _ => throw new InvalidOperationException("Unknown segment state.") });
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
            }
            file.WriteByte((byte)'\n'); file.Flush(flushToDisk: true);
        }
        File.Move(candidate, path, overwrite);
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
