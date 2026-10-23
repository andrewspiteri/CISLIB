using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class WireBytes
{
    internal sealed record Frame(int Start, int Prefix, int Length, JsonObject Message);
    internal static byte[] Transfer(string body)
    {
        bool send = body.StartsWith("sendto(", StringComparison.Ordinal), receive = body.StartsWith("recvmsg(", StringComparison.Ordinal);
        Need(send || receive, "Transfer syscall unsupported.");
        Match returned = Regex.Match(body, @"\) = (-?\d+)(?: [A-Z][A-Z0-9_]* \([^\r\n]*\))?$", RegexOptions.CultureInvariant);
        Need(returned.Success, "Transfer return code absent.");
        int count = int.Parse(returned.Groups[1].Value, CultureInfo.InvariantCulture);
        if (count < 0) { Need(body.EndsWith("EAGAIN (Resource temporarily unavailable)", StringComparison.Ordinal), "Transfer failed unexpectedly."); return []; }
        string encoded;
        if (send)
        {
            Match buffer = Regex.Match(body, "^sendto\\([^,]+, \"((?:\\\\x[0-9a-f]{2})*)\", (\\d+), 0, NULL, 0\\) = (\\d+)$", RegexOptions.CultureInvariant);
            Need(buffer.Success, "Sendto flags/length/bytes unsupported."); encoded = buffer.Groups[1].Value;
            Need(encoded.Length / 4 == int.Parse(buffer.Groups[2].Value, CultureInfo.InvariantCulture), "Submitted send buffer is incomplete.");
        }
        else
        {
            if (count == 0) { return []; }
            Need(body.Contains("msg_name=NULL, msg_namelen=0, msg_iov=[", StringComparison.Ordinal) && body.Contains("msg_controllen=0, msg_flags=0}, 0) = ", StringComparison.Ordinal), "Recvmsg flags or control data unsupported.");
            Match buffer = Regex.Match(body, "msg_iov=\\[\\{iov_base=\"((?:\\\\x[0-9a-f]{2})*)\", iov_len=(\\d+)\\}\\], msg_iovlen=1,", RegexOptions.CultureInvariant);
            Need(buffer.Success, "Recvmsg iovec incomplete or unsupported."); encoded = buffer.Groups[1].Value;
            Need(encoded.Length / 4 == count && count <= int.Parse(buffer.Groups[2].Value, CultureInfo.InvariantCulture), "Returned recv bytes are incomplete.");
        }
        Need(encoded.Length / 4 >= count && count <= 33_554_432, "Actual transfer prefix is unbounded/incomplete.");
        byte[] data = new byte[count];
        for (int i = 0; i < count; i++) { data[i] = byte.Parse(encoded.AsSpan(i * 4 + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
        return data;
    }

    internal static Frame[] Frames(byte[] data)
    {
        var result = new List<Frame>(); int at = 0;
        while (at < data.Length)
        {
            int start = at, length = 0, prefix = 0;
            for (int shift = 0; ; shift += 7)
            {
                Need(at < data.Length && shift < 35, "Incomplete or overflowing frame prefix."); byte b = data[at++]; prefix++;
                Need(shift != 28 || (b & 0xf8) == 0, "Frame prefix exceeds positive Int32."); length |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0) { Need(prefix == 1 || b != 0, "Noncanonical frame prefix."); break; }
            }
            Need(length > 0 && length <= 33_554_432 && data.Length - at >= length, "Message frame incomplete or oversized.");
            string text = new UTF8Encoding(false, true).GetString(data, at, length);
            using JsonDocument document = JsonDocument.Parse(text); Unique(document.RootElement);
            JsonObject message = JsonNode.Parse(text)!.AsObject();
            Need(message["MessageType"] is JsonValue value && value.TryGetValue<string>(out string? kind) && !string.IsNullOrWhiteSpace(kind), "Typed message absent.");
            result.Add(new Frame(start, prefix, length, message)); at += length;
        }
        return result.ToArray();
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject()) { Need(keys.Add(property.Name), "Duplicate JSON property."); Unique(property.Value); }
        }
        if (value.ValueKind == JsonValueKind.Array) { foreach (JsonElement item in value.EnumerateArray()) { Unique(item); } }
    }
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
}
