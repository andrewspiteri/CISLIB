using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class QueueBurstExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        QueuedFanoutExpectations.ValidateTransport(row, actual);
        JsonArray bindings = actual["bindings"]!.AsArray();
        string[] names = new[] { "console", "file" }.Where(name => row[name + "Enabled"]!.GetValue<bool>()).ToArray();
        var phases = new JsonObject(); var outputCounts = names.ToDictionary(name => name, _ => new JsonObject { ["Accepted"] = 0, ["Filtered"] = 0, ["QueueFull"] = 0 }, StringComparer.Ordinal);
        foreach (JsonNode? binding in bindings)
        {
            int caller = binding!["caller"]!.GetValue<int>(); JsonNode step = row["steps"]!.AsArray().Single(step => step!["label"]!.GetValue<int>() == caller)!;
            string phase = step["phase"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (phases[phase] is null) { phases[phase] = new JsonObject { ["Accepted"] = 0, ["PartiallyAccepted"] = 0, ["Rejected"] = 0, ["Filtered"] = 0 }; }
            JsonArray outputs = binding["outputs"]!.AsArray(); Require(outputs.Select(output => output!["name"]!.GetValue<string>()).SequenceEqual(names), "Enabled result order/set differs.");
            int interested = 0, accepted = 0;
            foreach (JsonNode? output in outputs)
            {
                string name = output!["name"]!.GetValue<string>(), status = output["status"]!.GetValue<string>(), level = step["level"]!.GetValue<string>();
                bool eligible = level != "None" && Rank(level) >= Rank(row[name + "Level"]!.GetValue<string>());
                Require(eligible ? status is "Accepted" or "QueueFull" : status == "Filtered", "Threshold/admission status differs.");
                if (eligible) { interested++; }
                if (status == "Accepted") { accepted++; Require(output["ticket"] is not null && output["ticket"]!.GetValue<long>() > 0, "Acceptance lacks a positive ticket."); }
                else { Require(output["ticket"] is null, "Rejection has a ticket."); }
                outputCounts[name][status] = outputCounts[name][status]!.GetValue<int>() + 1;
            }
            string outcome = interested == 0 ? "Filtered" : accepted == interested ? "Accepted" : accepted > 0 ? "PartiallyAccepted" : "Rejected";
            Require(binding["outcome"]!.GetValue<string>() == outcome, "Aggregate outcome differs from interested output results.");
            phases[phase]![outcome] = phases[phase]![outcome]!.GetValue<int>() + 1;
        }
        Require(actual["bounds"]!.AsObject().Select(pair => pair.Key).Order().SequenceEqual(names.Order()), "Enabled bound set differs.");
        var observations = new JsonObject();
        foreach (string name in names)
        {
            JsonNode output = actual["semantic"]!["outputs"]![name]!; JsonNode bound = actual["bounds"]![name]!;
            int count = outputCounts[name]["Accepted"]!.GetValue<int>();
            long[] tickets = bindings.SelectMany(binding => binding!["outputs"]!.AsArray()).Where(output => output!["name"]!.GetValue<string>() == name && output["status"]!.GetValue<string>() == "Accepted").Select(output => output!["ticket"]!.GetValue<long>()).Order().ToArray();
            Require(tickets.SequenceEqual(Enumerable.Range(1, count).Select(ticket => (long)ticket)), "Accepted ticket prefix differs.");
            Require(output["attemptedTickets"]!.AsArray().Select(ticket => ticket!.GetValue<long>()).SequenceEqual(tickets) && JsonNode.DeepEquals(output["attemptedTickets"], output["writtenTickets"]), "Healthy accepted work was lost or reordered.");
            Require(!output["faulted"]!.GetValue<bool>() && !output["originalFaultObserved"]!.GetValue<bool>() && output["unavailableCount"]!.GetValue<long>() == 0 && output["filteredCount"]!.GetValue<long>() == outputCounts[name]["Filtered"]!.GetValue<int>(), "Unexpected fault/filter observation.");
            var observed = bound.DeepClone().AsObject(); observed["admissions"] = outputCounts[name]; observed["lastAcceptedSequence"] = output["lastAcceptedSequence"]!.DeepClone(); observed["writtenCount"] = output["writtenTickets"]!.AsArray().Count; observations[name] = observed;
        }
        var summary = new JsonObject { ["workers"] = actual["workers"]!.DeepClone(), ["phaseOutcomes"] = phases, ["outputs"] = observations };
        foreach (string field in new[] { "factoryCalls", "clockCalls", "sharedCapturePairs", "sharedCaptures", "callbackGateHeld" }) { summary[field] = actual["semantic"]![field]!.DeepClone(); }
        Require(JsonNode.DeepEquals(row["expected"], summary), "Literal burst counts, queue/worker bounds or capture observations differ.");
    }
    private static int Rank(string level) => level switch { "Trace" => 0, "Debug" => 1, "Information" => 2, "Warning" => 3, "Error" => 4, "Critical" => 5, "None" => 6, _ => throw new InvalidDataException("Unknown fixture level.") };
    private static void Require(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
}
