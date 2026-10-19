using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderBehaviorExpectations
{
    internal static readonly string[] Modes = ["filtered", "no-outputs", "stopped", "disposed", "all-faulted", "partial-fault", "full", "template-tail", "float", "duplicates", "root-shape", "rejection", "capture-failure", "formatter-failure"];
    internal static bool IsPhysicalMode(string mode) => mode is not ("no-outputs" or "all-faulted" or "partial-fault" or "full");
    private static void Require(bool value, string reason)
    {
        if (!value) { throw new InvalidDataException(reason); }
    }
    private static bool Same(JsonNode? actual, string expected) => Same(actual, JsonNode.Parse(expected));
    private static bool Same(JsonNode? actual, JsonNode? expected)
    {
        if (actual is null || expected is null) { return actual is null && expected is null; }
        if (actual.GetValueKind() != expected.GetValueKind()) { return false; }
        if (expected is JsonObject obj)
        {
            return actual is JsonObject value && value.Count == obj.Count && obj.All(pair => value.ContainsKey(pair.Key) && Same(value[pair.Key], pair.Value));
        }
        if (expected is JsonArray array)
        {
            return actual is JsonArray value && value.Count == array.Count && array.Select((item, index) => Same(value[index], item)).All(match => match);
        }
        if (expected.GetValueKind() == JsonValueKind.Number)
        {
            string left = actual.ToJsonString(), right = expected.ToJsonString();
            bool integerLeft = left.IndexOfAny(['.', 'e', 'E']) < 0, integerRight = right.IndexOfAny(['.', 'e', 'E']) < 0;
            return integerLeft == integerRight && JsonNode.DeepEquals(actual, expected);
        }
        return JsonNode.DeepEquals(actual, expected);
    }
    private static bool Integer(JsonNode? node, int expected) => Same(node, expected.ToString(CultureInfo.InvariantCulture));
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string[] Names(JsonArray outputs) => outputs.Select(output => output!["name"]!.GetValue<string>()).ToArray();

    internal static void Validate(string mode, JsonObject report, int? expectedPid = null, bool physical = false)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && report["mode"]!.GetValue<string>() == mode, "mode");
        Require(report["runtime"]!.GetValue<string>() == ".NET 10.0.12", "actual runtime");
        bool validPid = report["pid"] is JsonValue pidValue && pidValue.TryGetValue<int>(out int pid) && pid > 0 && Integer(pidValue, pid) && (expectedPid is null || expectedPid == pid);
        Require(validPid, "actual child PID");
        Require(Same(report["physical"], JsonValue.Create(physical)) && Same(report["leaseReleased"], JsonValue.Create(physical)), "physical lifetime");
        int calls = mode switch { "filtered" or "no-outputs" or "stopped" or "disposed" => 0, "partial-fault" or "capture-failure" or "formatter-failure" => 2, "full" => 3, "rejection" => 19, _ => 1 };
        int states = mode switch { "all-faulted" or "capture-failure" or "formatter-failure" => 1, "partial-fault" => 2, "full" or "rejection" => 3, _ => 0 };
        Require(Integer(report["formatterCalls"], calls), "formatter calls");
        Require(Integer(report["stateCalls"], states), "state traversal");
        Require(report["enabledBefore"]!.GetValue<bool>() == (mode is not ("filtered" or "no-outputs")) && report["enabledAtProbe"]!.GetValue<bool>() == (mode is not ("filtered" or "no-outputs" or "stopped" or "disposed" or "all-faulted")), "interest gates");
        int invalid = mode == "rejection" ? 19 : mode == "capture-failure" || physical && mode == "formatter-failure" ? 1 : 0;
        Require(report["invalid"]!.GetValue<string>() == Number(invalid) && Integer(report["rejected"], invalid), "invalid accounting");
        Require(report["truncated"]!.GetValue<string>() == (mode == "template-tail" ? "2" : "0"), "truncation accounting");
        Require(report["exceptionIdentity"]!.GetValue<bool>() == (mode == "formatter-failure" && !physical) &&
            report["formatterFailureContained"]!.GetValue<bool>() == (mode == "formatter-failure" && physical), "formatter exception identity");
        string[] names = mode == "no-outputs" ? [] : mode == "partial-fault" ? ["console", "file"] : [physical ? "file" : "console"];
        JsonArray outputs = report["outputs"]!.AsArray();
        Require(Names(outputs).SequenceEqual(names), "output selection");
        foreach (JsonNode? node in outputs)
        {
            JsonObject output = node!.AsObject();
            bool failed = mode is "all-faulted" or "partial-fault" && output["name"]!.GetValue<string>() == "console";
            int accepted = failed ? 1 : mode is "filtered" or "stopped" or "disposed" ? 0 : mode is "partial-fault" or "full" or "template-tail" ? 2 : 1;
            string[] keys = ["accepted", "written", "failed", "abandoned", "pending", "full", "unavailable", "filtered"];
            int[] expected = [accepted, failed ? 0 : accepted, failed ? 1 : 0, 0, 0, mode == "full" ? 1 : 0, failed ? 1 : 0, mode == "filtered" ? 2 : 0];
            Require(keys.Select((key, index) => output[key]!.GetValue<string>() == Number(expected[index])).All(match => match), "output accounting");
            Require(failed ? output["fault"]?.GetValue<string>() == "WriteFailed" : output["fault"] is null, "safe fault code");
        }
        JsonObject result = report["result"]!.AsObject();
        Require(result.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["completion", "succeeded", "loss", "captured", "observed", "outputs"]), "result shape");
        Require(result["completion"]!.GetValue<string>() == "Completed" && result["succeeded"]!.GetValue<bool>() == (mode is not ("all-faulted" or "partial-fault")) && result["loss"]!.GetValue<bool>() == ((mode is "all-faulted" or "partial-fault" or "full" or "template-tail" or "rejection" or "capture-failure") || physical && mode == "formatter-failure"), "stop summary");
        JsonArray stopped = result["outputs"]!.AsArray();
        Require(Names(stopped).SequenceEqual(names), "stop outputs");
        for (int index = 0; index < outputs.Count; index++)
        {
            JsonNode live = outputs[index]!, stop = stopped[index]!;
            Require(stop["boundary"]!.GetValue<string>() == live["accepted"]!.GetValue<string>() && new[] { "written", "failed", "abandoned", "pending" }.All(key => stop[key]!.GetValue<string>() == live[key]!.GetValue<string>()), "stop boundary");
            Require(stop["flush"]!.GetValue<string>() == (live["fault"] is null ? "Succeeded" : "Failed"), "stop flush");
        }
        int count = mode is "filtered" or "no-outputs" or "stopped" or "disposed" or "all-faulted" or "partial-fault" ? 0 : mode is "full" or "template-tail" ? 2 : 1;
        JsonObject[] rows = Rows(Convert.FromBase64String(report["bytes"]!.GetValue<string>()), count);
        JsonObject[] fileRows = Rows(Convert.FromBase64String(report["fileBytes"]!.GetValue<string>()), mode == "partial-fault" ? 2 : 0);
        foreach (JsonObject row in rows.Concat(fileRows))
        {
            Require(Integer(row["schema_version"], 1) && row["component"]!.GetValue<string>() == "Provider.Behavior" && row["status"]!.GetValue<string>() == "info" && Integer(row["attributes"]!["event_id"], 17), "core wire mapping");
            Require(row["service"]!.GetValue<string>() == (physical ? "CIS.Core.DiscoveryFixture" : "Console.App"), "core identity");
            if (physical)
            {
                Require(Same(row["resource"]!["process_id"], report["pid"]) && row["resource"]!["runtime_description"]!.GetValue<string>() == ".NET 10.0.12" && Same(row["resource"]!["os_family"], report["os"]), "automatic process facts");
            }
            Require(mode == "template-tail" ? Same(row["truncated"], "true") : !row.ContainsKey("truncated"), "truncation marker");
        }
        if (mode is "full" or "partial-fault")
        {
            JsonObject[] accepted = mode == "full" ? rows : fileRows;
            Require(accepted.Select(row => row["event_name"]!.GetValue<string>()).SequenceEqual(["first", "second"]) && accepted.All(row => Same(row["attributes"], "{\"event_id\":17,\"value\":7}")), "accepted prefix");
        }
        if (mode == "template-tail")
        {
            Require(rows.Select(row => row["event_name"]!.GetValue<string>()).SequenceEqual(["framework", "skipped-tail"]) && rows[0]["message_template"]?.GetValue<string>() == "Items {Items} Count {Count}" && rows[1]["message_template"]?.GetValue<string>() == "bounded {Items}", "template after budget");
            Require(rows[0]["message"]!.GetValue<string>() == "Items " + string.Join(", ", Enumerable.Repeat("0", 2000)) + " Count 7" && rows[1]["message"]!.GetValue<string>() == "rendered", "supplied messages");
            string prefix = "{\"event_id\":17,\"Items\":[" + string.Join(",", Enumerable.Repeat("0", 1021)) + "]}";
            Require(rows.All(row => Same(row["attributes"], prefix)), "bounded retained prefix");
        }
        if (mode == "float") { Require(Same(rows[0]["attributes"], "{\"event_id\":17,\"number\":0.10000000149011612}"), "exact Single widening"); }
        if (mode == "duplicates") { Require(rows[0]["message_template"]!.GetValue<string>() == "last" && Same(rows[0]["attributes"], "{\"event_id\":17,\"nested\":{\"same\":2}}"), "last visited duplicates"); }
        if (mode == "root-shape") { Require(Same(rows[0]["attributes"], "{\"event_id\":17}"), "root shape policy"); }
        if (mode is "rejection" or "capture-failure" or "formatter-failure")
        {
            Require(rows[0]["event_name"]!.GetValue<string>() == "after-" + (mode == "rejection" ? "rejections" : mode == "capture-failure" ? "capture" : "formatter") && Same(rows[0]["attributes"], "{\"event_id\":17,\"value\":7}"), "no partial admission");
        }
    }
    private static JsonObject[] Rows(byte[] bytes, int count)
    {
        Require(count == 0 ? bytes.Length == 0 : bytes.Length > 0 && bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "actual framing");
        if (count == 0) { return []; }
        string[] lines = new UTF8Encoding(false, true).GetString(bytes)[..^1].Split('\n');
        Require(lines.Length == count && lines.All(line => line.Length > 0 && Encoding.UTF8.GetByteCount(line) + 1 <= 16384), "bounded record count");
        return lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
    }
}
