using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConsoleStartupExpectations
{
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    internal static void Validate(JsonObject row, JsonObject actual, bool physicalBytes = false)
    {
        JsonObject expected = row["expected"]!.AsObject(); string mode = row["mode"]!.GetValue<string>(); bool physical = row["physical"]!.GetValue<bool>();
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Startup case identity differs.");
        foreach (string key in new[] { "selectionCalls", "launchCalls", "workers", "canWriteCalls", "disposes", "canRead", "joinedAtReturn", "flowSuppressed", "beforeChecks", "queues", "successOutcome" })
        { Require(JsonNode.DeepEquals(expected[key], actual[key]), "Startup " + key + " differs."); }
        Require(actual["asyncDisposes"]!.GetValue<int>() == 0, "Borrowed async disposal occurred.");
        Require(JsonNode.DeepEquals(expected["existing"], actual["existingRunning"]), "Existing runtime was stopped by another startup attempt.");
        if (expected["code"] is null) { Require(actual["error"] is null, "Disabled/unselected console startup failed."); }
        else
        {
            string output = expected["output"]!.GetValue<string>(), code = expected["code"]!.GetValue<string>();
            var wanted = new JsonObject
            {
                ["type"] = "CIS.Logging.CoreLogInitializationException",
                ["output"] = output,
                ["code"] = code,
                ["message"] = "Logging output '" + output + "' could not initialize (" + code + ").",
                ["inner"] = false,
                ["dataCount"] = 0
            };
            Require(JsonNode.DeepEquals(wanted, actual["error"]), "Safe initialization error differs.");
        }
        byte[] before = Convert.FromBase64String(actual["beforeApplication"]!.GetValue<string>()), selected = Convert.FromBase64String(actual["selectedBytes"]!.GetValue<string>());
        if (mode == "existing") { CheckRecord(before, "startup.survived", "old-runtime-still-usable", "StartupExisting"); }
        else { Require(before.AsSpan().SequenceEqual(mode == "read-only" ? Encoding.UTF8.GetBytes("existing-console-data") : []), "Startup emitted data or modified retained stream bytes."); }
        byte[] marker = Encoding.UTF8.GetBytes("{\"application\":\"selected-startup-preserved\"}\n");
        byte[] wantedSelected = mode is "read-only" or "closed" || physical ? before : before.Concat(marker).ToArray();
        Require(selected.AsSpan().SequenceEqual(wantedSelected), "Borrowed stream changed or application cannot reuse it.");
        byte[] file = Convert.FromBase64String(actual["fileBytes"]!.GetValue<string>());
        if (expected["fileRecords"]!.GetValue<int>() == 1) { CheckRecord(file, "startup.file", "file-only-remains-independent", "Startup"); }
        else { Require(file.Length == 0, "Failed startup attempted ordinary file I/O."); }
        if (!physical) { Require(actual["stdout"]!.GetValue<string>() == "" && actual["stderr"]!.GetValue<string>() == "", "Controlled startup emitted standard-stream data."); }
        else if (physicalBytes)
        {
            foreach (string stream in new[] { "stdout", "stderr" })
            { Require(Convert.FromBase64String(actual[stream]!.GetValue<string>()).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes("{\"application\":\"" + stream + "-startup-preserved\"}\n")), "Physical application streams were changed."); }
        }
    }
    private static void CheckRecord(byte[] bytes, string eventName, string message, string component)
    {
        string text = new UTF8Encoding(false, true).GetString(bytes);
        Require(bytes.Length <= 65536 && text.EndsWith('\n') && text.Count(c => c == '\n') == 1 && !text.StartsWith('\uFEFF'), "Startup survivor record framing differs.");
        JsonObject value = JsonNode.Parse(bytes)!.AsObject();
        foreach ((string key, string wanted) in new[] { ("event_name", eventName), ("message", message), ("component", component), ("service", "Console.App"), ("env", "test"), ("version", "1.2.3"), ("status", "info"), ("timestamp", "2026-10-13T10:11:12.0000000Z") })
        { Require(value[key]!.GetValue<string>() == wanted, "Startup survivor identity differs."); }
        Require(value["schema_version"]!.GetValue<int>() == 1 && value["resource"]!["instance_id"]!.GetValue<string>() == "11111111-1111-1111-1111-111111111111", "Startup survivor protected identity differs.");
    }
    internal static void Damage(JsonObject actual, string fault)
    {
        switch (fault)
        {
            case "joined": actual["joinedAtReturn"]![0] = false; break;
            case "worker": actual["workers"] = 99; break;
            case "queue": actual["queues"]![0]!["pending"] = 1; break;
            case "disposed": actual["disposes"] = 1; break;
            case "flow": actual["flowSuppressed"]![0] = false; break;
            case "inner": actual["error"]!["inner"] = true; break;
            case "message": actual["error"]!["message"] = "private-startup-path-and-payload"; break;
            case "output": actual["error"]!["output"] = "file"; break;
            case "bytes": actual["beforeApplication"] = Convert.ToBase64String([10]); break;
            case "file": actual["fileBytes"] = Convert.ToBase64String([10]); break;
            default: throw new ArgumentException("Unknown startup damage.", nameof(fault));
        }
    }
}
