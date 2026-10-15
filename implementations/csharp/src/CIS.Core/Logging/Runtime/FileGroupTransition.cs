using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CIS.Logging.Runtime;

internal sealed record FileRetirement(FileInventorySegment Segment, long Bytes, string Sha256);
// Published before any destructive retirement. A null retirement list identifies a
// legacy schema-1 intent whose retirements had already happened before publication.
internal sealed record FileGroupTransition(FileGroupInventory Previous, FileGroupInventory Next, int RetainedLimit, bool Recovery, long OriginalBytes, string OriginalSha256, IReadOnlyList<FileRetirement>? Retirements = null)
{
    internal const int MaximumBytes = 2 * FileGroupInventory.MaximumBytes + 1024;
    internal static string PendingPath(string path) => path + ".cis.transition.json";
    internal static FileGroupTransition Begin(FileOutputOwner owner, FileGroupInventory next, int retainedLimit, bool recovery)
    {
        owner.RequireOwnership();
        FileGroupInventory previous = owner.Inventory ?? throw new InvalidOperationException("A transition requires managed identity.");
        long position = owner.ActiveFile.Position;
        string digest;
        try
        {
            owner.ActiveFile.Flush(flushToDisk: true);
            owner.ActiveFile.Position = 0;
            digest = Convert.ToHexStringLower(SHA256.HashData(owner.ActiveFile));
        }
        finally
        {
            owner.ActiveFile.Position = position;
        }

        string directory = Path.GetDirectoryName(owner.AbsolutePath)!;
        int removed = previous.Segments.Count + 1 - next.Segments.Count;
        var retirements = new FileRetirement[removed];
        for (int index = 0; index < removed; index++)
        {
            FileInventorySegment segment = previous.Segments[index];
            try
            {
                using FileStream file = Open(Path.Combine(directory, segment.FileName));
                retirements[index] = new(segment, file.Length, Convert.ToHexStringLower(SHA256.HashData(file)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                throw FileOutputWriteException.RetentionDeleteFailed();
            }
        }

        var intent = new FileGroupTransition(previous, next, retainedLimit, recovery, owner.ActiveFile.Length, digest, Array.AsReadOnly(retirements));
        FileMetadataPublication.Publish(PendingPath(owner.AbsolutePath), intent.Write, overwrite: false);
        return intent;
    }

    private void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema", 2);
        writer.WriteBoolean("recovery", Recovery);
        writer.WriteNumber("retainedLimit", RetainedLimit);
        writer.WriteNumber("originalBytes", OriginalBytes);
        writer.WriteString("originalSha256", OriginalSha256);
        writer.WritePropertyName("previous");
        Previous.Write(writer);
        writer.WritePropertyName("next");
        Next.Write(writer);
        writer.WriteStartArray("retirements");
        foreach (FileRetirement retirement in Retirements!)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", retirement.Segment.Id);
            writer.WriteString("fileName", retirement.Segment.FileName);
            writer.WriteNumber("bytes", retirement.Bytes);
            writer.WriteString("sha256", retirement.Sha256);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal void Retire(string activePath)
    {
        if (Retirements is null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(activePath)!;
        try
        {
            foreach (FileRetirement retirement in Retirements)
            {
                File.Delete(Path.Combine(directory, retirement.Segment.FileName));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw FileOutputWriteException.RetentionDeleteFailed();
        }
    }

    internal static void Complete(string path) => File.Delete(PendingPath(path));
    // The group lease is held. Validate every identity and surviving byte before
    // resuming the exact recorded retire -> rename -> publish -> fresh-active plan.
    internal static bool Reconcile(string path, LogOutputFormat format, int maximumRetainedFiles, long maximumFileBytes)
    {
        try
        {
            FileGroupTransition? intent = Read(path, format);
            if (intent is null)
            {
                return false;
            }

            FileGroupInventory current = ReadInventory(path, format);
            bool before = current.HasSameIdentity(intent.Previous);
            bool after = current.HasSameIdentity(intent.Next);
            if (!before && !after)
            {
                throw Incompatible();
            }

            if (intent.OriginalBytes > maximumFileBytes)
            {
                throw new CoreLogInitializationException("file", "FileSizeLimitExceeded");
            }

            if (intent.Next.Segments.Count - 1 > maximumRetainedFiles)
            {
                throw new CoreLogInitializationException("file", "FileRetentionLimitExceeded");
            }

            string directory = Path.GetDirectoryName(path)!;
            string archivedPath = Path.Combine(directory, intent.Next.Segments[^2].FileName);
            bool active = Exists(path);
            bool archived = Exists(archivedPath);
            if (before ? active == archived : !archived)
            {
                throw Incompatible();
            }

            if (before && active)
            {
                CheckBytes(path, intent.OriginalBytes, intent.OriginalSha256);
            }
            else
            {
                CheckBytes(archivedPath, intent.OriginalBytes, intent.OriginalSha256);
                if (active)
                {
                    using FileStream fresh = Open(path);
                    if (fresh.Length != 0)
                    {
                        throw Incompatible();
                    }
                }
            }

            intent.ValidateMembers(directory);
            intent.Retire(path);
            if (before)
            {
                if (active)
                {
                    File.Move(path, archivedPath, overwrite: false);
                }

                intent.Next.PublishReplacement(path);
            }

            if (!after || !active)
            {
                using var fresh = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            }

            Complete(path);
            return intent.Recovery;
        }
        catch (CoreLogInitializationException error) when (error.ErrorCode is "FileInventoryIncompatible" or "FileRecoveryIdentityExhausted")
        {
            throw Incompatible();
        }
        catch (FileOutputWriteException)
        {
            throw new CoreLogInitializationException("file", "FileRetentionDeleteFailed");
        }
        catch (JsonException)
        {
            throw Incompatible();
        }
        catch (DecoderFallbackException)
        {
            throw Incompatible();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            throw new CoreLogInitializationException("file", "FileTransitionInitializationFailed");
        }
    }

    private void ValidateMembers(string directory)
    {
        int removed = Previous.Segments.Count + 1 - Next.Segments.Count;
        for (int index = 0; index < Previous.Segments.Count - 1; index++)
        {
            string path = Path.Combine(directory, Previous.Segments[index].FileName);
            bool exists = Exists(path);
            if (index >= removed)
            {
                if (!exists)
                {
                    throw Incompatible();
                }
            }
            else if (Retirements is null)
            {
                if (exists)
                {
                    throw Incompatible();
                }
            }
            else if (exists)
            {
                FileRetirement retirement = Retirements[index];
                CheckBytes(path, retirement.Bytes, retirement.Sha256);
            }
        }
    }

    private static void CheckBytes(string path, long bytes, string digest)
    {
        using FileStream file = Open(path);
        if (file.Length != bytes || Convert.ToHexStringLower(SHA256.HashData(file)) != digest)
        {
            throw Incompatible();
        }
    }

    private static bool Exists(string path)
    {
        try
        {
            using FileStream file = Open(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static FileGroupInventory ReadInventory(string path, LogOutputFormat format)
    {
        FileStream file;
        try
        {
            file = Open(path + ".cis.inventory.json");
        }
        catch (FileNotFoundException)
        {
            throw Incompatible();
        }

        using (file)
        using (JsonDocument document = ReadDocument(file, FileGroupInventory.MaximumBytes))
        {
            return FileGroupInventory.Inspect(document.RootElement, path, format, inspectMembers: false);
        }
    }

    private static FileGroupTransition? Read(string path, LogOutputFormat format)
    {
        FileStream file;
        try
        {
            file = Open(PendingPath(path));
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        using (file)
        using (JsonDocument document = ReadDocument(file, MaximumBytes))
        {
            JsonElement root = document.RootElement;
            RequireObject(root);
            if (!root.TryGetProperty("schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version is not (1 or 2) || !root.TryGetProperty("recovery", out JsonElement recovery) || recovery.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !root.TryGetProperty("retainedLimit", out JsonElement limit) || limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out int bound) || bound is < LoggingOptions.MinimumFileRetainedFiles or > LoggingOptions.MaximumFileRetainedFiles || !root.TryGetProperty("originalBytes", out JsonElement size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out long bytes) || bytes < 0 || !root.TryGetProperty("originalSha256", out JsonElement hash) || !root.TryGetProperty("previous", out JsonElement previous) || !root.TryGetProperty("next", out JsonElement next))
            {
                throw Incompatible();
            }

            string digest = Digest(hash);
            FileGroupInventory old = FileGroupInventory.Inspect(previous, path, format, inspectMembers: false);
            FileGroupInventory updated = FileGroupInventory.Inspect(next, path, format, inspectMembers: false);
            bool isRecovery = recovery.GetBoolean();
            if (old.Active.Id == long.MaxValue || isRecovery && (format != LogOutputFormat.JsonLines || version == 1 && old.Segments.Count > bound))
            {
                throw Incompatible();
            }

            FileGroupInventory expected = isRecovery ? old.AfterPartialRecovery(path, bound) : old.AfterRotation(path, bound);
            if (!expected.HasSameIdentity(updated))
            {
                throw Incompatible();
            }

            IReadOnlyList<FileRetirement>? retirements = version == 1 ? null : ReadRetirements(root, old, updated);
            return new(old, updated, bound, isRecovery, bytes, digest, retirements);
        }
    }

    private static IReadOnlyList<FileRetirement> ReadRetirements(JsonElement root, FileGroupInventory previous, FileGroupInventory next)
    {
        int removed = previous.Segments.Count + 1 - next.Segments.Count;
        if (!root.TryGetProperty("retirements", out JsonElement values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != removed)
        {
            throw Incompatible();
        }

        var retirements = new FileRetirement[removed];
        int index = 0;
        foreach (JsonElement value in values.EnumerateArray())
        {
            RequireObject(value);
            FileInventorySegment segment = previous.Segments[index];
            if (!value.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long identity) || identity != segment.Id || !value.TryGetProperty("fileName", out JsonElement name) || name.ValueKind != JsonValueKind.String || name.GetString() != segment.FileName || !value.TryGetProperty("bytes", out JsonElement size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out long bytes) || bytes < 0 || !value.TryGetProperty("sha256", out JsonElement hash))
            {
                throw Incompatible();
            }

            retirements[index++] = new(segment, bytes, Digest(hash));
        }

        return Array.AsReadOnly(retirements);
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Incompatible();
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty member in value.EnumerateObject())
        {
            if (!names.Add(member.Name))
            {
                throw Incompatible();
            }
        }
    }

    private static string Digest(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Incompatible();
        }

        string digest = value.GetString()!;
        if (digest.Length != 64 || digest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw Incompatible();
        }

        return digest;
    }

    private static JsonDocument ReadDocument(FileStream file, int maximumBytes)
    {
        if (file.Length > maximumBytes)
        {
            throw Incompatible();
        }

        byte[] bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1)
        {
            throw Incompatible();
        }

        _ = new UTF8Encoding(false, true).GetCharCount(bytes);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 14 });
    }

    private static CoreLogInitializationException Incompatible() => new("file", "FileTransitionIncompatible");
}
