using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class QueuedFanoutExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]) && JsonNode.DeepEquals(row["expected"], actual["semantic"]), "Literal fanout/ticket/gate/capture observations differ.");
        ValidateTransport(row, actual);
    }
    internal static void ValidateTransport(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case identity differs.");
        Require(actual["independentProgress"]!.GetValue<bool>(), "Blocked file prevented independent console progress.");
        JsonArray bindings = actual["bindings"]!.AsArray(); Require(bindings.Count == row["steps"]!.AsArray().Count, "Missing producer bindings.");
        Require(bindings.Select(item => item!["caller"]!.GetValue<int>()).Order().SequenceEqual(row["steps"]!.AsArray().Select(item => item!["label"]!.GetValue<int>()).Order()), "Producer binding set differs.");
        var acceptedTickets = new HashSet<(string Name, long Ticket)>();
        for (int index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index]!.DeepClone().AsObject(); binding.Remove("caller"); Require(JsonNode.DeepEquals(binding, actual["semantic"]!["results"]![index]), "Binding result differs.");
            foreach (JsonNode? output in binding["outputs"]!.AsArray())
            {
                string name = output!["name"]!.GetValue<string>();
                if (output["status"]!.GetValue<string>() == "Accepted")
                {
                    Require(output["ticket"] is JsonValue value && value.TryGetValue<long>(out long ticket) && ticket > 0 && acceptedTickets.Add((name, ticket)), "Accepted ticket domain/uniqueness differs.");
                }
                else { Require(output["ticket"] is null, "Rejection has a ticket."); }
            }
        }
        Require(actual["wires"]!.AsObject().Count == row["expected"]!["outputs"]!.AsObject().Count, "Unexpected output wire.");
        foreach (string name in new[] { "console", "file" })
        {
            JsonNode? wire = actual["wires"]![name]; Require((wire is not null) == row[name + "Enabled"]!.GetValue<bool>(), "Enabled wire set differs."); if (wire is null) { continue; }
            Require(wire["pendingAfterComplete"]!.GetValue<int>() == 0, "Retained queued work after completion.");
            var originals = new Dictionary<long, byte[]>();
            foreach (JsonNode? item in wire["attempted"]!.AsArray())
            {
                long ticket = item!["ticket"]!.GetValue<long>(); byte[] raw = Convert.FromBase64String(item["wire"]!.GetValue<string>());
                Require(raw.AsSpan().SequenceEqual(Convert.FromBase64String(item["after"]!.GetValue<string>())) && originals.TryAdd(ticket, raw), "Capture changed or ticket reused.");
                string text = new UTF8Encoding(false, true).GetString(raw); Require(raw.Length is > 0 and <= 65536 && !text.StartsWith("\uFEFF", StringComparison.Ordinal) && text.EndsWith("\n", StringComparison.Ordinal) && text.Count(c => c == '\n') == 1, "Record framing/bounds differ.");
                JsonNode record = JsonNode.Parse(raw)!; JsonNode? binding = bindings.SingleOrDefault(binding => binding!["outputs"]!.AsArray().Any(output => output!["name"]!.GetValue<string>() == name && output["status"]!.GetValue<string>() == "Accepted" && output["ticket"]!.GetValue<long>() == ticket))!;
                Require(binding is not null, "Accepted ticket has no matching caller binding.");
                int caller = binding!["caller"]!.GetValue<int>(); JsonNode step = row["steps"]!.AsArray().Single(step => step!["label"]!.GetValue<int>() == caller)!;
                Require(record["message"]!.GetValue<string>() == "record-" + caller && JsonNode.DeepEquals(record["attributes"]!["input"], JsonValue.Create(caller)) && record["service"]!.GetValue<string>() == "Fanout.App" && record["component"]!.GetValue<string>() == "Fanout" && record["event_name"]!.GetValue<string>() == "fanout.event", "Original producer facts differ.");
                Require(record["status"]!.GetValue<string>() == (step["level"]!.GetValue<string>() switch { "Debug" => "debug", "Warning" => "warn", _ => "info" }), "Captured severity differs.");
            }
            Require(wire["attempted"]!.AsArray().Select(item => item!["ticket"]!.GetValue<long>()).SequenceEqual(actual["semantic"]!["outputs"]![name]!["attemptedTickets"]!.AsArray().Select(item => item!.GetValue<long>())), "Attempted ticket stream differs.");
            byte[] expected = actual["semantic"]!["outputs"]![name]!["writtenTickets"]!.AsArray().SelectMany(ticket => originals[ticket!.GetValue<long>()]).ToArray();
            Require(expected.AsSpan().SequenceEqual(Convert.FromBase64String(wire["stream"]!.GetValue<string>())), "Confirmed stream bytes/order differ.");
        }
    }
    private static void Require(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
}
