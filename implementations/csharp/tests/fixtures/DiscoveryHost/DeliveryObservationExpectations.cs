using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class DeliveryObservationExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case identity differs.");
        string[] names = new[] { "console", "file" }.Where(name => row[name + "Enabled"]!.GetValue<bool>()).ToArray();
        JsonObject expected = row["expected"]!.AsObject(); var projection = new JsonObject(); foreach (var item in expected) { projection[item.Key] = actual[item.Key]!.DeepClone(); }
        Require(JsonNode.DeepEquals(expected, projection), "Literal runtime boundaries/results/terminal errors differ.");
        long invalid = 0, truncated = 0; var previous = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        foreach (JsonNode? point in actual["checkpoints"]!.AsArray())
        {
            JsonNode obs = point!["observation"]!; long nextInvalid = Count(obs["invalid"]!), nextTruncated = Count(obs["truncated"]!);
            Require(nextInvalid >= invalid && nextTruncated >= truncated, "Global counter decreased."); invalid = nextInvalid; truncated = nextTruncated;
            JsonObject outputs = obs["outputs"]!.AsObject(); Require(outputs.Select(item => item.Key).SequenceEqual(names), "Output boundary order/set differs.");
            foreach (string name in names)
            {
                Dictionary<string, long> c = outputs[name]!.AsObject().ToDictionary(item => item.Key, item => Count(item.Value!), StringComparer.Ordinal);
                Require(c["sequence"] == c["accepted"] && c["accepted"] == (decimal)c["written"] + c["failed"] + c["abandoned"] + c["pending"], "Accepted ticket/disposition boundary differs.");
                Require(c["rejected"] == Math.Min(long.MaxValue, (decimal)c["queueFull"] + c["unavailable"]) && c["attempts"] == Math.Min(long.MaxValue, (decimal)c["accepted"] + c["queueFull"] + c["unavailable"]), "Admission aggregate differs.");
                Require(c["inFlight"] <= 1 && c["inFlight"] <= c["pending"] && c["pending"] <= 3, "Unresolved gauge bounds differ.");
                if (previous.TryGetValue(name, out Dictionary<string, long>? prior)) { foreach (string field in c.Keys.Where(field => field is not ("pending" or "inFlight"))) { Require(c[field] >= prior[field], "Cumulative output counter decreased."); } }
                previous[name] = c;
            }
        }
        foreach (string phase in new[] { "held", "cancellationRequested" }) { Require(actual[phase]!.AsObject().Select(item => item.Key).SequenceEqual(names), "Unresolved stream output set/order differs."); }
        JsonObject wires = actual["wires"]!.AsObject(); Require(wires.Select(item => item.Key).SequenceEqual(names), "Stream output set/order differs.");
        var originalByTicket = new Dictionary<long, byte[]>();
        foreach (string name in names)
        {
            string mode = row[name + "Mode"]!.GetValue<string>(); bool fails = mode is "partial-failure" or "complete-failure" or "zero-failure" or "partial-cancel";
            JsonNode wire = wires[name]!; JsonArray attempted = wire["attempted"]!.AsArray(); long[] tickets = fails ? [1L] : [1L, 2L, 3L];
            Require(attempted.Select(item => item!["ticket"]!.GetValue<long>()).SequenceEqual(tickets) && wire["confirmed"]!.AsArray().Select(item => item!.GetValue<long>()).SequenceEqual(fails ? [] : tickets), "Attempted/confirmed tickets differ or a write was retried.");
            var originals = new List<byte[]>();
            foreach (JsonNode? item in attempted)
            {
                byte[] before = Convert.FromBase64String(item!["before"]!.GetValue<string>()), after = Convert.FromBase64String(item["after"]!.GetValue<string>()); long ticket = item["ticket"]!.GetValue<long>();
                Require(before.AsSpan().SequenceEqual(after), "Owned captured bytes mutated."); originals.Add(before);
                Require(before.Length is > 0 and <= 16384 && before[^1] == (byte)'\n' && !before.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) && !before.Contains((byte)'\r'), "Complete captured record bounds/framing differ.");
                string text = new UTF8Encoding(false, true).GetString(before); Require(text.Count(value => value == '\n') == 1, "Raw record boundary injected."); JsonNode body = JsonNode.Parse(text)!;
                Require(body["component"]!.GetValue<string>() == "Delivery" && body["event_name"]!.GetValue<string>() == "delivery.event" && body["status"]!.GetValue<string>() == "warn" && body["schema_version"]!.GetValue<int>() == 1, "Captured event identity differs.");
                if (ticket <= 2) { Require(body["message"]!.GetValue<string>() == (ticket == 1 ? "seed \U0001F642\n\u0001quote\"" : "queued-one"), "Captured caller message differs."); }
                else { Require(body["truncated"]!.GetValue<bool>(), "Queued oversized record lost truncation provenance."); }
                if (originalByTicket.TryGetValue(ticket, out byte[]? prior)) { Require(before.AsSpan().SequenceEqual(prior), "Shared captured event changed across outputs."); } else { originalByTicket[ticket] = before; }
            }
            byte[] first = originals[0]; int prefix = mode.StartsWith("complete", StringComparison.Ordinal) ? first.Length : mode == "zero-failure" ? 0 : first.Length / 2;
            foreach (string phase in new[] { "held", "cancellationRequested" })
            {
                JsonNode state = actual[phase]![name]!; Require(Convert.FromBase64String(state["bytes"]!.GetValue<string>()).AsSpan().SequenceEqual(first.AsSpan(0, prefix)), "Unresolved stream bytes differ.");
                Require(state["cancellationRequested"]!.GetValue<bool>() == (phase == "cancellationRequested" && row[name + "Cancel"]!.GetValue<bool>()), "Cancellation request observation differs.");
            }
            byte[] emitted = Convert.FromBase64String(wire["stream"]!.GetValue<string>()), expectedBytes = fails ? first[..prefix] : originals.SelectMany(bytes => bytes).ToArray();
            Require(emitted.AsSpan().SequenceEqual(expectedBytes), "Partial/terminal stream bytes differ or record duplicated.");
        }
    }
    private static long Count(JsonNode value)
    {
        string text = value.GetValue<string>(); Require(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long count) && count >= 0 && count.ToString(CultureInfo.InvariantCulture) == text, "Noncanonical/negative Int64 observation."); return count;
    }
    private static void Require(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
}
