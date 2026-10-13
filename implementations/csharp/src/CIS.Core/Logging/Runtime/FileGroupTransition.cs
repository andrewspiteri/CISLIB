using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CIS.Logging.Runtime;

// One bounded intent for the exact group transition, after retirement and before rename.
// This is cooperative restart reconciliation, not a crash-durability or hostile-race guarantee.
internal sealed record FileGroupTransition(FileGroupInventory Previous, FileGroupInventory Next, int RetainedLimit, bool Recovery, long OriginalBytes, string OriginalSha256)
{
    internal const int MaximumBytes = 2 * FileGroupInventory.MaximumBytes + 1024;
    internal static string PendingPath(string path) => path + ".cis.transition.json";

    internal static void Begin(FileOutputOwner owner, FileGroupInventory next, int retainedLimit, bool recovery)
    {
        owner.RequireOwnership();
        FileGroupInventory previous = owner.Inventory ?? throw new InvalidOperationException("A transition requires managed identity.");
        long position = owner.ActiveFile.Position;
        string digest;
        try { owner.ActiveFile.Flush(flushToDisk: true); owner.ActiveFile.Position = 0; digest = Convert.ToHexStringLower(SHA256.HashData(owner.ActiveFile)); }
        finally { owner.ActiveFile.Position = position; }
        var intent = new FileGroupTransition(previous, next, retainedLimit, recovery, owner.ActiveFile.Length, digest);
        // Private incomplete candidates are never used for reconciliation.
        string candidate = PendingPath(owner.AbsolutePath) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var writer = new Utf8JsonWriter(file))
            {
                writer.WriteStartObject(); writer.WriteNumber("schema", 1); writer.WriteBoolean("recovery", recovery); writer.WriteNumber("retainedLimit", retainedLimit);
                writer.WriteNumber("originalBytes", intent.OriginalBytes); writer.WriteString("originalSha256", digest);
                writer.WritePropertyName("previous"); previous.Write(writer);
                writer.WritePropertyName("next"); next.Write(writer); writer.WriteEndObject(); writer.Flush();
            }
            file.WriteByte((byte)'\n'); file.Flush(flushToDisk: true);
        }
        File.Move(candidate, PendingPath(owner.AbsolutePath), overwrite: false);
    }

    internal static void Complete(string path) => File.Delete(PendingPath(path));

    // Called with the path-group lease held, before acquiring or creating an active handle.
    internal static bool Reconcile(string path, LogOutputFormat format, int maximumRetainedFiles, long maximumFileBytes)
    {
        try
        {
            FileGroupTransition? intent = Read(path, format);
            if (intent is null) { return false; }
            FileGroupInventory current = ReadInventory(path, format);
            bool before = current.HasSameIdentity(intent.Previous), after = current.HasSameIdentity(intent.Next);
            if (!before && !after) { throw Incompatible(); }
            if (intent.OriginalBytes > maximumFileBytes) { throw new CoreLogInitializationException("file", "FileSizeLimitExceeded"); }
            if (intent.Next.Segments.Count - 1 > maximumRetainedFiles) { throw new CoreLogInitializationException("file", "FileRetentionLimitExceeded"); }
            string directory = Path.GetDirectoryName(path)!;
            string archivedPath = Path.Combine(directory, intent.Next.Segments[^2].FileName);
            bool active = Exists(path), archived = Exists(archivedPath);
            if (before ? active == archived : !archived) { throw Incompatible(); }
            if (before && active) { intent.CheckOriginal(path); }
            else
            {
                intent.CheckOriginal(archivedPath);
                if (active) { using var fresh = Open(path); if (fresh.Length != 0) { throw Incompatible(); } }
            }

            int removed = intent.Previous.Segments.Count + 1 - intent.Next.Segments.Count;
            for (int index = 0; index < intent.Previous.Segments.Count - 1; index++)
            {
                bool exists = Exists(Path.Combine(directory, intent.Previous.Segments[index].FileName));
                if (index >= removed) { if (!exists) { throw Incompatible(); } }
                else if (exists) { throw Incompatible(); } // Retirement must finish before publishing intent.
            }
            // All identity, byte, membership and selected-bound checks precede mutations.
            if (before)
            {
                if (active) { File.Move(path, archivedPath, overwrite: false); }
                intent.Next.PublishReplacement(path);
            }
            if (!after || !active) { using var fresh = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read); }
            Complete(path);
            return intent.Recovery;
        }
        catch (CoreLogInitializationException error) when (error.ErrorCode is "FileInventoryIncompatible" or "FileRecoveryIdentityExhausted") { throw Incompatible(); }
        catch (JsonException) { throw Incompatible(); }
        catch (DecoderFallbackException) { throw Incompatible(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        { throw new CoreLogInitializationException("file", "FileTransitionInitializationFailed"); }
    }

    private void CheckOriginal(string path)
    {
        using var file = Open(path);
        if (file.Length != OriginalBytes || Convert.ToHexStringLower(SHA256.HashData(file)) != OriginalSha256) { throw Incompatible(); }
    }

    private static bool Exists(string path)
    {
        try { using var file = Open(path); return true; }
        catch (FileNotFoundException) { return false; }
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static FileGroupInventory ReadInventory(string path, LogOutputFormat format)
    {
        FileStream file;
        try { file = Open(path + ".cis.inventory.json"); }
        catch (FileNotFoundException) { throw Incompatible(); }
        using (file)
        using (JsonDocument document = ReadDocument(file, FileGroupInventory.MaximumBytes))
        { return FileGroupInventory.Inspect(document.RootElement, path, format, inspectMembers: false); }
    }

    private static FileGroupTransition? Read(string path, LogOutputFormat format)
    {
        FileStream file;
        try { file = Open(PendingPath(path)); }
        catch (FileNotFoundException) { return null; }
        using (file)
        using (JsonDocument document = ReadDocument(file, MaximumBytes))
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { throw Incompatible(); }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty member in root.EnumerateObject()) { if (!names.Add(member.Name)) { throw Incompatible(); } }
            if (!root.TryGetProperty("schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != 1
                || !root.TryGetProperty("recovery", out JsonElement recovery) || recovery.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("retainedLimit", out JsonElement limit) || limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out int bound) || bound is < LoggingOptions.MinimumFileRetainedFiles or > LoggingOptions.MaximumFileRetainedFiles
                || !root.TryGetProperty("originalBytes", out JsonElement size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out long bytes) || bytes < 0
                || !root.TryGetProperty("originalSha256", out JsonElement hash) || hash.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("previous", out JsonElement previous) || !root.TryGetProperty("next", out JsonElement next))
            { throw Incompatible(); }
            string digest = hash.GetString()!;
            if (digest.Length != 64 || digest.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) { throw Incompatible(); }
            FileGroupInventory old = FileGroupInventory.Inspect(previous, path, format, inspectMembers: false), updated = FileGroupInventory.Inspect(next, path, format, inspectMembers: false);
            bool isRecovery = recovery.GetBoolean();
            if (old.Active.Id == long.MaxValue || isRecovery && (format != LogOutputFormat.JsonLines || old.Segments.Count > bound)) { throw Incompatible(); }
            FileGroupInventory expected = isRecovery ? old.AfterPartialRecovery(path, bound) : old.AfterRotation(path, bound);
            if (!expected.HasSameIdentity(updated)) { throw Incompatible(); }
            return new(old, updated, bound, isRecovery, bytes, digest);
        }
    }

    private static JsonDocument ReadDocument(FileStream file, int maximumBytes)
    {
        if (file.Length > maximumBytes) { throw Incompatible(); }
        byte[] bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
        if (file.ReadByte() != -1) { throw Incompatible(); }
        _ = new UTF8Encoding(false, true).GetCharCount(bytes);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 14 });
    }

    private static CoreLogInitializationException Incompatible() => new("file", "FileTransitionIncompatible");
}
