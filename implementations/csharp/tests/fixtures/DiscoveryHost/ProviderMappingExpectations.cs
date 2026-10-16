using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderMappingExpectations
{
    internal static readonly string[] Modes = ["levels", "names", "ids", "payload", "structured"];
    private static void Require(bool condition, string reason)
    {
        if (!condition) { throw new InvalidDataException(reason); }
    }
    private static bool Integer(JsonNode? node, int expected) =>
        node is JsonValue value && value.TryGetValue<int>(out int actual) && actual == expected &&
        node.ToJsonString() == expected.ToString(CultureInfo.InvariantCulture);

    private static bool Same(JsonNode? actual, JsonNode? expected)
    {
        if (actual is null || expected is null) { return actual is null && expected is null; }
        if (actual.GetValueKind() != expected.GetValueKind()) { return false; }
        if (expected is JsonObject obj)
        {
            return actual is JsonObject value && value.Count == obj.Count &&
                obj.All(pair => value.ContainsKey(pair.Key) && Same(value[pair.Key], pair.Value));
        }
        if (expected is JsonArray array)
        {
            return actual is JsonArray value && value.Count == array.Count &&
                array.Select((item, index) => Same(value[index], item)).All(match => match);
        }
        if (expected.GetValueKind() == JsonValueKind.Number)
        {
            bool actualInteger = actual.ToJsonString().IndexOfAny(['.', 'e', 'E']) < 0;
            bool expectedInteger = expected.ToJsonString().IndexOfAny(['.', 'e', 'E']) < 0;
            return actualInteger == expectedInteger && JsonNode.DeepEquals(actual, expected);
        }
        return JsonNode.DeepEquals(actual, expected);
    }

    internal static void Validate(string mode, JsonObject a, int? expectedPid = null, bool physical = false)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode, "mode");
        Require(a["runtime"]!.GetValue<string>() == ".NET 10.0.12", "actual runtime");
        Require(a["pid"] is JsonValue pidValue && pidValue.TryGetValue<int>(out int pid) && pid > 0 &&
            Integer(pidValue, pid) && (expectedPid is null || pid == expectedPid), "actual child PID");
        Require(a["physical"]!.GetValue<bool>() == physical && a["borrowedConsoleOpen"]!.GetValue<bool>() &&
            a["leaseReleased"]!.GetValue<bool>() == physical, "physical lifetime");
        Require(a["invalid"]!.GetValue<string>() == "0", "core invalid count");
        int pairs = mode switch { "levels" => 6, "names" => 3, "ids" => 5, _ => 1 };
        Require(Integer(a["formatterCalls"], pairs), "supplied formatter calls");
        Require(Integer(a["noneFormatterCalls"], 0), "None formatter not called");
        Require(a["formatterExceptionReceived"]!.GetValue<bool>() == (mode is "payload" or "structured"), "formatter exception identity");
        JsonObject result = a["result"]!.AsObject();
        Require(result.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["completion", "succeeded", "loss", "captured", "observed", "outputs"]), "result shape");
        Require(result["completion"]!.GetValue<string>() == "Completed" && result["succeeded"]!.GetValue<bool>() &&
            !result["loss"]!.GetValue<bool>(), "stop summary");
        JsonArray outputs = result["outputs"]!.AsArray();
        Require(outputs.Count == 1 && outputs[0]!["name"]!.GetValue<string>() == (physical ? "file" : "console"), "output selection");
        JsonNode output = outputs[0]!;
        string count = (pairs * 2).ToString(CultureInfo.InvariantCulture);
        Require(output["boundary"]!.GetValue<string>() == count && output["written"]!.GetValue<string>() == count &&
            output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" &&
            output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded", "core delivery");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>());
        Require(bytes.Length > 0 && bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "actual framing");
        string[] lines = new UTF8Encoding(false, true).GetString(bytes)[..^1].Split('\n');
        Require(lines.Length == pairs * 2 && lines.All(line => line.Length > 0 && Encoding.UTF8.GetByteCount(line) + 1 <= 16384), "actual bounded records");
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        string[] statuses = mode == "levels" ? ["trace", "debug", "info", "warn", "error", "critical"] :
            Enumerable.Repeat(mode is "payload" or "structured" ? "error" : "info", pairs).ToArray();
        int[] ids = mode switch { "levels" => [17, 17, 17, 17, 17, 17], "names" => [9, -4, 0], "ids" => [0, 1, -1, int.MinValue, int.MaxValue], _ => [42] };
        string[] categories = mode == "names" ? ["Map.First", "Map.Second", "Map.Empty"] :
            Enumerable.Repeat("Map." + (mode == "levels" ? "Levels" : mode == "ids" ? "Ids" : mode == "structured" ? "Structured" : "Payload"), pairs).ToArray();
        string[] names = mode switch
        {
            "levels" => Enumerable.Repeat("level", pairs).ToArray(),
            "names" => ["named.event", "log.message", "log.message"],
            "ids" => Enumerable.Repeat("id", pairs).ToArray(),
            "structured" => ["structured"],
            _ => ["payload"]
        };
        string[] messages = mode switch
        {
            "levels" => ["level 0", "level 1", "level 2", "level 3", "level 4", "level 5"],
            "names" => ["named", "absent", "empty"],
            "ids" => ["0", "1", "-1", "-2147483648", "2147483647"],
            "structured" => ["Work 3 Ω"],
            _ => ["rendered {x} Ω 🙂"]
        };
        for (int i = 0; i < pairs; i++)
        {
            foreach (JsonObject row in rows.Skip(i * 2).Take(2))
            {
                Require(row["status"]!.GetValue<string>() == statuses[i], "mapped severity");
                Require(row["component"]!.GetValue<string>() == categories[i], "category component");
                Require(row["event_name"]!.GetValue<string>() == names[i], "mapped event name");
                Require(row["message"]!.GetValue<string>() == messages[i], "supplied rendered message");
                Require(Integer(row["attributes"]!["event_id"], ids[i]), "numeric event ID");
                Require(DateTimeOffset.TryParseExact(row["timestamp"]!.GetValue<string>(),
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _), "valid capture timestamp");
                if (mode == "structured")
                {
                    JsonNode? attributes = JsonNode.Parse("""
                        {"event_id":42,"Count":3,"User":"Ω","Money":"1.20","At":"2026-10-14T10:00:00.0000000Z","Items":[1,null,true],"Uuid":"11111111-2222-3333-4444-555555555555","Number":0.10000000149011612,"Large":"9223372036854775807"}
                        """);
                    Require(Same(row["attributes"], attributes), "equivalent structured values");
                    Require(row["message_template"]?.GetValue<string>() == "Work {Count} {User}", "equivalent structured template");
                    string? span = row["span_id"]?.GetValue<string>();
                    Require(row["trace_id"]?.GetValue<string>() == "11111111111111111111111111111111" &&
                        span is { Length: 16 } && span.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') && span.Any(c => c != '0'), "equivalent correlation");
                }
                else { Require(row["attributes"]!.AsObject().Count == 1, "numeric event ID"); }
                Require(Integer(row["schema_version"], 1) &&
                    row["service"]!.GetValue<string>() == (physical ? "CIS.Core.DiscoveryFixture" : "Console.App"), "automatic core facts");
                if (physical)
                {
                    Require(Integer(row["resource"]!["process_id"], a["pid"]!.GetValue<int>()) &&
                        row["resource"]!["runtime_description"]!.GetValue<string>() == ".NET 10.0.12" &&
                        row["resource"]!["os_family"]!.GetValue<string>() == a["os"]!.GetValue<string>(), "automatic core facts");
                }
                if (mode is "payload" or "structured")
                {
                    JsonObject error = row["exception"]!.AsObject();
                    Require(error["type"]!.GetValue<string>() == "System.InvalidOperationException", "exception type");
                    Require(error["message"]!.GetValue<string>() == "controlled mapping failure", "exception message");
                    Require(error.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["type", "message"]), "exception excludes Data");
                }
                else { Require(!row.ContainsKey("exception"), "no fabricated exception"); }
                row.Remove("timestamp");
            }
            Require(Same(rows[i * 2], rows[i * 2 + 1]), "equivalent core record");
        }
    }
}
