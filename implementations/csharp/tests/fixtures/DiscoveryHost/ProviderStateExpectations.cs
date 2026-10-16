using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderStateExpectations
{
    internal static readonly string[] Modes = ["extensions", "scalars", "collections", "precedence", "unsupported", "bounds", "filtered", "correlation", "template-size"];
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

    private static (int Nodes, int Depth) Shape(JsonNode? value)
    {
        IEnumerable<JsonNode?> children = value is JsonObject map ? map.Select(p => p.Value) : value is JsonArray array ? array : [];
        int nodes = 1, depth = 1;
        foreach (JsonNode? child in children)
        {
            var shape = Shape(child); nodes += shape.Nodes; depth = Math.Max(depth, shape.Depth + 1);
        }
        return (nodes, depth);
    }

    internal static void Validate(string mode, JsonObject a, int? expectedPid = null, bool physical = false)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode, "mode");
        Require(a["runtime"]!.GetValue<string>() == ".NET 10.0.12", "actual runtime");
        Require(a["pid"] is JsonValue pidValue && pidValue.TryGetValue<int>(out int pid) && pid > 0 &&
            Integer(pidValue, pid) && (expectedPid is null || pid == expectedPid), "actual child PID");
        Require(a["physical"]!.GetValue<bool>() == physical && a["leaseReleased"]!.GetValue<bool>() == physical &&
            (physical ? a["borrowedConsoleOpen"] is null : a["borrowedConsoleOpen"]!.GetValue<bool>()), "physical lifetime");
        int count = mode is "unsupported" or "filtered" ? 0 : mode == "bounds" ? 3 : 1;
        int calls = mode is "extensions" or "filtered" ? 0 : mode == "unsupported" ? 12 : mode == "bounds" ? 3 : 1;
        Require(Integer(a["formatterCalls"], calls), "formatter calls");
        Require(Integer(a["stateIdentityChecks"], calls), "original state and null exception");
        Require(Integer(a["arbitraryToStringCalls"], 0), "no arbitrary ToString");
        Require(Integer(a["stateEnumerationCalls"], mode == "correlation" ? 1 : 0), "state enumeration");
        Require(Integer(a["arrayEnumerated"], mode == "bounds" ? 1022 : 0), "bounded traversal");
        Require(a["invalid"]!.GetValue<string>() == (mode == "unsupported" ? "12" : "0"), "invalid accounting");
        Require(a["truncated"]!.GetValue<string>() == (mode == "bounds" ? "3" : mode == "template-size" ? "1" : "0"), "truncation accounting");
        JsonObject result = a["result"]!.AsObject();
        Require(result.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["completion", "succeeded", "loss", "captured", "observed", "outputs"]), "result shape");
        Require(result["completion"]!.GetValue<string>() == "Completed" && result["succeeded"]!.GetValue<bool>() &&
            result["loss"]!.GetValue<bool>() == (mode is "unsupported" or "bounds" or "template-size"), "stop summary");
        JsonArray outputs = result["outputs"]!.AsArray();
        Require(outputs.Count == 1 && outputs[0]!["name"]!.GetValue<string>() == (physical ? "file" : "console"), "output selection");
        JsonNode output = outputs[0]!;
        string expectedCount = count.ToString(CultureInfo.InvariantCulture);
        Require(output["boundary"]!.GetValue<string>() == expectedCount && output["written"]!.GetValue<string>() == expectedCount &&
            output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" &&
            output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded", "core delivery");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>());
        Require(count == 0 ? bytes.Length == 0 : bytes.Length > 0 && bytes[^1] == (byte)'\n' &&
            !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "actual framing");
        string[] lines = count == 0 ? [] : new UTF8Encoding(false, true).GetString(bytes)[..^1].Split('\n');
        Require(lines.Length == count && lines.All(line => line.Length > 0 && Encoding.UTF8.GetByteCount(line) + 1 <= 16384), "actual bounded records");
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        string[] names = mode == "bounds" ? ["array-bound", "depth-bound", "field-bound"] : count == 0 ? [] : [mode == "extensions" ? "orders" : mode];
        Require(rows.Select(row => row["event_name"]!.GetValue<string>()).SequenceEqual(names), "event names");
        foreach (JsonObject row in rows)
        {
            Require(Integer(row["schema_version"], 1) && row["status"]!.GetValue<string>() == "info" &&
                row["component"]!.GetValue<string>() == "Provider.State" &&
                row["service"]!.GetValue<string>() == (physical ? "CIS.Core.DiscoveryFixture" : "Console.App"), "automatic core facts");
            if (physical)
            {
                Require(Integer(row["resource"]!["process_id"], a["pid"]!.GetValue<int>()) &&
                    row["resource"]!["runtime_description"]!.GetValue<string>() == ".NET 10.0.12" &&
                    row["resource"]!["os_family"]!.GetValue<string>() == a["os"]!.GetValue<string>(), "automatic core facts");
            }
            Require(Integer(row["attributes"]!["event_id"], 17), "provider numeric ID");
            var shape = Shape(row["attributes"]);
            Require(shape.Nodes <= 1024 && shape.Depth <= 8, "attribute shape");
            Require(mode is "bounds" or "template-size" ? Same(row["truncated"], JsonValue.Create(true)) : !row.ContainsKey("truncated"), "truncation marker");
        }
        if (count == 0) { return; }
        JsonObject first = rows[0]; JsonObject attributes = first["attributes"]!.AsObject();
        if (mode == "extensions")
        {
            Require(first["message"]!.GetValue<string>() == "Orders 3 Ω" && first["message_template"]!.GetValue<string>() == "Orders {Count} {User}", "framework template/message");
            Require(Same(attributes, JsonNode.Parse("""{"event_id":17,"Count":3,"User":"Ω"}""")), "framework fields");
        }
        else { Require(rows.All(row => row["message"]!.GetValue<string>() == "supplied rendered"), "supplied rendered message"); }
        if (mode == "scalars")
        {
            JsonNode? expected = JsonNode.Parse("""
                {"event_id":17,"nil":null,"bool":true,"text":"Ω 🙂","char":"x","sbyte":-8,"byte":8,"short":-16,"ushort":16,"int":-32,"uint":4294967295,"long":9007199254740991,"ulong":9007199254740991,"float":1.5,"double":2.5,"decimal":"1.20","wholeDecimal":"2.0","uuid":"11111111-2222-3333-4444-555555555555","offset":"2026-10-14T10:00:00.0000000Z","utc":"2026-10-14T10:00:00.0000000Z","explicit":"9223372036854775807"}
                """);
            Require(Same(attributes, expected), "scalar normalization");
            Require(first["message_template"]!.GetValue<string>() == "raw {template}", "original format");
        }
        if (mode == "collections")
        {
            Require(Same(attributes, JsonNode.Parse("""{"event_id":17,"nested":{"array":[1,null,true],"typed":{"i":7}},"owned":["original"]}""")) &&
                !first.ContainsKey("message_template"), "copied collections");
        }
        if (mode == "precedence")
        {
            Require(Same(attributes, JsonNode.Parse("""{"event_id":17,"common":"event","choice":2,"service":"fake"}""")) &&
                first["operation_id"]!.GetValue<string>() == "operation.original" && first["message_template"]!.GetValue<string>() == "scope {choice}", "event/scope precedence");
        }
        if (mode == "bounds")
        {
            var array = new JsonArray(Enumerable.Range(0, 1021).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());
            Require(Same(attributes, new JsonObject { ["event_id"] = 17, ["array"] = array }) &&
                Shape(attributes).Nodes == 1024 && first["message_template"]!.GetValue<string>() == "large {array}", "array bound");
            JsonArray deep = [];
            for (int i = 0; i < 6; i++) { deep = new JsonArray(deep); }
            Require(Same(rows[1]["attributes"], new JsonObject { ["event_id"] = 17, ["deep"] = deep }) &&
                Shape(rows[1]["attributes"]) == (9, 8), "depth bound");
            var fields = new JsonObject { ["event_id"] = 17 };
            for (int i = 0; i < 1022; i++) { fields["k" + i.ToString("D4", CultureInfo.InvariantCulture)] = i; }
            Require(Same(rows[2]["attributes"], fields), "field bound");
        }
        if (mode == "correlation")
        {
            string? span = a["expectedSpan"]?.GetValue<string>();
            Require(first["trace_id"]!.GetValue<string>() == "11111111111111111111111111111111" &&
                first["trace_id"]!.GetValue<string>() == a["expectedTrace"]!.GetValue<string>() &&
                first["span_id"]!.GetValue<string>() == span && span is { Length: 16 } &&
                span.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') && span.Any(c => c != '0') &&
                Same(attributes, JsonNode.Parse("""{"event_id":17,"trace_id":"fake"}""")), "frozen Activity");
        }
        if (mode == "template-size")
        {
            Require(first["message_template"]!.GetValue<string>() == new string('x', 8192) &&
                Same(attributes, JsonNode.Parse("""{"event_id":17}""")), "template bound");
        }
    }
}
