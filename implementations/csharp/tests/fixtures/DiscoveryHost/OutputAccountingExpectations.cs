using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OutputAccountingExpectations
{
    private static readonly string[] Fields = ["filtered", "attempts", "accepted", "queueFull", "unavailable", "rejected", "written", "failed", "abandoned", "pending", "inFlight"];
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case identity differs.");
        string[] names = new[] { "console", "file" }.Where(name => row[name + "Enabled"]!.GetValue<bool>()).ToArray();
        var previous = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        foreach (JsonNode? point in actual["checkpoints"]!.AsArray())
        {
            JsonObject outputs = point!["outputs"]!.AsObject();
            Require(outputs.Select(item => item.Key).SequenceEqual(names), "Enabled output set/order differs.");
            foreach (string name in names)
            {
                JsonObject source = outputs[name]!.AsObject(); Require(source.Select(item => item.Key).Order(StringComparer.Ordinal).SequenceEqual(Fields.Order(StringComparer.Ordinal)), "Counter shape differs.");
                var count = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (string field in Fields)
                {
                    string text = source[field]!.GetValue<string>();
                    Require(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value >= 0 && value.ToString(CultureInfo.InvariantCulture) == text, "Count is not canonical nonnegative Int64."); count[field] = value;
                }
                Require(count["accepted"] == (decimal)count["written"] + count["failed"] + count["abandoned"] + count["pending"], "Accepted disposition identity differs.");
                Require(count["rejected"] == Math.Min(long.MaxValue, (decimal)count["queueFull"] + count["unavailable"]), "Rejection aggregate differs.");
                Require(count["attempts"] == Math.Min(long.MaxValue, (decimal)count["accepted"] + count["queueFull"] + count["unavailable"]), "Admission aggregate differs.");
                Require(count["inFlight"] <= 1 && count["inFlight"] <= count["pending"] && count["pending"] <= 3, "Pending/in-flight bounds differ.");
                if (previous.TryGetValue(name, out Dictionary<string, long>? prior))
                { foreach (string field in Fields.Take(9)) { Require(count[field] >= prior[field], "Cumulative count decreased."); } }
                previous[name] = count;
            }
        }
        JsonObject expected = row["expected"]!.AsObject(); var projection = new JsonObject();
        foreach (var item in expected) { Require(actual.ContainsKey(item.Key), "Expected observation missing."); projection[item.Key] = actual[item.Key]!.DeepClone(); }
        Require(JsonNode.DeepEquals(expected, projection), "Literal accounting checkpoints/results/writer attempts differ.");
    }
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
