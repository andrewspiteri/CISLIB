using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Shared observations for the native provider readers, never a shipping dependency.
internal static class ProviderReportChecks
{
    internal static void Require(bool value, string reason)
    {
        if (!value) { throw new InvalidDataException(reason); }
    }
    internal static bool Integer(JsonNode? value, long expected) =>
        value is JsonValue scalar && (scalar.TryGetValue<long>(out long actual) && actual == expected || scalar.TryGetValue<int>(out int small) && small == expected) &&
        value.ToJsonString() == expected.ToString(CultureInfo.InvariantCulture);
    internal static bool Same(JsonNode? actual, JsonNode? expected)
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
            return (actual.ToJsonString().IndexOfAny(['.', 'e', 'E']) < 0) ==
                (expected.ToJsonString().IndexOfAny(['.', 'e', 'E']) < 0) && JsonNode.DeepEquals(actual, expected);
        }
        return JsonNode.DeepEquals(actual, expected);
    }
    internal static bool Same(JsonNode? actual, string expected) => Same(actual, JsonNode.Parse(expected));
    internal static void Header(string mode, string[] modes, JsonObject report, int? expectedPid)
    {
        Require(modes.Contains(mode, StringComparer.Ordinal) && (string?)report["mode"] == mode, "mode");
        Require((string?)report["runtime"] == ".NET 10.0.12", "actual runtime");
        Require(report["pid"] is JsonValue scalar && scalar.TryGetValue<int>(out int pid) && pid > 0 &&
            Integer(scalar, pid) && (expectedPid is null || pid == expectedPid), "actual child PID");
    }
    internal static JsonObject[] Rows(byte[] data, int count, string framingReason = "JSONL framing", string countReason = "bounded records")
    {
        Require(count == 0 ? data.Length == 0 : data.Length > 0 && data[^1] == 10 &&
            !data.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) && !data.Contains((byte)13), framingReason);
        string[] lines = count == 0 ? [] : new UTF8Encoding(false, true).GetString(data)[..^1].Split('\n');
        Require(lines.All(line => line.Length > 0 && Encoding.UTF8.GetByteCount(line) + 1 <= 16384), "bounded records");
        Require(lines.Length == count, countReason);
        return lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
    }
    internal static JsonObject[] Rows(JsonObject report, int count) =>
        Rows(Convert.FromBase64String(report["bytes"]!.GetValue<string>()), count);
    internal static void Identity(JsonObject row, JsonObject report, bool physical, string reason = "automatic core facts")
    {
        Require(Integer(row["schema_version"], 1) &&
            (string?)row["service"] == (physical ? "CIS.Core.DiscoveryFixture" : "Console.App"), reason);
        if (physical)
        {
            Require(Same(row["resource"]?["process_id"], report["pid"]) && Integer(row["resource"]?["process_id"], report["pid"]!.GetValue<int>()) &&
                (string?)row["resource"]?["runtime_description"] == ".NET 10.0.12" &&
                Same(row["resource"]?["os_family"], report["os"]), reason);
        }
    }
    internal static void Shape(JsonObject result)
    {
        Require(result.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["completion", "succeeded", "loss", "captured", "observed", "outputs"]), "result shape");
    }
    internal static void Delivery(JsonNode output, int count, string reason)
    {
        string n = count.ToString(CultureInfo.InvariantCulture);
        Require((string?)output["boundary"] == n && (string?)output["written"] == n &&
            (string?)output["failed"] == "0" && (string?)output["abandoned"] == "0" &&
            (string?)output["pending"] == "0" && (string?)output["flush"] == "Succeeded", reason);
    }
    internal static void Stop(JsonObject report, int count, bool physical, bool loss,
        string summaryReason = "stop summary", string deliveryReason = "core delivery")
    {
        JsonObject result = report["result"]!.AsObject(); Shape(result);
        Require((string?)result["completion"] == "Completed" && Same(result["succeeded"], "true") &&
            Same(result["loss"], JsonValue.Create(loss)), summaryReason);
        JsonArray outputs = result["outputs"]!.AsArray();
        Require(outputs.Count == 1 && (string?)outputs[0]!["name"] == (physical ? "file" : "console"), "output selection");
        Delivery(outputs[0]!, count, deliveryReason);
    }
    internal static void Activity(JsonObject report, string trace = "trace", string span = "span")
    {
        string? value = (string?)report[span];
        Require((string?)report[trace] == new string('1', 32) && value is { Length: 16 } &&
            value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') && value.Any(c => c != '0'), "actual Activity");
    }
    internal static JsonObject Fields(int count, bool eventId = true, string? common = null)
    {
        var fields = new JsonObject();
        if (eventId) { fields["event_id"] = 17; }
        if (common is not null) { fields["common"] = common; }
        for (int i = 0; i < count; i++) { fields["k" + i.ToString("D4", CultureInfo.InvariantCulture)] = i; }
        return fields;
    }
}
