using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderStateExpectations
{
    internal static readonly string[] Modes = ["extensions", "scalars", "collections", "precedence", "unsupported", "bounds", "filtered", "correlation", "template-size"];
    private static void Require(bool condition) { if (!condition) { throw new InvalidDataException("Provider state expectation failed."); } }
    private static (int Nodes, int Depth) Shape(JsonNode? value)
    {
        IEnumerable<JsonNode?> children = value is JsonObject map ? map.Select(p => p.Value) : value is JsonArray array ? array : [];
        int nodes = 1, depth = 1; foreach (JsonNode? child in children) { var shape = Shape(child); nodes += shape.Nodes; depth = Math.Max(depth, shape.Depth + 1); }
        return (nodes, depth);
    }
    internal static void Validate(string mode, JsonObject a)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        bool physical = a["physical"]!.GetValue<bool>(); Require(a["leaseReleased"]!.GetValue<bool>() == physical && (physical ? a["borrowedConsoleOpen"] is null : a["borrowedConsoleOpen"]!.GetValue<bool>()));
        int count = mode is "unsupported" or "filtered" ? 0 : mode == "bounds" ? 3 : 1;
        int calls = mode is "extensions" or "filtered" ? 0 : mode == "unsupported" ? 12 : mode == "bounds" ? 3 : 1;
        Require(a["formatterCalls"]!.GetValue<int>() == calls && a["stateIdentityChecks"]!.GetValue<int>() == calls && a["arbitraryToStringCalls"]!.GetValue<int>() == 0);
        Require(a["stateEnumerationCalls"]!.GetValue<int>() == (mode == "correlation" ? 1 : 0) && a["arrayEnumerated"]!.GetValue<int>() == (mode == "bounds" ? 1022 : 0));
        Require(a["invalid"]!.GetValue<string>() == (mode == "unsupported" ? "12" : "0") && a["truncated"]!.GetValue<string>() == (mode == "bounds" ? "3" : mode == "template-size" ? "1" : "0"));
        Require(a["result"]!["completion"]!.GetValue<string>() == "Completed" && a["result"]!["succeeded"]!.GetValue<bool>() && a["result"]!["loss"]!.GetValue<bool>() == (mode is "unsupported" or "bounds" or "template-size"));
        JsonArray outputs = a["result"]!["outputs"]!.AsArray(); Require(outputs.Count == 1); JsonNode output = outputs[0]!;
        Require(output["name"]!.GetValue<string>() == (physical ? "file" : "console") && long.Parse(output["boundary"]!.GetValue<string>(), CultureInfo.InvariantCulture) == count && long.Parse(output["written"]!.GetValue<string>(), CultureInfo.InvariantCulture) == count && output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" && output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>());
        if (count == 0) { Require(bytes.Length == 0); return; }
        Require(bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries); Require(lines.Length == count && lines.All(s => Encoding.UTF8.GetByteCount(s) + 1 <= 16384));
        JsonObject[] rows = lines.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        foreach (JsonObject row in rows)
        {
            Require(row["status"]!.GetValue<string>() == "info" && row["component"]!.GetValue<string>() == "Provider.State" && row["attributes"]!["event_id"]!.GetValue<int>() == 17);
            var shape = Shape(row["attributes"]); Require(shape.Nodes <= 1024 && shape.Depth <= 8);
            Require(row["truncated"] is null ? mode is not ("bounds" or "template-size") : row["truncated"]!.GetValue<bool>() && mode is "bounds" or "template-size");
        }
        JsonObject first = rows[0]; JsonObject attributes = first["attributes"]!.AsObject();
        if (mode == "extensions")
        { Require(first["event_name"]!.GetValue<string>() == "orders" && first["message"]!.GetValue<string>() == "Orders 3 Ω" && first["message_template"]!.GetValue<string>() == "Orders {Count} {User}"); Require(attributes.Count == 3 && attributes["Count"]!.GetValue<int>() == 3 && attributes["User"]!.GetValue<string>() == "Ω"); }
        else { Require(rows.All(row => row["message"]!.GetValue<string>() == "supplied rendered")); }
        if (mode == "scalars")
        {
            JsonObject expected = JsonNode.Parse("{\"event_id\":17,\"nil\":null,\"bool\":true,\"text\":\"Ω 🙂\",\"char\":\"x\",\"sbyte\":-8,\"byte\":8,\"short\":-16,\"ushort\":16,\"int\":-32,\"uint\":4294967295,\"long\":9007199254740991,\"ulong\":9007199254740991,\"float\":1.5,\"double\":2.5,\"decimal\":\"1.20\",\"wholeDecimal\":\"2.0\",\"uuid\":\"11111111-2222-3333-4444-555555555555\",\"offset\":\"2026-10-14T10:00:00.0000000Z\",\"utc\":\"2026-10-14T10:00:00.0000000Z\",\"explicit\":\"9223372036854775807\"}")!.AsObject();
            Require(JsonNode.DeepEquals(attributes, expected) && first["message_template"]!.GetValue<string>() == "raw {template}");
        }
        if (mode == "collections")
        { Require(JsonNode.DeepEquals(attributes, JsonNode.Parse("{\"event_id\":17,\"nested\":{\"array\":[1,null,true],\"typed\":{\"i\":7}},\"owned\":[\"original\"]}")) && first["message_template"] is null); }
        if (mode == "precedence")
        { Require(attributes.Count == 4 && attributes["common"]!.GetValue<string>() == "event" && attributes["choice"]!.GetValue<int>() == 2 && attributes["service"]!.GetValue<string>() == "fake" && first["service"]!.GetValue<string>() != "fake" && first["operation_id"]!.GetValue<string>() == "operation.original" && first["message_template"]!.GetValue<string>() == "scope {choice}"); }
        if (mode == "bounds")
        {
            Require(rows.Select(row => row["event_name"]!.GetValue<string>()).SequenceEqual(new[] { "array-bound", "depth-bound", "field-bound" }));
            Require(attributes["array"]!.AsArray().Count == 1021 && attributes["array"]![1020]!.GetValue<int>() == 1020 && Shape(attributes).Nodes == 1024 && first["message_template"]!.GetValue<string>() == "large {array}");
            Require(Shape(rows[1]["attributes"]).Depth == 8 && Shape(rows[1]["attributes"]).Nodes == 9);
            Require(rows[2]["attributes"]!.AsObject().Count == 1023 && rows[2]["attributes"]!["k1021"]!.GetValue<int>() == 1021 && rows[2]["attributes"]!["k1022"] is null);
        }
        if (mode == "correlation")
        { Require(first["trace_id"]!.GetValue<string>() == a["expectedTrace"]!.GetValue<string>() && first["span_id"]!.GetValue<string>() == a["expectedSpan"]!.GetValue<string>() && attributes["trace_id"]!.GetValue<string>() == "fake" && attributes.Count == 2); }
        if (mode == "template-size") { Require(first["message_template"]!.GetValue<string>() == new string('x', 8192) && attributes.Count == 1); }
    }
}
