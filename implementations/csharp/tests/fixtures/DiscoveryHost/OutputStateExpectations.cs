using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OutputStateExpectations
{
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case differs.");
        Require(JsonNode.DeepEquals(row["expected"]!["result"], actual["result"]), "Independent post-fault admission differs.");
        string[] names = new[] { "console", "file" }.Where(name => row[name + "Mode"]!.GetValue<string>() != "disabled").ToArray();
        JsonArray expected = row["expected"]!["checkpoints"]!.AsArray(), points = actual["checkpoints"]!.AsArray(); Require(points.Count == expected.Count, "Boundary set differs.");
        var faults = new Dictionary<string, string>(StringComparer.Ordinal);
        var previous = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        DateTimeOffset before = DateTimeOffset.ParseExact(actual["beforeUtc"]!.GetValue<string>(), "O", CultureInfo.InvariantCulture), after = DateTimeOffset.ParseExact(actual["afterUtc"]!.GetValue<string>(), "O", CultureInfo.InvariantCulture);
        Require(before.Offset == TimeSpan.Zero && after.Offset == TimeSpan.Zero && before <= after
            && (string?)actual["beforeUtc"] == before.ToString("O", CultureInfo.InvariantCulture) && (string?)actual["afterUtc"] == after.ToString("O", CultureInfo.InvariantCulture), "Noncanonical capture interval.");
        for (int index = 0; index < points.Count; index++)
        {
            JsonObject point = points[index]!.AsObject(), wanted = expected[index]!.AsObject();
            Require(JsonNode.DeepEquals(point["phase"], wanted["phase"]) && JsonNode.DeepEquals(point["closed"], wanted["closed"]), "Boundary phase/closure differs.");
            Require(point["invalid"]!.GetValue<string>() == "0" && point["truncated"]!.GetValue<string>() == "0", "Fault created a recursive event.");
            JsonObject outputs = point["outputs"]!.AsObject(); Require(outputs.Select(item => item.Key).SequenceEqual(names), "Output set/order differs.");
            foreach (string name in names)
            {
                JsonObject counts = outputs[name]!.AsObject(), expectedCounts = wanted["outputs"]![name]!.AsObject();
                Require(counts.Select(field => field.Key).ToHashSet(StringComparer.Ordinal).SetEquals(expectedCounts.Select(field => field.Key).Append("faultAtUtc")), "Output state field set differs.");
                foreach (var item in expectedCounts) { Require(JsonNode.DeepEquals(item.Value, counts[item.Key]), "Literal state/disposition differs: " + name + "/" + item.Key); }
                long Count(string key) { string token = counts[key]!.GetValue<string>(); Require(long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value >= 0 && token == value.ToString(CultureInfo.InvariantCulture), "Noncanonical counter."); return value; }
                Require(Count("accepted") == Count("written") + Count("failed") + Count("abandoned") + Count("pending") && Count("inFlight") <= Count("pending") && Count("inFlight") <= 1, "Accepted disposition differs.");
                var current = new[] { "accepted", "written", "failed", "abandoned", "pending", "inFlight", "unavailable" }.ToDictionary(key => key, Count, StringComparer.Ordinal);
                if (previous.TryGetValue(name, out Dictionary<string, long>? prior)) { Require(current.All(pair => pair.Key is "pending" or "inFlight" || pair.Value >= prior[pair.Key]), "Cumulative count decreased."); }
                previous[name] = current;
                if (counts["fault"] is null) { Require(counts["faultAtUtc"] is null, "Nonfaulted timestamp appeared."); }
                else
                {
                    string token = counts["faultAtUtc"]!.GetValue<string>(); DateTimeOffset at = DateTimeOffset.ParseExact(token, "O", CultureInfo.InvariantCulture);
                    Require(at.Offset == TimeSpan.Zero && at >= before && at <= after, "Fault time is not captured UTC.");
                    if (faults.TryGetValue(name, out string? retained)) { Require(token == retained, "Fault time changed across stop."); }
                    else { faults[name] = token; }
                }
            }
        }
        Require(actual["wires"]!.AsObject().Select(item => item.Key).SequenceEqual(names), "Writer set differs.");
        int failures = 0; var shared = new Dictionary<long, byte[]>();
        foreach (string name in names)
        {
            bool fails = row[name + "Mode"]!.GetValue<string>() != "success";
            int total = row["closeBeforeRelease"]!.GetValue<bool>() || row["releaseFirst"]!.GetValue<string>() == name ? 3 : 4;
            JsonObject wire = actual["wires"]![name]!.AsObject(); JsonArray attempted = wire["attempts"]!.AsArray(), confirmed = wire["confirmed"]!.AsArray();
            Require(attempted.Count == (fails ? 1 : total) && confirmed.Count == (fails ? 0 : total), "Writer retried/attempted abandoned work or invented confirmation.");
            var originals = new List<byte[]>();
            for (int index = 0; index < attempted.Count; index++)
            {
                long ticket = attempted[index]!["ticket"]!.GetValue<long>(); Require(ticket == index + 1 && (fails || confirmed[index]!.GetValue<long>() == ticket), "Ticket order differs.");
                byte[] bytes = Convert.FromBase64String(attempted[index]!["bytes"]!.GetValue<string>()); originals.Add(bytes);
                Require(bytes.Length is > 0 and <= 65536 && !bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) && bytes[^1] == 10 && bytes.Count(value => value == 10) == 1 && !bytes.Contains((byte)13), "Captured framing differs.");
                JsonObject body = JsonNode.Parse(new UTF8Encoding(false, true).GetString(bytes))!.AsObject(); Require(body["component"]!.GetValue<string>() == "States" && body["event_name"]!.GetValue<string>() == "states.event" && body["message"]!.GetValue<string>() == "record-" + ticket, "Original event differs.");
                if (shared.TryGetValue(ticket, out byte[]? original)) { Require(bytes.SequenceEqual(original), "Shared capture changed."); } else { shared[ticket] = bytes; }
            }
            byte[] stream = Convert.FromBase64String(wire["bytes"]!.GetValue<string>());
            Require(stream.SequenceEqual(fails ? originals[0][..(originals[0].Length / 2)] : originals.SelectMany(bytes => bytes).ToArray()), "Partial/complete stream differs.");
            Require(wire["privateExceptionAccesses"]!.GetValue<int>() == 0, "Private exception was read/formatted."); if (fails) { failures++; }
        }
        Require(actual["originalErrors"]!.GetValue<int>() == failures, "Original writer errors were replaced.");
    }
    internal static void Damage(JsonObject actual, string fault)
    {
        JsonObject final = actual["checkpoints"]!.AsArray().Last()!["outputs"]!["file"]!.AsObject();
        switch (fault)
        {
            case "fault": final["fault"] = "private/path/payload"; break;
            case "time": final["faultAtUtc"] = "2001-01-01T00:00:00.0000000+00:00"; break;
            case "state": final["state"] = "Running"; break;
            case "abandon": final["abandoned"] = "0"; break;
            case "retry": actual["wires"]!["file"]!["attempts"]!.AsArray().Add(actual["wires"]!["file"]!["attempts"]![0]!.DeepClone()); break;
            case "private": actual["wires"]!["file"]!["privateExceptionAccesses"] = 1; break;
            default: actual["checkpoints"]![0]!["outputs"] = actual["checkpoints"]!.AsArray().Last()!["outputs"]!.DeepClone(); break;
        }
    }
}
