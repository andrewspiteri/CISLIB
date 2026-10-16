using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Assert observable bytes and literal outcomes, independently of the shipping size calculator.
internal static class EventEncodingExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        void Require(bool condition, string reason) { if (!condition) { throw new InvalidOperationException(row["id"] + ": " + reason); } }
        Require(row["id"]!.GetValue<string>() == actual["caseId"]!.GetValue<string>(), "Case identity differs.");
        if (row["expected"]!["errorType"] is { } error)
        {
            Require(error.GetValue<string>() == actual["errorType"]?.GetValue<string>(), "Expected envelope failure missing.");
            Require(actual["wire"] is null && actual["utf8Bytes"] is null, "Failure emitted a record."); return;
        }
        string wire = actual["wire"]!.GetValue<string>(); int cap = row["input"]!["maxEntryBytes"]!.GetValue<int>();
        int bytes = Encoding.UTF8.GetByteCount(wire);
        Require(bytes == actual["utf8Bytes"]!.GetValue<int>() && bytes <= cap, "Actual byte limit differs.");
        Require(wire.EndsWith('\n') && wire.Count(c => c == '\n') == 1 && !wire.Contains('\r') && !wire.StartsWith('\uFEFF'), "Record framing differs.");
        JsonObject record = JsonNode.Parse(wire)!.AsObject(); bool clipped = row["expected"]!["truncated"]!.GetValue<bool>();
        Require(actual["truncated"]!.GetValue<bool>() == clipped && (record["truncated"]?.GetValue<bool>() ?? false) == clipped, "Truncation marker differs.");
        Require(record["schema_version"]!.GetValue<int>() == 1 && record["timestamp"]!.GetValue<string>() == "2026-10-12T10:11:12.1234567Z", "Version/time differs.");
        Require(record["service"]!.GetValue<string>() == "Encoding.App" && record["env"]!.GetValue<string>() == "tests" && record["version"]!.GetValue<string>() == "release-4", "Protected identities differ.");
        Require(record["component"]!.GetValue<string>() == "component" && record["event_name"]!.GetValue<string>() == "encoding.event", "Names differ.");
        Require(record["status"]!.GetValue<string>() == "info", "Severity differs.");
        if (row["expected"]!["fillsLimit"]!.GetValue<bool>()) { Require(bytes == cap, "Exact cap including LF differs."); }
        Require(record["resource"] is JsonObject resource && (string?)resource["build_version"] == "1.2.3", "Protected build resource differs.");
        string mode = row["input"]!["mode"]!.GetValue<string>(); JsonObject attributes = record["attributes"]!.AsObject();
        if (mode == "complete")
        {
            Require(record["message"]!.GetValue<string>() == "quote \" slash \\ newline\n café 😀", "Captured text changed.");
            Require(record["operation_id"]!.GetValue<string>() == "scope-operation", "Scope ID differs.");
            Require(record["trace_id"]!.GetValue<string>() == new string('1', 32), "Trace differs.");
            string span = record["span_id"]!.GetValue<string>(); Require(span.Length == 16 && span.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') && span != new string('0', 16) && span != new string('2', 16), "Current span differs.");
            Require(attributes.ContainsKey("null") && attributes["null"] is null, "Explicit null lost.");
            Require(attributes["integer"]!.GetValue<long>() == 9007199254740991 && attributes["integerString"]!.GetValue<string>() == "90071992547409920000" && attributes["decimal"]!.GetValue<string>() == "12.3400", "Scalar types differ.");
            Require(JsonNode.DeepEquals(attributes["array"], JsonNode.Parse("[true,1.25]")), "Array order/types differ.");
            Require(attributes["map"]!.AsObject().Select(pair => pair.Key).SequenceEqual(new[] { "a", "z" }, StringComparer.Ordinal), "Ordinal map order differs.");
            Require(record["message_template"]!.GetValue<string>() == "", "Empty template was omitted.");
        }
        else if (mode == "atomic") { Require(!attributes.ContainsKey("a") && attributes["b"]!.GetValue<int>() == 0, "Indivisible value was corrupted or later value lost."); }
        else if (mode == "prior") { Require(record["message"]!.GetValue<string>() == new string('x', 8192), "Earlier scalar clipping differs."); }
        else if (mode == "exception") { Require(record["exception"]!["truncated"]!.GetValue<bool>() && record["exception"]!["message"]!.GetValue<string>() == "captured failure", "Exception marker/text differs."); }
        else if (mode == "unicode")
        {
            string retained = attributes["payload"]!.GetValue<string>();
            Require(retained.Length > 0 && string.Concat(Enumerable.Repeat("😀\u0001", 10000)).StartsWith(retained, StringComparison.Ordinal), "Scalar prefix was split.");
        }
    }
}
