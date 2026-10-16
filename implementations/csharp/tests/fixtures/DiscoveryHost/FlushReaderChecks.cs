using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushReaderChecks
{
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    private static long Count(JsonNode? value)
    {
        Require(value is JsonValue token && token.TryGetValue<string>(out _), "counter encoding");
        string text = (string)value!;
        Require(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long count) && count >= 0 && count.ToString(CultureInfo.InvariantCulture) == text, "counter encoding");
        return count;
    }

    private static void Snapshot(JsonNode capture)
    {
        Require(capture.AsObject().Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["capturedAtUtc", "invalid", "truncated", "console", "file"]), "capture shape");
        Require(DateTimeOffset.TryParseExact((string?)capture["capturedAtUtc"], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset stamp) && stamp.Offset == TimeSpan.Zero, "UTC capture");
        _ = Count(capture["invalid"]); _ = Count(capture["truncated"]);
        foreach (string name in new[] { "console", "file" })
        {
            if (capture[name] is not JsonObject output) { continue; }
            Require(output.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["boundary", "filtered", "queueFull", "unavailable"]), "output shape");
            foreach (var pair in output) { _ = Count(pair.Value); }
        }
    }

    private static JsonObject Target(long boundary, long filtered = 0, long full = 0) => new()
    {
        ["boundary"] = boundary.ToString(CultureInfo.InvariantCulture),
        ["filtered"] = filtered.ToString(CultureInfo.InvariantCulture),
        ["queueFull"] = full.ToString(CultureInfo.InvariantCulture),
        ["unavailable"] = "0"
    };

    private static JsonObject[] Records(JsonNode encoded, int pid, bool boundary)
    {
        byte[] bytes = Convert.FromBase64String(encoded.GetValue<string>());
        Require(bytes.Length == 0 || bytes[^1] == 10, "complete record boundary");
        Require(!bytes.Contains((byte)13), "whole bounded record");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        return (bytes.Length == 0 ? [] : text[..^1].Split('\n')).Select(line =>
        {
            Require(line.Length > 0 && !line.StartsWith('\uFEFF') && Encoding.UTF8.GetByteCount(line) + 1 <= (boundary ? 16384 : 65536), "whole bounded record");
            JsonObject record = JsonNode.Parse(line)!.AsObject();
            Require(record["schema_version"]!.GetValue<int>() == 1 && (string?)record["timestamp"] == (boundary ? "2026-10-13T15:16:17.0000000Z" : "2026-10-13T17:18:19.0000000Z")
                && (string?)record["service"] == "Console.App" && (string?)record["env"] == "test" && (string?)record["version"] == "1.2.3"
                && (string?)record["component"] == (boundary ? "FlushBoundary" : "FlushCoordination"), "wire identity");
            Require((string?)record["resource"]!["instance_id"] == "11111111-1111-1111-1111-111111111111"
                && JsonNode.DeepEquals(record["attributes"], new JsonObject { ["producer_pid"] = pid }), "wire producer/resource");
            if (boundary && record["truncated"] is not null)
            {
                Require(record["truncated"]!.GetValue<bool>() && (string?)record["event_name"] is "flush.2" or "flush.4" && (string?)record["message"] == new string('x', 8192), "actual truncation");
            }
            else { Require((string?)record["message"] == "\u03a9 \U0001F642" && !record.ContainsKey("truncated"), "wire message"); }
            return record;
        }).ToArray();
    }

    private static void Terminal(JsonNode output, string name, long accepted, long filtered, long full, bool fault, long unavailable = 0)
    {
        var expected = new JsonObject
        {
            ["name"] = name,
            ["state"] = "Stopped",
            ["accepted"] = accepted.ToString(CultureInfo.InvariantCulture),
            ["written"] = (fault ? 0 : accepted).ToString(CultureInfo.InvariantCulture),
            ["failed"] = fault ? "1" : "0",
            ["abandoned"] = fault ? "1" : "0",
            ["pending"] = "0",
            ["unavailable"] = unavailable.ToString(CultureInfo.InvariantCulture),
            ["queueFull"] = full.ToString(CultureInfo.InvariantCulture),
            ["filtered"] = filtered.ToString(CultureInfo.InvariantCulture),
            ["fault"] = fault ? "WriteFailed" : null
        };
        Require(JsonNode.DeepEquals(expected, output), "terminal output facts");
    }
    internal static void Boundary(string mode, JsonObject actual, int? processId)
    {
        int pid = actual["pid"]!.GetValue<int>();
        Require(pid > 0 && (!processId.HasValue || pid == processId) && (string?)actual["runtime"] == ".NET 10.0.12", "actual child PID/runtime");
        string[] names = mode == "physical" ? ["file"] : mode is "both" or "saturated" or "concurrent" ? ["console", "file"] : ["console"];
        JsonArray captures = actual["captures"]!.AsArray();
        foreach (JsonNode? capture in captures)
        {
            Snapshot(capture!);
            foreach (string name in new[] { "console", "file" }) { Require((capture![name] is not null) == names.Contains(name, StringComparer.Ordinal), "disabled output"); }
        }
        JsonNode first = captures[0]!;
        Require(Count(first["invalid"]) == (mode is "both" or "saturated" ? 1 : 0) && Count(first["truncated"]) == (mode is "both" or "saturated" ? 1 : 0), "initial global facts");
        if (mode == "saturated") { Require(Count(captures[1]!["truncated"]) == 1 && JsonNode.DeepEquals(actual["heldControls"], new JsonObject { ["console"] = 1, ["file"] = 1 }), "bounded controls"); }
        if (mode == "concurrent")
        {
            Require(JsonNode.DeepEquals(first["console"], Target(1)), "seed target");
            foreach (JsonNode? capture in captures) { Require(JsonNode.DeepEquals(capture!["console"], Target(Count(capture["console"]!["boundary"]))) && JsonNode.DeepEquals(capture["console"], capture["file"]), "atomic output targets"); }
        }
        if (mode == "faulted") { Require(Count(captures[1]!["invalid"]) == 0 && Count(captures[1]!["truncated"]) == 0, "fault initial facts"); }
        JsonNode final = actual["final"]!;
        Require((string?)final["state"] == "Stopped", "terminal runtime");
        Require(Count(final["invalid"]) == (mode switch { "both" => 2, "saturated" => 3, "concurrent" => 16, _ => 0 })
            && Count(final["truncated"]) == (mode switch { "both" => 2, "saturated" => 1, _ => 0 }), "final global facts");
        JsonArray outputs = final["outputs"]!.AsArray();
        Require(outputs.Select(output => (string)output!["name"]!).SequenceEqual(names), "enabled outputs");
        foreach (JsonNode? output in outputs)
        {
            string name = (string)output!["name"]!;
            int accepted = mode switch { "empty" => 0, "both" => name == "console" ? 5 : 3, "saturated" => 3, "faulted" => 2, "closed" => 1, "concurrent" => 65, _ => 2 };
            bool fault = mode == "faulted"; int written = fault ? 0 : accepted;
            Terminal(output, name, accepted, mode == "both" ? name == "console" ? 2 : 4 : mode == "saturated" ? 1 : 0, mode == "saturated" ? 2 : 0, fault, fault ? 1 : 0);
            JsonObject[] records;
            if (mode == "physical") { records = Records(actual["bytes"]!, pid, boundary: true); }
            else
            {
                JsonNode writer = actual["writers"]![name]!;
                Require(writer["maximum"]!.GetValue<int>() == 1 && writer["attempts"]!.GetValue<int>() == (fault ? 1 : written), "actual write attempts");
                records = Records(writer["bytes"]!, pid, boundary: true);
            }
            Require(records.Length == written, "whole write bytes");
            if (mode == "concurrent")
            {
                string[] expected = new[] { "flush.0" }.Concat(from producer in Enumerable.Range(0, 16) from index in Enumerable.Range(0, 4) select $"flush.producer.{producer}.{index}").ToArray();
                Require(records.Select(record => (string)record["event_name"]!).ToHashSet(StringComparer.Ordinal).SetEquals(expected) && records.All(record => (string?)record["status"] == "warn"), "actual producer cohort");
            }
            else
            {
                string[] expected = mode switch
                {
                    "empty" or "faulted" => [],
                    "closed" => ["flush.0"],
                    "physical" => ["flush.0", "flush.1"],
                    "saturated" => ["flush.0", "flush.1", "flush.3"],
                    _ => name == "console" ? ["flush.0", "flush.1", "flush.2", "flush.3", "flush.4"] : ["flush.1", "flush.3", "flush.4"]
                };
                Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(expected), "actual record selection");
                Require(records.All(record => (string?)record["status"] == (mode == "both" && (string?)record["event_name"] is "flush.1" or "flush.3" or "flush.4" ? "error" : mode == "saturated" ? "warn" : "info")), "wire severity");
            }
        }
        if (mode == "physical") { return; }
        Require((actual["writers"]!["file"] is not null) == names.Contains("file", StringComparer.Ordinal), "disabled writer");
        string[] results = mode switch
        {
            "empty" => [],
            "closed" => ["Accepted"],
            "faulted" => ["Accepted", "Accepted", "Rejected"],
            "both" => ["Accepted", "Accepted", "Filtered", "InvalidEvent", "Accepted", "Accepted", "InvalidEvent", "Filtered", "Accepted"],
            "saturated" => ["Accepted", "Accepted", "Rejected", "InvalidEvent", "Rejected", "Filtered", "InvalidEvent", "Accepted", "InvalidEvent"],
            _ => ["Accepted"]
        };
        Require(actual["writeResults"]!.AsArray().Select(value => (string)value!).SequenceEqual(results), "admission outcomes");
    }

    internal static void Coordination(string mode, JsonObject actual, int? processId)
    {
        int pid = actual["pid"]!.GetValue<int>();
        Require(pid > 0 && (!processId.HasValue || pid == processId), "actual child PID");
        bool repeated = mode is "write-cancel" or "write-deadline" or "flush-cancel" or "flush-deadline", both = mode is "cohort" or "dual";
        Require(actual["capacity"]!.GetValue<int>() == (mode is "cohort" or "dual" or "fault" ? 2 : 3), "owned memory streams");
        JsonArray race = actual["race"]!.AsArray();
        if (mode == "complete-race")
        {
            Require(race.Count(value => (string?)value == "cancelled") == actual["cancelled"]!.GetValue<int>() && race.Count(value => (string?)value == "succeeded") == actual["succeeded"]!.GetValue<int>(), "truthful race totals");
        }
        else { Require(race.Count == 0, "no invented race"); }
        int[][] expectedPhases = mode switch
        {
            "cohort" => [[3, 3, 64, 64, 0], [3, 3, 16, 64, 0]],
            "dual" => [[2, 1, 1, 1, 0], [0, 1, 0, 0, 1], [0, 1, 0, 0, 0]],
            "pre-cancel" => [[1, 0, 0, 0, 0]],
            _ => repeated ? [[4, 0, 0, 4, 0]] : []
        };
        JsonArray phases = actual["phases"]!.AsArray(); Require(phases.Count == expectedPhases.Length, "live caller transition phases");
        string[] fields = ["consoleControls", "fileControls", "consoleAttached", "consoleSlots", "fileAttached"];
        for (int index = 0; index < phases.Count; index++)
        {
            Require(phases[index]!.AsObject().Count == fields.Length && fields.Select((key, field) => phases[index]![key]!.GetValue<int>() == expectedPhases[index][field]).All(value => value), "live caller transition phases");
        }
        JsonArray snapshots = actual["snapshots"]!.AsArray();
        Require(snapshots.Count == (repeated ? 6 : both ? 3 : mode == "fault" ? 2 : 1), "owned snapshot count");
        int accepted = repeated ? mode.StartsWith("write", StringComparison.Ordinal) ? 4 : 3 : mode == "cohort" ? 3 : mode is "dual" or "fault" ? 2 : 1;
        for (int index = 0; index < snapshots.Count; index++)
        {
            JsonNode snapshot = snapshots[index]!; Snapshot(snapshot); Require(Count(snapshot["truncated"]) == 0, "no invented truncation");
            JsonObject console; JsonObject? file = null; long invalid = 0;
            if (repeated)
            {
                console = index < 4 ? Target((mode.StartsWith("flush", StringComparison.Ordinal) ? 0 : 1) + index) : Target(accepted, full: index == 4 ? 1 : 4);
                invalid = index < 4 ? 0 : 1;
            }
            else if (mode == "cohort") { console = Target(index + 1); file = Target(index + 1); }
            else if (mode == "dual")
            {
                console = Target(index == 0 ? 1 : 2, filtered: index == 2 ? 1 : 0);
                file = Target(index == 0 ? 0 : 1, filtered: index == 2 ? 2 : 1); invalid = index == 2 ? 1 : 0;
            }
            else { console = Target(index + 1); }
            Require(JsonNode.DeepEquals(snapshot["console"], console) && JsonNode.DeepEquals(snapshot["file"], file) && Count(snapshot["invalid"]) == invalid, "original caller rejection facts");
        }
        Require(Count(actual["finalInvalid"]) == (repeated || mode == "dual" ? 1 : 0) && Count(actual["finalTruncated"]) == 0, "final global counters");
        string[] names = both ? ["console", "file"] : ["console"];
        JsonArray outputs = actual["outputs"]!.AsArray();
        Require(outputs.Select(output => (string)output!["name"]!).SequenceEqual(names), "enabled destinations");
        foreach (JsonNode? output in outputs)
        {
            string name = (string)output!["name"]!; bool fault = mode == "fault";
            int total = mode == "dual" && name == "file" ? 1 : accepted;
            Terminal(output, name, total, mode == "dual" ? name == "console" ? 1 : 2 : 0, repeated ? 4 : 0, fault);
            JsonNode writer = actual["writers"]![name]!;
            string[] order = writer["order"]!.AsArray().Select(value => (string)value!).ToArray();
            Require(writer["attempts"]!.GetValue<int>() == (fault ? 1 : total) && writer["flushes"]!.GetValue<int>() == order.Count(operation => operation.StartsWith("flush.", StringComparison.Ordinal)), "actual writer call count");
            JsonObject[] records = Records(writer["bytes"]!, pid, boundary: false);
            string[] expectedNames = fault ? [] : mode == "dual" && name == "file" ? ["coord.1"] : Enumerable.Range(0, total).Select(index => "coord." + index).ToArray();
            Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(expectedNames), "original complete records");
            Require(records.All(record => (string?)record["status"] == (mode == "dual" && (string?)record["event_name"] == "coord.1" ? "error" : "info")), "literal wire severity");
        }
        Require((actual["writers"]!["file"] is not null) == both, "disabled writer");
    }
}
