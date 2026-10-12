using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class RenderingStoryExpectations
{
    internal static void Validate(JsonObject catalog, JsonObject row, JsonObject actual)
    {
        try { Check(catalog, row, actual); }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or KeyNotFoundException)
        { throw new InvalidDataException("Rendering story observation is malformed.", error); }
    }
    private static void Check(JsonObject catalog, JsonObject row, JsonObject actual)
    {
        JsonlReuseExpectations.Validate(catalog, row, actual);
        Require(JsonNode.DeepEquals(row["presentation"]!["formats"], JsonNode.Parse("[0,1]")) && JsonNode.DeepEquals(row["presentation"]!["details"], JsonNode.Parse("[0,1]")), "Presentation matrix differs.");
        Require(JsonNode.DeepEquals(actual["afterRendering"], actual["wires"]![0]), "Rendering changed original capture bytes.");
        int count = row["expected"]!["records"]!.GetValue<int>(), cap = row["input"]!["maxEntryBytes"]!.GetValue<int>();
        JsonArray frames = actual["renderFrames"]!.AsArray(); Require(frames.Count == count * 4, "View count differs.");
        var keys = new HashSet<(int Record, int Format, int Detail)>();
        foreach (JsonNode? frame in frames)
        {
            int index = frame!["recordIndex"]!.GetValue<int>(), format = frame["format"]!.GetValue<int>(), detail = frame["detail"]!.GetValue<int>();
            Require(index >= 0 && index < count && format is 0 or 1 && detail is 0 or 1 && keys.Add((index, format, detail)), "View identity differs.");
            byte[] raw = Convert.FromBase64String(actual["wires"]![0]![index]!.GetValue<string>()), line = Convert.FromBase64String(frame["line"]!.GetValue<string>());
            bool lost = format == 1 ? row["expected"]!["truncated"]!.GetValue<bool>() : row["presentation"]![detail == 0 ? "normalTruncated" : "verboseTruncated"]!.GetValue<bool>();
            Require(frame["truncated"]!.GetValue<bool>() == lost && frame["jsonMemoryReused"]!.GetValue<bool>() == (format == 1), "Loss/owned JSONL view differs.");
            if (format == 1) { Require(line.AsSpan().SequenceEqual(raw), "JSONL was rewritten by detail."); continue; }
            using JsonDocument document = JsonDocument.Parse(raw); JsonElement source = document.RootElement;
            Dictionary<string, JsonElement> fields = HumanRenderingExpectations.Read(line, cap);
            var expected = new HashSet<string>(StringComparer.Ordinal) { "timestamp", "severity", "component", "event_name", "message" };
            foreach ((string display, string key) in new[] { ("timestamp", "timestamp"), ("severity", "status"), ("component", "component"), ("event_name", "event_name"), ("message", "message") }) { Equal(fields, display, source.GetProperty(key)); }
            if (lost) { expected.Add("truncated"); Require(fields.TryGetValue("truncated", out JsonElement marker) && marker.ValueKind == JsonValueKind.True, "Visible loss marker differs."); }
            if (detail == 1)
            {
                foreach (string key in new[] { "service", "env", "version", "operation_id", "trace_id", "span_id", "attributes", "exception", "message_template" })
                {
                    bool omitted = row["presentation"]!["verboseOmittedPayload"]!.AsArray().Any(value => value!.GetValue<string>() == key);
                    if (source.TryGetProperty(key, out JsonElement value) && !omitted) { Equal(fields, key, value); expected.Add(key); }
                }
                foreach (JsonProperty property in source.GetProperty("resource").EnumerateObject()) { string key = "resource." + property.Name; Equal(fields, key, property.Value); expected.Add(key); }
            }
            Require(expected.SetEquals(fields.Keys), "Human information field set differs.");
        }
    }
    private static void Equal(Dictionary<string, JsonElement> fields, string key, JsonElement value) => Require(fields.TryGetValue(key, out JsonElement actual) && JsonElement.DeepEquals(value, actual), "Human information/type differs: " + key);
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
