using System.Text;
using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.ProviderReportChecks;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderHostFilterExpectations
{
    internal static readonly string[] Modes = ["broad", "provider-rule", "category-rule", "narrow-host", "narrow-category", "trace-file", "core-off"];
    internal static void Validate(string mode, JsonObject a, string consoleText, int? expectedPid = null)
    {
        Header(mode, Modes, a, expectedPid);
        bool off = mode == "core-off", narrow = mode is "narrow-host" or "narrow-category";
        int count = off ? 0 : narrow ? 3 : mode == "trace-file" ? 6 : 5;
        Require(Integer(a["formatterCalls"], count) && Integer(a["stateEnumerations"], count), "skipped callback work");
        Require(Same(a["coreDebug"], JsonValue.Create(!off)) && Same(a["hostDebug"], JsonValue.Create(!off && !narrow)), "effective host interest");
        Require(Same(a["fileExists"], JsonValue.Create(!off)) && Same(a["leaseReleased"], JsonValue.Create(!off)), "physical lifetime");
        Require((string?)a["consoleLevel"] == (off ? "None" : "Warning") &&
            (string?)a["fileLevel"] == (off ? "None" : mode == "trace-file" ? "Trace" : "Debug"), "core thresholds");
        Require((string?)a["invalid"] == "0", "invalid accounting");
        string[] fileNames = off ? [] : narrow ? ["warning", "error", "critical", "direct.debug"] :
            mode == "trace-file" ? ["trace", "debug", "information", "warning", "error", "critical"] : ["debug", "information", "warning", "error", "critical"];
        string[] consoleNames = off ? [] : ["warning", "error", "critical"];
        Check(Convert.FromBase64String(a["fileBytes"]!.GetValue<string>()), fileNames);
        Check(Encoding.UTF8.GetBytes(consoleText), consoleNames);
        JsonObject result = a["result"]!.AsObject(); Shape(result);
        Require((string?)result["completion"] == "Completed" && Same(result["succeeded"], "true") && Same(result["loss"], "false"), "actual stop result");
        string[] destinations = off ? [] : ["console", "file"];
        JsonArray stopped = result["outputs"]!.AsArray();
        Require(stopped.Select(output => (string?)output!["name"]).SequenceEqual(destinations), "output composition");
        foreach (JsonNode? output in stopped)
        {
            Delivery(output!, (string?)output!["name"] == "console" ? consoleNames.Length : fileNames.Length, "separate core delivery");
        }
        JsonArray live = a["outputs"]!.AsArray();
        Require(live.Select(output => (string?)output!["name"]).SequenceEqual(destinations), "live output composition");
        foreach (JsonNode? output in live)
        {
            string expected = ((string?)output!["name"] == "console" ? consoleNames.Length : fileNames.Length).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Require((string?)output["accepted"] == expected && (string?)output["written"] == expected &&
                new[] { "failed", "abandoned", "pending", "full", "unavailable" }.All(key => (string?)output[key] == "0") &&
                output["fault"] is null, "live delivery accounting");
        }
        void Check(byte[] data, string[] names)
        {
            JsonObject[] rows = Rows(data, names.Length, countReason: "destination severity records");
            Require(rows.Select(row => (string?)row["event_name"]).SequenceEqual(names), "destination severity records");
            foreach (JsonObject row in rows)
            {
                string name = row["event_name"]!.GetValue<string>(); bool direct = name == "direct.debug";
                string status = name switch { "information" => "info", "warning" => "warn", "direct.debug" => "debug", _ => name };
                Require(Integer(row["schema_version"], 1) && (string?)row["status"] == status, "wire level");
                Require((string?)row["message"] == (direct ? "direct" : "rendered") &&
                    (string?)row["component"] == (direct ? "Direct.Host.Filter" : "Provider.Host.Filters"), "mapped message and category");
                Identity(row, a, true, "automatic facts");
                Require(!row.ContainsKey("truncated") && !row.ContainsKey("operation_id"), "untruncated ordinary event");
                Require(Same(row["attributes"], direct ? "{}" : """{"event_id":17,"value":7}"""), "structured state");
            }
        }
    }
}
