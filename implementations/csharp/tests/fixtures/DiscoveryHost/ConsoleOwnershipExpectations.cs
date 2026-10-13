using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConsoleOwnershipExpectations
{
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    internal static void Validate(JsonObject catalog, JsonObject row, JsonObject actual, bool physicalBytes = false)
    {
        JsonObject expected = row["expected"]!.AsObject(); string mode = row["mode"]!.GetValue<string>(); bool physical = row["physical"]!.GetValue<bool>(), fail = mode == "fault";
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Borrowed case identity differs.");
        foreach (string key in new[] { "selectionCalls", "clockCalls", "writeCalls", "maxActive", "disposedBeforeOwner", "asyncDisposed", "ownerDisposeCalls" })
        { Require(JsonNode.DeepEquals(expected[key], actual[key]), "Borrowed " + key + " differs."); }
        foreach (string key in new[] { "active", "flushes", "otherWriteCalls", "otherDisposes" }) { Require(actual[key]!.GetValue<int>() == 0, "Unexpected stream operation: " + key); }
        Require(!actual["inheritedContext"]!.GetValue<bool>(), "Console worker inherited creator flow.");
        int count = expected["records"]!.GetValue<int>(), runs = expected["runtimes"]!.GetValue<int>(), perRun = expected["perRuntime"]!.GetValue<int>();
        JsonObject[] results = actual["results"]!.AsArray().Select(x => x!.AsObject()).ToArray(); Require(results.Length == count, "Caller result count differs.");
        Require(results.Select(v => v["index"]!.GetValue<int>()).Order().SequenceEqual(Enumerable.Range(0, count)), "Caller identity set differs.");
        foreach (JsonObject result in results)
        {
            int index = result["index"]!.GetValue<int>(); Require(result["generation"]!.GetValue<int>() == index / perRun, "Runtime generation differs.");
            Require(result["outcome"]!.GetValue<string>() == "Accepted" && !result["truncated"]!.GetValue<bool>() && result["outputCount"]!.GetValue<int>() == 1
                && result["name"]!.GetValue<string>() == "console" && result["status"]!.GetValue<string>() == "Accepted", "Caller admission differs.");
        }
        for (int generation = 0; generation < runs; generation++)
        {
            JsonObject[] cohort = results.Where(v => v["generation"]!.GetValue<int>() == generation).ToArray();
            Require(cohort.Select(v => long.Parse(v["ticket"]!.GetValue<string>(), CultureInfo.InvariantCulture)).Order().SequenceEqual(Enumerable.Range(1, perRun).Select(v => (long)v)), "Successful ticket set differs.");
            if (mode != "serialized") { Require(cohort.All(v => v["ticket"]!.GetValue<string>() == (v["index"]!.GetValue<int>() % perRun + 1).ToString(CultureInfo.InvariantCulture)), "Sequential ticket order differs."); }
        }
        JsonArray stops = actual["stops"]!.AsArray(); Require(stops.Count == runs, "Completed runtime count differs.");
        foreach (JsonNode? stop in stops)
        {
            var wanted = new JsonObject
            {
                ["state"] = "Stopped",
                ["outputState"] = "Stopped",
                ["accepted"] = perRun,
                ["written"] = expected["written"]!.DeepClone(),
                ["failed"] = expected["failed"]!.DeepClone(),
                ["abandoned"] = expected["abandoned"]!.DeepClone(),
                ["pending"] = 0,
                ["inFlight"] = 0,
                ["queueFull"] = 0,
                ["filtered"] = 0,
                ["unavailable"] = 0,
                ["ticket"] = perRun,
                ["fault"] = fail ? "WriteFailed" : null,
                ["invalid"] = 0,
                ["truncated"] = 0
            };
            Require(JsonNode.DeepEquals(wanted, stop), "Complete console disposition differs.");
        }
        if (!physical && count > 0)
        {
            var wanted = new JsonObject
            {
                ["state"] = "Stopping",
                ["accepted"] = perRun,
                ["pending"] = perRun,
                ["written"] = 0,
                ["inFlight"] = 1,
                ["calls"] = 1,
                ["active"] = 1,
                ["disposes"] = 0,
                ["asyncDisposes"] = 0
            };
            Require(JsonNode.DeepEquals(wanted, actual["held"]), "Held stream closure/pending/serialization differs.");
        }
        else { Require(actual["held"] is null, "Unobserved held seam appeared."); }
        if (physical && !physicalBytes) { return; }
        string selected = row["stream"]!.GetValue<int>() == 0 ? "stderr" : "stdout", other = selected == "stderr" ? "stdout" : "stderr";
        byte[] selectedBytes = Convert.FromBase64String(actual[selected]!.GetValue<string>()), marker = Marker(selected);
        Require(selectedBytes.AsSpan().EndsWith(marker), "Selected application marker missing.");
        Require(Convert.FromBase64String(actual[other]!.GetValue<string>()).AsSpan().SequenceEqual(Marker(other)), "Unselected stream contains records or lost its application result.");
        byte[] bytes = selectedBytes[..^marker.Length];
        if (fail) { Require(bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(expected["failedPrefix"]!.GetValue<string>())), "Failed write prefix/retry differs."); return; }
        Require(bytes.Length == 0 || bytes[^1] == 10, "Record final LF missing.");
        string text = new UTF8Encoding(false, true).GetString(bytes); string[] lines = text.Length == 0 ? [] : text[..^1].Split('\n');
        JsonObject[] ordered = results.OrderBy(v => v["generation"]!.GetValue<int>()).ThenBy(v => long.Parse(v["ticket"]!.GetValue<string>(), CultureInfo.InvariantCulture)).ToArray();
        Require(lines.Length == count, "Whole record count differs.");
        for (int position = 0; position < lines.Length; position++)
        {
            int index = ordered[position]["index"]!.GetValue<int>(); byte[] line = Encoding.UTF8.GetBytes(lines[position] + "\n");
            Require(line.Length <= 65536 && !lines[position].StartsWith('\uFEFF'), "Record cap/BOM differs.");
            JsonObject wanted = catalog["baseRecord"]!.DeepClone().AsObject(); wanted["event_name"] = "borrow." + index.ToString(CultureInfo.InvariantCulture);
            wanted["message"] = "owned caf\u00e9 \U0001F642\nend=" + index.ToString(CultureInfo.InvariantCulture); wanted["attributes"] = new JsonObject { ["index"] = index };
            if (row["format"]!.GetValue<int>() == 1) { Require(JsonNode.DeepEquals(wanted, JsonNode.Parse(line)), "JSON facts/types/ticket order differ."); }
            else
            {
                Dictionary<string, JsonElement> fields = HumanRenderingExpectations.Read(line, 65536); Require(fields.Count == 5, "Default human detail differs.");
                foreach ((string display, string json) in new[] { ("timestamp", "timestamp"), ("severity", "status"), ("component", "component"), ("event_name", "event_name"), ("message", "message") })
                { Require(JsonNode.DeepEquals(JsonNode.Parse(fields[display].GetRawText()), wanted[json]), "Human facts/ticket order differ."); }
            }
        }
    }
    private static byte[] Marker(string stream) => Encoding.UTF8.GetBytes("{\"application\":\"" + stream + "-still-open\"}\n");
    internal static void Damage(JsonObject actual, string fault)
    {
        switch (fault)
        {
            case "dispose": actual["disposedBeforeOwner"] = 1; break;
            case "overlap": actual["maxActive"] = 2; break;
            case "ticket": actual["results"]![0]!["ticket"] = "99"; break;
            case "state": actual["stops"]![0]!["pending"] = 1; break;
            case "held": actual["held"]!["inFlight"] = 2; break;
            case "prefix": actual["stderr"] = Convert.ToBase64String(new byte[] { (byte)'x' }.Concat(Convert.FromBase64String(actual["stderr"]!.GetValue<string>())).ToArray()); break;
            case "fault": actual["stops"]![0]!["failed"] = 0; break;
            case "stream": JsonNode? prior = actual["stdout"]?.DeepClone(); actual["stdout"] = actual["stderr"]?.DeepClone(); actual["stderr"] = prior; break;
            case "bytes": actual["stderr"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(actual["stderr"]!.GetValue<string>())).Replace("Console.App", "Wrong.App", StringComparison.Ordinal))); break;
            case "order": string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(actual["stderr"]!.GetValue<string>())).Split('\n'); (lines[0], lines[1]) = (lines[1], lines[0]); actual["stderr"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', lines))); break;
            default: throw new ArgumentException("Unknown ownership damage.", nameof(fault));
        }
    }
}
