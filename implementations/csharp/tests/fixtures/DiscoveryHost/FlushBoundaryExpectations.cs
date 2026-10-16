using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushBoundaryExpectations
{
    internal static void Validate(string mode, JsonObject actual, int? processId = null)
    {
        FlushReaderChecks.Boundary(mode, actual, processId);
        void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
        Require(actual["mode"]!.GetValue<string>() == mode, "mode");
        Require(JsonNode.DeepEquals(actual["captures"], actual["after"]), "snapshot serialization");
        Require(actual["finalControls"]!["console"]!.GetValue<int>() == 0 && actual["finalControls"]!["file"]!.GetValue<int>() == 0, "released coordination");
        JsonArray captures = actual["captures"]!.AsArray(), results = actual["flushResults"]!.AsArray();
        Require(captures.Count == results.Count && captures.Count == (mode == "concurrent" ? 34 : mode is "saturated" or "faulted" ? 2 : 1), "capture count");
        for (int index = 0; index < captures.Count; index++)
        {
            JsonNode capture = captures[index]!;
            Require(DateTimeOffset.ParseExact(capture["capturedAtUtc"]!.GetValue<string>(), "O", CultureInfo.InvariantCulture).Offset == TimeSpan.Zero, "UTC capture");
            foreach (string name in new[] { "console", "file" })
            {
                if (capture[name] is null) { Require(results[index]![name] is null, "disabled output"); continue; }
                Require(results[index]![name]!.GetValue<bool>() == (mode is not ("faulted" or "closed")), "real writer flush");
            }
        }
        long Value(JsonNode snapshot, string field) => long.Parse(snapshot[field]!.GetValue<string>(), CultureInfo.InvariantCulture);
        void Output(JsonNode? snapshot, long boundary, long filtered = 0, long full = 0, long unavailable = 0)
        { Require(snapshot is not null && Value(snapshot, "boundary") == boundary && Value(snapshot, "filtered") == filtered && Value(snapshot, "queueFull") == full && Value(snapshot, "unavailable") == unavailable, "initial output facts"); }
        JsonNode first = captures[0]!;
        if (mode == "physical") { Require(first["console"] is null && actual["leaseReleased"]!.GetValue<bool>(), "physical ownership"); Output(first["file"], 2); return; }
        Require(actual["physicalFiles"]!.GetValue<int>() == 0, "controlled streams only");
        if (mode == "empty") { Output(first["console"], 0); Require(first["file"] is null, "disabled output"); }
        else if (mode == "closed") { Output(first["console"], 1); }
        else if (mode == "faulted") { Output(first["console"], 2); Output(captures[1]!["console"], 2, unavailable: 1); }
        else if (mode == "both")
        {
            Require(Value(first, "invalid") == 1 && Value(first, "truncated") == 1, "initial global facts");
            Output(first["console"], 3, filtered: 1); Output(first["file"], 1, filtered: 3);
        }
        else if (mode == "saturated")
        {
            Require(Value(first, "invalid") == 1 && Value(captures[1]!, "invalid") == 2 && Value(first, "truncated") == 1, "initial global facts");
            foreach (JsonNode? capture in captures) { Output(capture!["console"], 2, filtered: 1, full: 2); Output(capture["file"], 2, filtered: 1, full: 2); }
        }
        else
        {
            var targets = new HashSet<long>();
            foreach (JsonNode? capture in captures)
            {
                long target = Value(capture!["console"]!, "boundary"); Require(target is >= 1 and <= 65 && Value(capture["file"]!, "boundary") == target, "atomic output targets");
                Require(Value(capture, "invalid") is >= 0 and <= 16 && Value(capture, "truncated") == 0, "initial global facts"); targets.Add(target);
            }
            Require(actual["heldControls"]!["console"]!.GetValue<int>() == targets.Count && actual["heldControls"]!["file"]!.GetValue<int>() == targets.Count, "bounded target controls");
            Require(Value(captures[^1]!, "invalid") == 16 && Value(captures[^1]!["console"]!, "boundary") == 65, "complete cohort boundary");
        }
        if (mode is "saturated" or "concurrent") { Require(actual["coalesced"]!.GetValue<bool>(), "shared target registration"); }
        foreach (string name in new[] { "console", "file" })
        {
            JsonNode? writer = actual["writers"]![name]; if (writer is null) { continue; }
            Require(writer["maximum"]!.GetValue<int>() <= 1, "serialized writer");
            string[] order = writer["order"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
            string[] expected = mode switch
            {
                "empty" => ["flush.0"],
                "closed" => ["write.1"],
                "faulted" => [],
                "both" when name == "console" => ["write.1", "write.2", "write.3", "flush.3", "write.4", "write.5"],
                "both" => ["write.1", "flush.1", "write.2", "write.3"],
                "saturated" => ["write.1", "write.2", "flush.2", "write.3"],
                _ => []
            };
            if (mode != "concurrent") { Require(order.SequenceEqual(expected), "fixed target ordering"); }
            else
            {
                long written = 0; var flushed = new HashSet<long>();
                foreach (string operation in order)
                {
                    long sequence = long.Parse(operation.AsSpan(operation.IndexOf('.') + 1), CultureInfo.InvariantCulture);
                    if (operation.StartsWith("write.", StringComparison.Ordinal)) { Require(sequence == ++written, "contiguous writes"); }
                    else { Require(sequence == written && flushed.Add(sequence), "fixed target ordering"); }
                }
                Require(written == 65 && flushed.SetEquals(captures.Select(capture => Value(capture![name]!, "boundary"))), "covered cohort targets");
            }
        }
    }
}
