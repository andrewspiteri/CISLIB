using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderFoundationExpectations
{
    internal static readonly string[] Modes = ["forward", "extensions", "categories", "filtered", "nonowning", "stopped", "typed-scope", "formatter-error"];
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
    private static bool Integer(JsonNode? node, int expected) => node is JsonValue value && value.TryGetValue<int>(out int actual) && actual == expected && node.ToJsonString() == expected.ToString(CultureInfo.InvariantCulture);
    internal static void Validate(string mode, JsonObject a, int? expectedPid = null, bool physical = false)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode, "mode");
        Require(a["runtime"]!.GetValue<string>() == ".NET 10.0.12", "actual runtime");
        Require(a["pid"] is JsonValue pidValue && pidValue.TryGetValue<int>(out int pid) && pid > 0 && Integer(pidValue, pid) && (expectedPid is null || pid == expectedPid), "actual child PID");
        Require(a["physical"]!.GetValue<bool>() == physical && a["borrowedConsoleOpen"]!.GetValue<bool>() && a["leaseReleased"]!.GetValue<bool>() == physical, "physical lifetime");
        bool contained = physical && mode == "formatter-error";
        Require(a["invalid"]!.GetValue<string>() == (contained ? "1" : "0"), "core invalid count");
        Require(a["sharedRuntimeLiveAfterProviderDispose"]!.GetValue<bool>() == (mode == "nonowning") && a["formatterErrorPreserved"]!.GetValue<bool>() == (mode == "formatter-error" && !physical) && a["scopeCopied"]!.GetValue<bool>() == (mode == "typed-scope") &&
            a["formatterFailureContained"]!.GetValue<bool>() == contained, "provider ownership/error/scope facts");
        Require(a["formatterExceptionReceived"]!.GetValue<bool>() == (mode == "forward"), "formatter exception identity");
        int count = mode is "filtered" or "stopped" or "formatter-error" ? 0 : mode is "categories" or "nonowning" ? 2 : 1;
        Require(Integer(a["formatterCalls"], mode is "forward" or "nonowning" or "typed-scope" or "formatter-error" ? 1 : mode == "categories" ? 2 : 0), "deferred formatter calls");
        JsonObject result = a["result"]!.AsObject();
        Require(result.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["completion", "succeeded", "loss", "captured", "observed", "outputs"]), "result shape");
        Require(result["completion"]!.GetValue<string>() == "Completed" && result["succeeded"]!.GetValue<bool>() && result["loss"]!.GetValue<bool>() == contained, "stop summary");
        JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 1 && outputs[0]!["name"]!.GetValue<string>() == (physical ? "file" : "console"), "output selection"); JsonNode output = outputs[0]!;
        Require(output["boundary"]!.GetValue<string>() == count.ToString(CultureInfo.InvariantCulture) && output["written"]!.GetValue<string>() == count.ToString(CultureInfo.InvariantCulture) && output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" && output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded", "core delivery");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>());
        Require(count == 0 ? bytes.Length == 0 : bytes.Length > 0 && bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "actual framing");
        if (count == 0) { return; }
        string[] lines = new UTF8Encoding(false, true).GetString(bytes)[..^1].Split('\n');
        Require(lines.Length == count && lines.All(line => line.Length > 0 && Encoding.UTF8.GetByteCount(line) + 1 <= 16384), "actual bounded records");
        JsonObject[] rows = lines.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        string[] names = mode switch { "categories" => ["first", "second"], "nonowning" => ["direct", "other"], "extensions" => ["log.message"], "typed-scope" => ["scoped"], _ => ["forward"] };
        string[] components = mode switch { "categories" => ["Orders.First", "Orders.Second"], "nonowning" => ["Direct", "Other.Provider"], _ => ["Provider.Foundation"] };
        Require(rows.Select(x => x["event_name"]!.GetValue<string>()).SequenceEqual(names), "event names");
        Require(rows.Select(x => x["component"]!.GetValue<string>()).SequenceEqual(components), "category components");
        foreach (JsonObject row in rows)
        {
            Require(Integer(row["schema_version"], 1) && row["status"]!.GetValue<string>() == (mode == "forward" ? "error" : "info") && row["service"]!.GetValue<string>() == (physical ? "CIS.Core.DiscoveryFixture" : "Console.App"), "automatic core facts");
            if (physical)
            { Require(Integer(row["resource"]!["process_id"], a["pid"]!.GetValue<int>()) && row["resource"]!["runtime_description"]!.GetValue<string>() == ".NET 10.0.12" && row["resource"]!["os_family"]!.GetValue<string>() == a["os"]!.GetValue<string>(), "automatic core facts"); }
        }
        if (mode == "extensions") { Require(rows[0]["message"]!.GetValue<string>() == "Orders 3", "framework formatter"); }
        if (mode == "forward")
        {
            Require(rows[0]["message"]!.GetValue<string>() == "Ω 🙂", "rendered message");
            JsonObject error = rows[0]["exception"]!.AsObject();
            Require(error["type"]!.GetValue<string>() == "System.InvalidOperationException", "exception type");
            Require(error["message"]!.GetValue<string>() == "controlled provider failure", "exception message");
            Require(error.All(x => x.Key is "type" or "message" or "stack_trace" or "inner_exceptions" or "truncated") && !rows[0].ToJsonString().Contains("private_data", StringComparison.Ordinal) && !rows[0].ToJsonString().Contains("must not be projected", StringComparison.Ordinal), "exception excludes Data");
        }
        if (mode == "typed-scope") { Require(rows[0]["operation_id"]!.GetValue<string>() == "operation.original" && rows[0]["attributes"]!["scope_attribute"]!.GetValue<string>() == "original", "copied typed scope"); }
    }
}
