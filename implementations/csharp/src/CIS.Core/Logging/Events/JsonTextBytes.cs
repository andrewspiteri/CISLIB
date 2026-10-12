using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;

namespace CIS.Logging.Events;

// Measure the same encoder used by the writer, without allocating an oversized escaped string.
internal static class JsonTextBytes
{
    internal static JavaScriptEncoder Encoder => JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    internal static int Size(string text, int limit)
    {
        int count = 2;
        Span<char> escaped = stackalloc char[12];
        ReadOnlySpan<char> rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            int cost = ScalarCost(rest, escaped, out int consumed);
            if (cost > limit - count) { return limit + 1; }
            count += cost;
            rest = rest[consumed..];
        }
        return count > limit ? limit + 1 : count;
    }

    internal static string Prefix(string text, int contentBytes, out bool clipped)
    {
        int retained = 0;
        Span<char> escaped = stackalloc char[12];
        while (retained < text.Length)
        {
            int cost = ScalarCost(text.AsSpan(retained), escaped, out int consumed);
            if (cost > contentBytes) { break; }
            contentBytes -= cost;
            retained += consumed;
        }
        clipped = retained != text.Length;
        return clipped ? text[..retained] : text;
    }

    private static int ScalarCost(ReadOnlySpan<char> source, Span<char> escaped, out int consumed)
    {
        if (Rune.DecodeFromUtf16(source, out _, out consumed) != OperationStatus.Done)
        {
            throw new ArgumentException("Use valid Unicode scalar text.", nameof(source));
        }
        if (Encoder.Encode(source[..consumed], escaped, out int read, out int written, isFinalBlock: true) != OperationStatus.Done || read != consumed)
        {
            throw new InvalidOperationException("A Unicode scalar could not be JSON encoded.");
        }
        return Encoding.UTF8.GetByteCount(escaped[..written]);
    }
}
