using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderBehaviorExpectations
{
    internal static readonly string[] Modes = ["filtered", "no-outputs", "stopped", "disposed", "all-faulted", "partial-fault", "full", "template-tail", "float", "duplicates", "root-shape", "rejection", "capture-failure", "formatter-failure"];
    private static void Require(bool value) { if (!value) { throw new InvalidDataException("Provider behavior expectation failed."); } }
    internal static void Validate(string mode, JsonObject a)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        bool physical = a["physical"]!.GetValue<bool>(); Require(a["leaseReleased"]!.GetValue<bool>() == physical);
        int calls = mode switch { "filtered" or "no-outputs" or "stopped" or "disposed" => 0, "partial-fault" or "capture-failure" or "formatter-failure" => 2, "full" => 3, "rejection" => 19, _ => 1 };
        int states = mode switch { "all-faulted" or "capture-failure" or "formatter-failure" => 1, "partial-fault" => 2, "full" or "rejection" => 3, _ => 0 };
        Require(a["formatterCalls"]!.GetValue<int>() == calls && a["stateCalls"]!.GetValue<int>() == states);
        Require(a["enabledBefore"]!.GetValue<bool>() == (mode is not ("filtered" or "no-outputs")) && a["enabledAtProbe"]!.GetValue<bool>() == (mode is not ("filtered" or "no-outputs" or "stopped" or "disposed" or "all-faulted")));
        int invalid = mode == "rejection" ? 19 : mode == "capture-failure" ? 1 : 0;
        Require(a["invalid"]!.GetValue<string>() == invalid.ToString(System.Globalization.CultureInfo.InvariantCulture) && a["rejected"]!.GetValue<int>() == invalid);
        Require(a["truncated"]!.GetValue<string>() == (mode == "template-tail" ? "2" : "0") && a["exceptionIdentity"]!.GetValue<bool>() == (mode == "formatter-failure"));
        JsonArray outputs = a["outputs"]!.AsArray(); Require(outputs.Count == (mode == "no-outputs" ? 0 : mode == "partial-fault" ? 2 : 1));
        foreach (JsonNode? node in outputs)
        {
            JsonObject output = node!.AsObject(); bool failed = mode is "all-faulted" or "partial-fault" && output["name"]!.GetValue<string>() == "console";
            int accepted = failed ? 1 : mode is "filtered" or "stopped" or "disposed" ? 0 : mode is "partial-fault" or "full" or "template-tail" ? 2 : 1;
            Require(output["accepted"]!.GetValue<string>() == accepted.ToString(System.Globalization.CultureInfo.InvariantCulture) && output["written"]!.GetValue<string>() == (failed ? "0" : accepted.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Require(output["failed"]!.GetValue<string>() == (failed ? "1" : "0") && output["unavailable"]!.GetValue<string>() == (failed ? "1" : "0"));
            Require(output["full"]!.GetValue<string>() == (mode == "full" ? "1" : "0") && output["filtered"]!.GetValue<string>() == (mode == "filtered" ? "2" : "0"));
            Require(output["abandoned"]!.GetValue<string>() == "0" && output["pending"]!.GetValue<string>() == "0" && (failed ? output["fault"] is not null : output["fault"] is null));
        }
        Require(a["result"]!["succeeded"]!.GetValue<bool>() == (mode is not ("all-faulted" or "partial-fault")));
        Require(a["result"]!["loss"]!.GetValue<bool>() == (mode is "all-faulted" or "partial-fault" or "full" or "template-tail" or "rejection" or "capture-failure"));
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>()); byte[] file = Convert.FromBase64String(a["fileBytes"]!.GetValue<string>());
        int count = mode is "filtered" or "no-outputs" or "stopped" or "disposed" or "all-faulted" or "partial-fault" ? 0 : mode is "full" or "template-tail" ? 2 : 1;
        JsonObject[] rows = Rows(bytes, count); JsonObject[] fileRows = Rows(file, mode == "partial-fault" ? 2 : 0);
        foreach (JsonObject row in rows.Concat(fileRows)) { Require(row["component"]!.GetValue<string>() == "Provider.Behavior" && row["status"]!.GetValue<string>() == "info" && row["attributes"]!["event_id"]!.GetValue<int>() == 17); }
        if (mode is "full" or "partial-fault") { Require((mode == "full" ? rows : fileRows).Select(row => row["event_name"]!.GetValue<string>()).SequenceEqual(new[] { "first", "second" })); }
        if (mode == "template-tail")
        {
            Require(rows[0]["message_template"]!.GetValue<string>() == "Items {Items} Count {Count}" && rows[0]["message"]!.GetValue<string>() == "Items " + string.Join(", ", Enumerable.Repeat("0", 2000)) + " Count 7");
            Require(rows[1]["message_template"]!.GetValue<string>() == "bounded {Items}" && rows.All(row => row["truncated"]!.GetValue<bool>() && row["attributes"]!.AsObject().Count == 2 && row["attributes"]!["Items"]!.AsArray().Count == 1021));
        }
        if (mode == "float") { Require(rows[0]["attributes"]!["number"]!.GetValue<double>() == 0.10000000149011612); }
        if (mode == "duplicates") { Require(rows[0]["message_template"]!.GetValue<string>() == "last" && JsonNode.DeepEquals(rows[0]["attributes"], JsonNode.Parse("{\"event_id\":17,\"nested\":{\"same\":2}}"))); }
        if (mode == "root-shape") { Require(JsonNode.DeepEquals(rows[0]["attributes"], JsonNode.Parse("{\"event_id\":17}"))); }
        if (mode is "rejection" or "capture-failure" or "formatter-failure") { Require(rows[0]["event_name"]!.GetValue<string>() == "after-" + (mode == "rejection" ? "rejections" : mode == "capture-failure" ? "capture" : "formatter")); }
    }
    private static JsonObject[] Rows(byte[] bytes, int count)
    {
        Require(count == 0 ? bytes.Length == 0 : bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries); Require(lines.Length == count && lines.All(line => Encoding.UTF8.GetByteCount(line) + 1 <= 16384));
        return lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
    }
}
