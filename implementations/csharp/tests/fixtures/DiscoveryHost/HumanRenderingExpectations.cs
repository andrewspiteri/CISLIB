using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Information assertions, not a permanent spacing/member-order snapshot.
internal static class HumanRenderingExpectations
{
    internal static Dictionary<string, JsonElement> Read(byte[] line, int cap)
    {
        if (line.Length > cap || line.Length == 0 || line[^1] != 10) { throw new InvalidDataException("Human byte cap or final LF differs."); }
        string text = new UTF8Encoding(false, true).GetString(line.AsSpan(0, line.Length - 1));
        foreach (Rune scalar in text.EnumerateRunes())
        {
            if (Rune.IsControl(scalar) || Rune.GetUnicodeCategory(scalar) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            { throw new InvalidDataException("Raw terminal control or record boundary."); }
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        int offset = 0;
        while (offset < line.Length - 1)
        {
            while (line[offset] == (byte)' ') { offset++; }
            int equal = Array.IndexOf(line, (byte)'=', offset);
            if (equal < offset) { throw new InvalidDataException("Missing field label."); }
            string name = Encoding.UTF8.GetString(line.AsSpan(offset, equal - offset));
            var reader = new Utf8JsonReader(line.AsSpan(equal + 1));
            using JsonDocument value = JsonDocument.ParseValue(ref reader);
            if (!result.TryAdd(name, value.RootElement.Clone())) { throw new InvalidDataException("Duplicate display field."); }
            offset = equal + 1 + checked((int)reader.BytesConsumed);
            if (offset < line.Length - 1 && line[offset] != (byte)' ') { throw new InvalidDataException("Missing field separation."); }
        }
        return result;
    }

    internal static void Validate(JsonObject row, JsonObject actual)
    {
        int cap = row["maxEntryBytes"]!.GetValue<int>();
        foreach (string flag in new[] { "captureTruncated", "normalTruncated", "verboseTruncated" })
        {
            if (actual[flag]!.GetValue<bool>() != row["expected"]![flag]!.GetValue<bool>()) { throw new InvalidDataException("Unexpected truncation: " + flag); }
        }
        byte[] raw = Convert.FromBase64String(actual["json"]!.GetValue<string>());
        using JsonDocument document = JsonDocument.Parse(raw); JsonElement captured = document.RootElement;
        if (actual["captureUnchanged"]!.GetValue<bool>() != true || actual["clockCalls"]!.GetValue<int>() != 1) { throw new InvalidDataException("Rendering recaptured or changed the event."); }
        foreach (string detail in new[] { "normal", "verbose" })
        {
            byte[] line = Convert.FromBase64String(actual[detail]!.GetValue<string>());
            Dictionary<string, JsonElement> fields = Read(line, cap);
            foreach ((string display, string json) in new[] { ("timestamp", "timestamp"), ("severity", "status"), ("component", "component"), ("event_name", "event_name") })
            { Equal(fields, display, captured.GetProperty(json)); }
            if (!fields.TryGetValue("message", out JsonElement message) || !captured.GetProperty("message").GetString()!.StartsWith(message.GetString()!, StringComparison.Ordinal))
            { throw new InvalidDataException("Required message/prefix differs."); }
            bool truncated = actual[detail + "Truncated"]!.GetValue<bool>();
            if (fields.ContainsKey("truncated") != truncated || truncated && !fields["truncated"].GetBoolean()) { throw new InvalidDataException("Display loss marker differs."); }
            if (actual["captureTruncated"]!.GetValue<bool>() && !truncated) { throw new InvalidDataException("Capture loss disappeared."); }
            if (!truncated) { Equal(fields, "message", captured.GetProperty("message")); }
            string originalMessage = captured.GetProperty("message").GetString()!, retained = message.GetString()!;
            _ = new UTF8Encoding(false, true).GetByteCount(retained);
            if (retained.Length < originalMessage.Length)
            {
                Rune next = Rune.GetRuneAt(originalMessage, retained.Length);
                UnicodeCategory category = Rune.GetUnicodeCategory(next);
                int cost = next.Value is '"' or '\\' or '\n' or '\r' or '\t' or '\b' or '\f' ? 2
                    : Rune.IsControl(next) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? 6 * next.Utf16SequenceLength
                    : next.Utf8SequenceLength;
                if (line.Length + cost <= cap) { throw new InvalidDataException("Message prefix leaves room for its next scalar."); }
            }
            if (detail == "normal")
            {
                if (fields.Count != (truncated ? 6 : 5)) { throw new InvalidDataException("Normal detail differs."); }
                continue;
            }
            foreach (string name in new[] { "service", "env", "version", "operation_id", "trace_id", "span_id" })
            {
                if (captured.TryGetProperty(name, out JsonElement value)) { Equal(fields, name, value); }
                else if (fields.ContainsKey(name)) { throw new InvalidDataException("Invented optional context."); }
            }
            foreach (JsonProperty property in captured.GetProperty("resource").EnumerateObject())
            {
                bool omitted = row["expected"]!["verboseOmittedResources"]!.AsArray().Any(item => item!.GetValue<string>() == property.Name);
                if (!omitted) { Equal(fields, "resource." + property.Name, property.Value); }
                else if (fields.ContainsKey("resource." + property.Name)) { throw new InvalidDataException("Omitted descriptive resource was retained."); }
            }
            foreach (JsonNode? omitted in row["expected"]!["verboseOmittedPayload"]!.AsArray())
            {
                if (fields.ContainsKey(omitted!.GetValue<string>())) { throw new InvalidDataException("Oversized typed payload was retained."); }
            }
            var expectedFields = new HashSet<string>(StringComparer.Ordinal) { "timestamp", "severity", "component", "event_name", "message", "service", "env", "version" };
            if (truncated) { expectedFields.Add("truncated"); }
            foreach (string name in new[] { "operation_id", "trace_id", "span_id" })
            { if (captured.TryGetProperty(name, out _)) { expectedFields.Add(name); } }
            foreach (JsonProperty property in captured.GetProperty("resource").EnumerateObject())
            { if (!row["expected"]!["verboseOmittedResources"]!.AsArray().Any(item => item!.GetValue<string>() == property.Name)) { expectedFields.Add("resource." + property.Name); } }
            foreach (string name in new[] { "attributes", "exception", "message_template" })
            {
                if (captured.TryGetProperty(name, out JsonElement value))
                {
                    if (!row["expected"]!["verboseOmittedPayload"]!.AsArray().Any(item => item!.GetValue<string>() == name)) { Equal(fields, name, value); expectedFields.Add(name); }
                }
                else if (fields.ContainsKey(name)) { throw new InvalidDataException("Invented payload."); }
            }
            if (!expectedFields.SetEquals(fields.Keys)) { throw new InvalidDataException("Verbose field set differs."); }
        }
    }
    private static void Equal(Dictionary<string, JsonElement> fields, string name, JsonElement expected)
    {
        if (!fields.TryGetValue(name, out JsonElement actual) || !JsonElement.DeepEquals(expected, actual))
        { throw new InvalidDataException("Missing or changed display information: " + name); }
    }
}
