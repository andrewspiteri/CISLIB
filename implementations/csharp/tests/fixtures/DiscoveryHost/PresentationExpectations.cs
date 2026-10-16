using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class PresentationExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        try { Check(row, actual); }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or KeyNotFoundException)
        { throw new InvalidDataException("Presentation observation is malformed.", error); }
    }
    private static void Check(JsonObject row, JsonObject actual)
    {
        JsonObject expected = row["expected"]!.AsObject();
        if (actual["caseId"]!.GetValue<string>() != row["id"]!.GetValue<string>()) { throw new InvalidDataException("Case differs."); }
        foreach (string key in new[] { "factoryCalls", "clockCalls", "consoleFiltered", "fileFiltered", "sharedCaptures" })
        { if (actual[key]!.GetValue<int>() != expected[key]!.GetValue<int>()) { throw new InvalidDataException("Capture/admission count differs: " + key); } }
        if (actual["invalidCount"]!.GetValue<long>() != 0 || actual["truncatedCount"]!.GetValue<long>() != 0) { throw new InvalidDataException("Unexpected captured loss."); }
        if (!JsonNode.DeepEquals(expected["enabled"], actual["enabled"])) { throw new InvalidDataException("Presentation changed filtering."); }
        if (!JsonNode.DeepEquals(expected["outcomes"], actual["outcomes"])) { throw new InvalidDataException("Presentation changed outcomes/tickets."); }
        if (actual["ambientOperation"]!.GetValue<string>() != "after-operation" || actual["sampled"]!["spanId"]!.GetValue<string>() == actual["afterSampled"]!["spanId"]!.GetValue<string>())
        { throw new InvalidDataException("Ambient context was not replaced."); }
        foreach (string output in new[] { "console", "file" })
        {
            bool json = row[output + "Format"]!.GetValue<int>() == 1;
            JsonArray frames = actual[output + "Frames"]!.AsArray(), statuses = expected[output + "Statuses"]!.AsArray();
            if (frames.Count != statuses.Count) { throw new InvalidDataException("Presentation changed admission count."); }
            for (int index = 0; index < frames.Count; index++)
            {
                JsonNode frame = frames[index]!;
                byte[] source = Convert.FromBase64String(frame["source"]!.GetValue<string>()), line = Convert.FromBase64String(frame["line"]!.GetValue<string>());
                if (!source.AsSpan().SequenceEqual(Convert.FromBase64String(frame["after"]!.GetValue<string>()))) { throw new InvalidDataException("Rendering mutated the capture."); }
                if (line.Length > 65536 || line.Length == 0 || line[^1] != 10 || line.Count(item => item == 10) != 1) { throw new InvalidDataException("Entry cap/framing differs."); }
                using JsonDocument document = JsonDocument.Parse(source); JsonElement capture = document.RootElement;
                foreach ((string key, string value) in new[] { ("status", statuses[index]!.GetValue<string>()), ("service", "Presentation.App"), ("component", "Presentation"), ("event_name", "presentation.event"), ("message", "literal {user} {missing:format} unmatched { } quote \" slash \\\n\u001b[2J caf\u00e9 \U0001f600\u202e"), ("message_template", "raw {user} {missing:format} unmatched { {{ }}\n\u001b[31m"), ("operation_id", "before-operation"), ("timestamp", "2026-10-12T10:11:12.1234567Z") })
                { if (capture.GetProperty(key).GetString() != value) { throw new InvalidDataException("Captured meaning differs: " + key); } }
                if (capture.GetProperty("attributes").GetProperty("user").GetString() != "CIS_MUST_NOT_INTERPOLATE" || capture.GetProperty("attributes").GetProperty("number").GetInt32() != 42) { throw new InvalidDataException("Template attributes changed."); }
                foreach (string id in new[] { "traceId", "spanId" })
                {
                    string key = id == "traceId" ? "trace_id" : "span_id";
                    if (capture.GetProperty(key).GetString() != actual["sampled"]![id]!.GetValue<string>()) { throw new InvalidDataException("Rendering resampled correlation."); }
                }
                if (frame["truncated"]!.GetValue<bool>() || frame["jsonMemoryReused"]!.GetValue<bool>() != json) { throw new InvalidDataException("Unexpected loss or JSONL byte copy."); }
                if (json)
                {
                    if (!source.AsSpan().SequenceEqual(line)) { throw new InvalidDataException("JSONL was reformatted or changed by human detail."); }
                }
                else
                {
                    Dictionary<string, JsonElement> fields = HumanRenderingExpectations.Read(line, 65536);
                    foreach ((string display, string key) in new[] { ("timestamp", "timestamp"), ("severity", "status"), ("component", "component"), ("event_name", "event_name"), ("message", "message") })
                    { Equal(fields, display, capture.GetProperty(key)); }
                    if (row["detail"]!.GetValue<int>() == 0)
                    { if (fields.Count != 5) { throw new InvalidDataException("Normal detail differs."); } }
                    else
                    {
                        foreach (string key in new[] { "service", "env", "version", "operation_id", "trace_id", "span_id", "attributes", "message_template" }) { Equal(fields, key, capture.GetProperty(key)); }
                        var expectedFields = new HashSet<string>(StringComparer.Ordinal) { "timestamp", "severity", "component", "event_name", "message", "service", "env", "version", "operation_id", "trace_id", "span_id", "attributes", "message_template" };
                        foreach (JsonProperty property in capture.GetProperty("resource").EnumerateObject()) { Equal(fields, "resource." + property.Name, property.Value); expectedFields.Add("resource." + property.Name); }
                        if (!expectedFields.SetEquals(fields.Keys)) { throw new InvalidDataException("Verbose presentation field set differs."); }
                    }
                }
            }
        }
    }
    private static void Equal(Dictionary<string, JsonElement> fields, string key, JsonElement expected)
    {
        if (!fields.TryGetValue(key, out JsonElement value) || !JsonElement.DeepEquals(value, expected)) { throw new InvalidDataException("Display meaning differs: " + key); }
    }
}
