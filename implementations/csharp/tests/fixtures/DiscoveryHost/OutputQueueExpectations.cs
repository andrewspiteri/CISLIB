using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OutputQueueExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]) && actual["workers"]!.GetValue<int>() == row["expected"]!["workers"]!.GetValue<int>(), "Queue identity/enabled worker count differs.");
        Require(actual["distinctWorkers"]!.GetValue<bool>() && actual["completed"]!.GetValue<bool>(), "Worker ownership/completion differs.");
        bool independent = row["mode"]!.GetValue<string>() == "independent";
        if (independent) { Require(actual["outputs"]!["consoleProgressWhileFileBlocked"]!.GetValue<bool>(), "Blocked file prevented console progress."); }
        foreach (string name in new[] { "console", "file" })
        {
            bool enabled = row[name + "Enabled"]!.GetValue<bool>(); JsonNode? output = actual["outputs"]![name];
            Require((output is not null) == enabled, "Disabled worker exists or enabled worker missing."); if (output is null) { continue; }
            int accepted = row["expected"]!["acceptedPerOutput"]!.GetValue<int>(), rejected = row["expected"]!["rejectedPerOutput"]!.GetValue<int>();
            Require(output["admissions"]!.AsArray().Select(value => value!.GetValue<bool>()).SequenceEqual(Enumerable.Repeat(true, accepted).Concat(Enumerable.Repeat(false, rejected))), "Bounded incoming admission/eviction differs.");
            Require(output["pendingAtBarrier"]!.GetValue<int>() == (independent && name == "console" ? 0 : row["expected"]!["pendingAtBarrier"]!.GetValue<int>()) && output["maximumActive"]!.GetValue<int>() == 1, "Queue/in-flight bound differs.");
            foreach (string flag in new[] { "admittedAfterComplete", "ambientSeen", "inlineSeen" }) { Require(!output[flag]!.GetValue<bool>(), "Worker closure/context/producer isolation differs."); }
            Require(output["ownedReferences"]!.GetValue<bool>() && output["pendingAfterComplete"]!.GetValue<int>() == 0 && output["activeAfterComplete"]!.GetValue<int>() == 0, "Ownership or terminal retained work differs.");
            Require(Convert.FromBase64String(output["stream"]!.GetValue<string>()).AsSpan().SequenceEqual(Convert.FromBase64String(output["expectedStream"]!.GetValue<string>())), "Memory writer bytes/order changed.");
            string text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(output["stream"]!.GetValue<string>()));
            Require(!text.StartsWith("\uFEFF", StringComparison.Ordinal) && text.EndsWith("\n", StringComparison.Ordinal), "Memory writer record framing differs.");
            string[] records = text.Split('\n');
            Require(records.Length == accepted + 1 && records[^1].Length == 0 && records.Take(accepted).All(line => Encoding.UTF8.GetByteCount(line) + 1 <= 65536), "Memory writer record count/bounds differs.");
            Require(records.Take(accepted).Select(line => JsonNode.Parse(line)!["message"]!.GetValue<string>()).SequenceEqual(Enumerable.Range(0, accepted).Select(index => "record-" + index)), "Actual stream facts/order differ.");
            Require(output["messages"]!.AsArray().Select(value => value!.GetValue<string>()).SequenceEqual(Enumerable.Range(0, accepted).Select(index => "record-" + index)), "Previously admitted work was lost or reordered.");
        }
    }
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
}
