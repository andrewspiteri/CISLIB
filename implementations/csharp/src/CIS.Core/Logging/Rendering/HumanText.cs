using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CIS.Logging.Rendering;

// Measurement and writing share one scalar escaper. Never allocate an oversized escaped string.
internal static class HumanText
{
    internal static int TextSize(string text, int limit)
    {
        int size = 2;
        if (size > limit) { return limit + 1; }
        Span<char> token = stackalloc char[12];
        foreach (Rune scalar in text.EnumerateRunes())
        {
            int length = Escape(scalar, token);
            int cost = Encoding.UTF8.GetByteCount(token[..length]);
            if (cost > limit - size) { return limit + 1; }
            size += cost;
        }
        return size > limit ? limit + 1 : size;
    }

    internal static int ValueSize(JsonElement value, int limit)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return TextSize(value.GetString()!, limit);
            case JsonValueKind.Array:
                int arraySize = 2;
                if (arraySize > limit) { return limit + 1; }
                foreach (JsonElement child in value.EnumerateArray())
                {
                    arraySize += (arraySize == 2 ? 0 : 1) + ValueSize(child, limit - arraySize);
                    if (arraySize > limit) { return limit + 1; }
                }
                return arraySize > limit ? limit + 1 : arraySize;
            case JsonValueKind.Object:
                int objectSize = 2;
                if (objectSize > limit) { return limit + 1; }
                foreach (JsonProperty child in value.EnumerateObject())
                {
                    objectSize += (objectSize == 2 ? 0 : 1) + TextSize(child.Name, limit - objectSize) + 1;
                    if (objectSize > limit) { return limit + 1; }
                    objectSize += ValueSize(child.Value, limit - objectSize);
                    if (objectSize > limit) { return limit + 1; }
                }
                return objectSize > limit ? limit + 1 : objectSize;
            default:
                int size = value.GetRawText().Length; // Owned JSON numbers, booleans and null are ASCII.
                return size > limit ? limit + 1 : size;
        }
    }

    internal static void AppendValue(StringBuilder output, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: AppendText(output, value.GetString()!, int.MaxValue, out _); break;
            case JsonValueKind.Array:
                output.Append('['); bool firstItem = true;
                foreach (JsonElement child in value.EnumerateArray())
                {
                    if (!firstItem) { output.Append(','); }
                    firstItem = false; AppendValue(output, child);
                }
                output.Append(']'); break;
            case JsonValueKind.Object:
                output.Append('{'); bool firstProperty = true;
                foreach (JsonProperty child in value.EnumerateObject())
                {
                    if (!firstProperty) { output.Append(','); }
                    firstProperty = false; AppendText(output, child.Name, int.MaxValue, out _);
                    output.Append(':'); AppendValue(output, child.Value);
                }
                output.Append('}'); break;
            default: output.Append(value.GetRawText()); break;
        }
    }

    // Returns content bytes; quotes have already been reserved by the caller.
    internal static int AppendText(StringBuilder output, string text, int contentBytes, out bool clipped)
    {
        output.Append('"'); int size = 0, retainedChars = 0;
        Span<char> token = stackalloc char[12];
        foreach (Rune scalar in text.EnumerateRunes())
        {
            int length = Escape(scalar, token);
            int cost = Encoding.UTF8.GetByteCount(token[..length]);
            if (cost > contentBytes - size) { break; }
            output.Append(token[..length]); size += cost; retainedChars += scalar.Utf16SequenceLength;
        }
        output.Append('"'); clipped = retainedChars != text.Length;
        return size;
    }

    private static int Escape(Rune scalar, Span<char> token)
    {
        string? shortEscape = scalar.Value switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\b' => "\\b",
            '\f' => "\\f",
            _ => null
        };
        if (shortEscape is not null) { shortEscape.AsSpan().CopyTo(token); return shortEscape.Length; }
        UnicodeCategory category = Rune.GetUnicodeCategory(scalar);
        if (Rune.IsControl(scalar) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
        {
            Span<char> chars = stackalloc char[2]; int count = scalar.EncodeToUtf16(chars);
            for (int i = 0; i < count; i++)
            {
                token[i * 6] = '\\'; token[i * 6 + 1] = 'u';
                if (!((int)chars[i]).TryFormat(token.Slice(i * 6 + 2, 4), out int written, "X4", CultureInfo.InvariantCulture) || written != 4)
                { throw new InvalidOperationException("A human escape could not be encoded."); }
            }
            return count * 6;
        }
        return scalar.EncodeToUtf16(token);
    }
}
