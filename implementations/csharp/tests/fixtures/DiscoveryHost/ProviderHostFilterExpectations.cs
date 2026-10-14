using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderHostFilterExpectations
{
    internal static readonly string[] Modes = ["broad", "provider-rule", "category-rule", "narrow-host", "narrow-category", "trace-file", "core-off"];

    internal static void Validate(string mode, JsonObject report, string consoleText)
    {
        void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
        bool off = mode == "core-off", narrow = mode is "narrow-host" or "narrow-category";
        int count = off ? 0 : narrow ? 3 : mode == "trace-file" ? 6 : 5;
        Require((string?)report["consoleLevel"] == (off ? "None" : "Warning") && (string?)report["fileLevel"] == (off ? "None" : mode == "trace-file" ? "Trace" : "Debug"), "Core thresholds.");
        Require((string?)report["mode"] == mode && (string?)report["runtime"] == ".NET 10.0.12", "Actual case and runtime.");
        Require(report["formatterCalls"]!.GetValue<int>() == count && report["stateEnumerations"]!.GetValue<int>() == count, "Only interested events invoke formatter and copy state.");
        Require(report["coreDebug"]!.GetValue<bool>() == !off && report["hostDebug"]!.GetValue<bool>() == (!off && !narrow), "Host can suppress an interested Core event.");
        Require(report["fileExists"]!.GetValue<bool>() == !off && report["leaseReleased"]!.GetValue<bool>() == !off && (string?)report["invalid"] == "0", "Physical file lifetime and valid events.");
        string[] fileNames = off ? [] : narrow ? ["warning", "error", "critical", "direct.debug"] : mode == "trace-file" ? ["trace", "debug", "information", "warning", "error", "critical"] : ["debug", "information", "warning", "error", "critical"];
        string[] consoleNames = off ? [] : ["warning", "error", "critical"];
        byte[] file = Convert.FromBase64String(report["fileBytes"]!.GetValue<string>());
        Check(Encoding.UTF8.GetString(file), fileNames);
        Check(consoleText, consoleNames);
        JsonObject result = report["result"]!.AsObject();
        Require(result["succeeded"]!.GetValue<bool>() && !result["loss"]!.GetValue<bool>() && (string?)result["completion"] == "Completed", "Successful stop cannot detect host suppression.");
        JsonArray outputs = result["outputs"]!.AsArray();
        Require(outputs.Count == (off ? 0 : 2), "Configured destinations.");
        foreach (JsonNode? node in outputs)
        {
            JsonObject output = node!.AsObject();
            string expected = ((string?)output["name"] == "console" ? consoleNames.Length : fileNames.Length).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Require((string?)output["boundary"] == expected && (string?)output["written"] == expected && (string?)output["failed"] == "0" && (string?)output["pending"] == "0" && (string?)output["abandoned"] == "0" && (string?)output["flush"] == "Succeeded", "Independent destination delivery.");
        }
        JsonArray live = report["outputs"]!.AsArray();
        Require(live.Select(node => (string?)node!["name"]).SequenceEqual(off ? [] : new[] { "console", "file" }), "Live destination composition.");
        foreach (JsonNode? node in live)
        {
            JsonObject output = node!.AsObject();
            string expected = ((string?)output["name"] == "console" ? consoleNames.Length : fileNames.Length).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Require((string?)output["accepted"] == expected && (string?)output["written"] == expected && (string?)output["failed"] == "0" && (string?)output["abandoned"] == "0" && (string?)output["pending"] == "0" && (string?)output["full"] == "0" && (string?)output["unavailable"] == "0" && output["fault"] is null, "Live delivery accounting.");
        }
        void Check(string text, string[] names)
        {
            Require(text.Length == 0 || text.EndsWith('\n') && !text.StartsWith('\uFEFF'), "JSONL framing.");
            JsonObject[] rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
            Require(rows.Select(row => row["event_name"]!.GetValue<string>()).SequenceEqual(names), "Output severity selection.");
            foreach (JsonObject row in rows)
            {
                string name = row["event_name"]!.GetValue<string>();
                Require((string?)row["status"] == (name == "information" ? "info" : name == "warning" ? "warn" : name == "direct.debug" ? "debug" : name), "Core level mapping.");
                Require((string?)row["message"] == (name == "direct.debug" ? "direct" : "rendered"), "Actual formatter message.");
                Require(row["resource"]!["process_id"]!.GetValue<int>() == report["pid"]!.GetValue<int>() && row["schema_version"]!.GetValue<int>() == 1, "Automatic wire identity.");
                Require((string?)row["component"] == (name == "direct.debug" ? "Direct.Host.Filter" : "Provider.Host.Filters"), "Factory category mapping.");
                if (name != "direct.debug") { Require(row["attributes"]!.AsObject().Count == 2 && row["attributes"]!["event_id"]!.GetValue<int>() == 17 && row["attributes"]!["value"]!.GetValue<int>() == 7, "Structured provider state."); }
            }
        }
    }
}
