using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using CIS.Messaging;
using CIS.RuleEngine;

namespace CIS.Management;

/// <summary>A validated Rule Engine definition carried by one immutable RM08 application source.</summary>
public sealed class ManagementRuleDefinitionCarrier
{
    private const int MaximumSourceBytes = 256 * 1024;
    private const int ChunkBytes = 48 * 1024;
    private const int MaximumChunkCharacters = ChunkBytes / 3 * 4;
    private const int MaximumChunks = (RuleBounds.MaximumDefinitionBytes + ChunkBytes - 1) / ChunkBytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] definitionBytes;

    private ManagementRuleDefinitionCarrier(byte[] bytes, RuleDefinition definition)
    {
        definitionBytes = bytes;
        Definition = definition;
    }

    /// <summary>Gets the validated definition in schema and ordinal order.</summary>
    public RuleDefinition Definition
    {
        get;
    }

    /// <summary>Returns a defensive copy of the exact UTF-8 definition carried by the application source.</summary>
    public byte[] GetDefinitionBytes() => definitionBytes.ToArray();

    /// <summary>Validates and packs exact definition bytes into a bounded RM08 source JSON object.</summary>
    /// <exception cref="ArgumentException">The definition is malformed or exceeds the portable profile.</exception>
    public static string Encode(ReadOnlySpan<byte> definitionUtf8)
    {
        _ = RuleDefinitionCodec.Decode(definitionUtf8);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteStartArray("definitionChunks");
        while (!definitionUtf8.IsEmpty)
        {
            int count = Math.Min(definitionUtf8.Length, ChunkBytes);
            writer.WriteStringValue(Base64Url.EncodeToString(definitionUtf8[..count]));
            definitionUtf8 = definitionUtf8[count..];
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return StrictUtf8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Unpacks the exact application source and validates the definition before returning it.</summary>
    /// <exception cref="ArgumentException">The source has no definition or violates either transport profile.</exception>
    public static ManagementRuleDefinitionCarrier Decode(string sourceJson)
    {
        ArgumentNullException.ThrowIfNull(sourceJson);
        byte[] source = EncodeSource(sourceJson);
        const int carrierDepth = 2;
        if (!MessageWire.ValidateJson(source, carrierDepth))
        {
            throw Invalid("Use a strict JSON object with unique source members.");
        }

        using JsonDocument document = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = carrierDepth });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("definitionChunks", out JsonElement list)
            || root.EnumerateObject().Count() != 1 || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() is < 1 or > MaximumChunks)
        {
            throw Invalid("The application source must contain only a nonempty definitionChunks array within the chunk limit.");
        }

        var chunks = new (string Text, int Length)[list.GetArrayLength()];
        int total = 0;
        int index = 0;
        foreach (JsonElement item in list.EnumerateArray())
        {
            string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (text is null || text.Length is < 1 or > MaximumChunkCharacters
                || !text.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                || !Base64Url.IsValid(text, out int length)
                || length is < 1 or > ChunkBytes
                || index < chunks.Length - 1 && length != ChunkBytes
                || length > RuleBounds.MaximumDefinitionBytes - total)
            {
                throw Invalid("The application source contains an invalid or oversized base64url chunk.");
            }
            chunks[index] = (text, length);
            total += length;
            index++;
        }

        var definition = new byte[total];
        int position = 0;
        foreach ((string text, int length) in chunks)
        {
            Span<byte> chunk = definition.AsSpan(position, length);
            int written = Base64Url.DecodeFromChars(text, chunk);
            if (written != length || !string.Equals(text, Base64Url.EncodeToString(chunk), StringComparison.Ordinal))
            {
                throw Invalid("Use canonical unpadded base64url chunks.");
            }
            position += length;
        }

        return new(definition, RuleDefinitionCodec.Decode(definition));
    }

    private static byte[] EncodeSource(string sourceJson)
    {
        if (sourceJson.Length > MaximumSourceBytes)
        {
            throw Invalid("The application source exceeds the RM08 source limit.");
        }
        try
        {
            if (StrictUtf8.GetByteCount(sourceJson) > MaximumSourceBytes)
            {
                throw Invalid("The application source exceeds the RM08 source limit.");
            }
            return StrictUtf8.GetBytes(sourceJson);
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("The application source contains invalid Unicode.", nameof(sourceJson), error);
        }
    }

    private static ArgumentException Invalid(string message) => new(message, "sourceJson");
}
