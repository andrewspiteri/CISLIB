using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Literal shared expectations and independent inspection of the actual encoded bytes.
internal static class EventQualificationExpectations
{
    internal static void Validate(JsonObject catalog, JsonObject row, JsonObject actual)
    {
        Require(actual["caseId"]!.GetValue<string>() == row["id"]!.GetValue<string>(), "Case identity differs.");
        string mode = row["input"]!["mode"]!.GetValue<string>();
        JsonArray records = actual["records"]!.AsArray();
        Require(records.Count == row["expected"]!["records"]!.GetValue<int>(), "Record count differs.");
        if (mode == "none")
        {
            Require(actual["noneSkipped"]!.GetValue<bool>() && actual["clockCalls"]!.GetValue<int>() == 0, "None performed projection.");
            return;
        }
        JsonObject common = catalog["common"]!.AsObject();
        var spans = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < records.Count; index++)
        {
            JsonObject item = records[index]!.AsObject();
            string wire = item["wire"]!.GetValue<string>();
            int bytes = new UTF8Encoding(false, true).GetByteCount(wire);
            Require(wire.EndsWith('\n') && wire.Count(value => value == '\n') == 1 && !wire.Contains('\r') && !wire.StartsWith('\uFEFF'), "JSONL framing differs.");
            Require(bytes == item["utf8Bytes"]!.GetValue<int>() && bytes <= row["input"]!["maxEntryBytes"]!.GetValue<int>(), "Encoded entry exceeds actual byte cap.");
            JsonObject record = JsonNode.Parse(wire)!.AsObject();
            Require(record["schema_version"]!.GetValue<int>() == 1, "Schema differs.");
            foreach (string field in new[] { "timestamp", "env", "version", "component", "event_name" }) { Equal(common[field], record[field], field); }
            Equal(row["expected"]!["status"], record["status"], "status");
            string service = common["service"]!.GetValue<string>() + (mode == "stores" ? ".Store" + index : "");
            Require(record["service"]!.GetValue<string>() == service, "Protected service changed.");
            bool truncated = row["expected"]!["truncated"]!.GetValue<bool>();
            Require(item["truncated"]!.GetValue<bool>() == truncated && (record["truncated"]?.GetValue<bool>() ?? false) == truncated, "Loss state differs.");
            Require(truncated || !record.ContainsKey("truncated"), "False top-level loss marker was emitted.");
            JsonObject expectedResource = common["resource"]!.DeepClone().AsObject();
            if (row["expected"]!["environmentKind"] is JsonNode kind) { expectedResource["environment_kind"] = kind.DeepClone(); }
            Resource(expectedResource, record["resource"]!.AsObject(), mode);
            if (item["sampled"] is JsonObject sampled)
            {
                Equal(sampled["traceId"], record["trace_id"], "trace_id"); Equal(sampled["spanId"], record["span_id"], "span_id");
                string trace = record["trace_id"]!.GetValue<string>(), span = record["span_id"]!.GetValue<string>();
                Require(Hex(trace, 32) && Hex(span, 16) && span != new string('2', 16), "W3C pair is invalid or uses the parent span.");
                if (mode is "concurrent" or "stores") { Require(spans.Add(span), "Concurrent spans were shared."); }
            }
            else { Require(!record.ContainsKey("trace_id") && !record.ContainsKey("span_id"), "Unavailable correlation was emitted."); }
            if (mode is "fields" or "delayed" or "child")
            {
                Complete(common, record, mode, index);
                Equal(actual["originalStack"], record["exception"]!["stack_trace"], "Captured exception stack");
            }
            else if (mode is "absent" or "empty")
            {
                Require(record["message"]!.GetValue<string>() == "" && record["attributes"]!.AsObject().Count == 0, "Empty required payload differs.");
                foreach (string absent in new[] { "operation_id", "exception", "trace_id", "span_id", "truncated" }) { Require(!record.ContainsKey(absent), "Absent optional field was emitted: " + absent); }
                Require(mode == "empty" ? record["message_template"]!.GetValue<string>() == "" : !record.ContainsKey("message_template"), "Empty/absent template differs.");
                Keys(record, "schema_version timestamp status event_name message service env version component resource attributes" + (mode == "empty" ? " message_template" : ""));
            }
            else if (mode is "concurrent" or "stores")
            {
                string suffix = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Require(record["message"]!.GetValue<string>() == "worker-" + suffix && record["operation_id"]!.GetValue<string>() == (index % 2 == 0 ? "operation-" : "event-") + suffix, "Worker operation/payload leaked.");
                JsonObject attributes = record["attributes"]!.AsObject();
                Require(attributes["worker"]!.GetValue<int>() == index && attributes["shared"]!.GetValue<string>() == "event-" + suffix && attributes["scopeOnly"]!.GetValue<string>() == "outer", "Worker attributes leaked.");
                Keys(attributes, "worker shared scopeOnly");
                Require(item["scopeRestored"]!.GetValue<bool>() && item["activityRestored"]!.GetValue<bool>() && item["callerChanged"]!.GetValue<bool>(), "Worker cleanup/mutation did not occur.");
            }
            else if (mode == "aggregate")
            {
                Equal(common["attributes"]!["text"], record["message"], "Higher priority message");
                string retained = record["attributes"]!["a"]!.GetValue<string>();
                Require(retained.Length > 0 && string.Concat(Enumerable.Repeat("\U0001f600\u0001", row["input"]!["maxEntryBytes"]!.GetValue<int>())).StartsWith(retained, StringComparison.Ordinal), "Escaped scalar prefix differs.");
                Require(!record["attributes"]!.AsObject().ContainsKey("z") && !record.ContainsKey("exception") && !record.ContainsKey("message_template"), "Lower priority content won the byte budget.");
                JsonObject larger = JsonNode.Parse(actual["largerWire"]!.GetValue<string>())!.AsObject();
                Require(!actual["largerTruncated"]!.GetValue<bool>() && !larger.ContainsKey("truncated") && larger["attributes"]!["a"]!.GetValue<string>().Length > retained.Length && larger["attributes"]!["z"]!.GetValue<string>() == "1.0000", "Byte fitting mutated the owned input.");
            }
            else if (mode == "sibling")
            {
                JsonObject error = record["exception"]!.AsObject();
                Require(error["message"]!.GetValue<string>() == "root" && error["truncated"]!.GetValue<bool>(), "Omitted child loss is not local to its parent.");
                JsonArray children = error["inner_exceptions"]!.AsArray();
                Require(children.Count == 1 && children[0]!["type"]!.GetValue<string>() == "System.Exception" && children[0]!["message"]!.GetValue<string>() == "small" && !children[0]!.AsObject().ContainsKey("truncated"), "Later fitting sibling was lost or corrupted.");
            }
            Require(!wire.Contains("CIS_EXCLUDED_EXCEPTION_DATA", StringComparison.Ordinal) && !wire.Contains("CIS_CHANGED_EXCEPTION_DATA", StringComparison.Ordinal) && !wire.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal), "Excluded data leaked.");
        }
        if (mode == "delayed")
        {
            JsonObject later = actual["laterState"]!.AsObject();
            Require(later["operationId"]!.GetValue<string>() == "replacement" && later["callerChanged"]!.GetValue<bool>() && later["exceptionDataChanged"]!.GetValue<bool>() && later["scopeOnly"]!.GetValue<string>() == "replacement-scope", "Delayed caller/scope mutation did not occur.");
            Require(later["traceId"]!.GetValue<string>() != records[0]!["sampled"]!["traceId"]!.GetValue<string>() && later["spanId"]!.GetValue<string>() != records[0]!["sampled"]!["spanId"]!.GetValue<string>(), "Replacement activity was not distinct.");
        }
        if (mode is "child" or "fields" or "delayed") { Require(actual["storeEmpty"]!.GetValue<bool>(), "Owner scope cleanup failed."); }
        if (mode == "child")
        {
            Equal(records[0]!["sampled"], records[2]!["sampled"], "Restored parent activity");
            Require(records[0]!["sampled"]!["spanId"]!.GetValue<string>() != records[1]!["sampled"]!["spanId"]!.GetValue<string>() && records[0]!["sampled"]!["traceId"]!.GetValue<string>() == records[1]!["sampled"]!["traceId"]!.GetValue<string>(), "Child activity did not inherit trace with a new span.");
        }
        if (mode is "concurrent" or "stores")
        {
            Require(actual["parentHeadsUnchanged"]!.GetValue<bool>() && actual["parentActivityUnchanged"]!.GetValue<bool>() && actual["storesEmpty"]!.GetValue<bool>(), "Concurrent parent/owner state changed.");
            foreach (JsonNode? parent in actual["parentWhileBlocked"]!.AsArray()) { Require(parent!["operationId"]!.GetValue<string>() == "parent" && parent["shared"]!.GetValue<string>() == "parent", "Blocked worker affected parent scope."); }
        }
    }

    private static void Complete(JsonObject common, JsonObject record, string mode, int index)
    {
        JsonObject attributes = common["attributes"]!.DeepClone().AsObject();
        if (mode == "delayed") { attributes["scopeOnly"] = "original-scope"; }
        Equal(attributes, record["attributes"], "Typed attributes");
        Require(record["attributes"]!["map"]!.AsObject().Select(pair => pair.Key).SequenceEqual(["a", "z"]), "Map keys are not ordinal.");
        Require(record["message"]!.GetValue<string>() == "quote \" slash \\ newline\n caf\u00e9 \U0001f600" && record["message_template"]!.GetValue<string>() == "template {value}", "Supplied text changed.");
        Require(record["operation_id"]?.GetValue<string>() == (mode == "child" ? index == 1 ? "child" : "outer" : mode == "delayed" ? "inner" : "event"), "Scope/event ID precedence differs.");
        JsonObject error = record["exception"]!.AsObject();
        Require(error["type"]!.GetValue<string>() == "System.InvalidOperationException" && error["message"]!.GetValue<string>() == "captured failure" && error["stack_trace"]!.GetValue<string>().Length > 0, "Exception fields differ.");
        Keys(error, "type message stack_trace inner_exceptions");
        JsonArray children = error["inner_exceptions"]!.AsArray();
        Require(children.Count == 1 && children[0]!["type"]!.GetValue<string>() == "System.ArgumentException" && children[0]!["message"]!.GetValue<string>() == "inner", "Exception children differ.");
        Keys(children[0]!.AsObject(), "type message");
        Keys(record, "schema_version timestamp status event_name message service env version component resource attributes operation_id trace_id span_id exception message_template");
    }
    private static void Resource(JsonObject expected, JsonObject actual, string mode)
    {
        string[] optional = "process_started_at host_name os_description os_architecture process_architecture runtime_description process_id commit_id build_id".Split(' ');
        foreach ((string key, JsonNode? value) in expected)
        {
            bool omitted = mode is "absent" or "empty" && optional.Contains(key, StringComparer.Ordinal) || mode is "aggregate" or "sibling" && key is "os_description" or "runtime_description";
            if (omitted) { Require(!actual.ContainsKey(key), "Unavailable/lower priority resource was emitted: " + key); }
            else { Equal(value, actual[key], key); }
        }
        Require(actual.Count == expected.Count - (mode is "absent" or "empty" ? 9 : mode is "aggregate" or "sibling" ? 2 : 0), "Resource allowlist differs.");
    }
    private static bool Hex(string text, int size) => text.Length == size && text.Any(character => character != '0') && text.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Keys(JsonObject value, string names) => Require(value.Select(pair => pair.Key).Order(StringComparer.Ordinal).SequenceEqual(names.Split(' ').Order(StringComparer.Ordinal)), "Field set differs.");
    private static void Equal(JsonNode? expected, JsonNode? actual, string field) => Require(JsonNode.DeepEquals(expected, actual), field + " differs.");
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
