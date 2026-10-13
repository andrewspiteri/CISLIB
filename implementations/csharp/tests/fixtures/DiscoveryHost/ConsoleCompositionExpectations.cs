using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConsoleCompositionExpectations
{
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    internal static void Validate(JsonObject catalog, JsonObject row, JsonObject actual, bool physicalBytes = false)
    {
        JsonObject expected = row["expected"]!.AsObject(); Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Console case identity differs.");
        Require(JsonNode.DeepEquals(expected["results"], actual["results"]), "Frozen decisions/tickets/loss differ.");
        foreach (string key in new[] { "clockCalls", "selectionCalls" }) { Require(JsonNode.DeepEquals(expected[key], actual[key]), "Console " + key + " differs."); }
        Require(actual["state"]!.GetValue<string>() == "Stopped" && actual["invalid"]!.GetValue<string>() == "0"
            && actual["truncated"]!.GetValue<string>() == expected["truncatedEvents"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture), "Global console accounting differs.");
        JsonArray outputs = actual["outputs"]!.AsArray(); string profile = row["profile"]!.GetValue<string>();
        string[] names = profile == "both" ? ["console", "file"] : profile == "console" ? ["console"] : profile == "file" ? ["file"] : [];
        Require(outputs.Count == names.Length, "Configured output set differs.");
        for (int index = 0; index < outputs.Count; index++)
        {
            JsonObject output = outputs[index]!.AsObject(); string name = names[index]; int records = expected[name + "Records"]!.GetValue<int>();
            int filtered = expected["results"]!.AsArray().Count(result => result!["outputs"]!.AsArray().Any(value => value!["name"]!.GetValue<string>() == name && value["status"]!.GetValue<string>() == "Filtered"));
            Require(output["name"]!.GetValue<string>() == name && output["accepted"]!.GetValue<string>() == records.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && output["written"]!.GetValue<string>() == output["accepted"]!.GetValue<string>() && output["filtered"]!.GetValue<string>() == filtered.ToString(System.Globalization.CultureInfo.InvariantCulture), "Console accepted/written/filtered differs.");
            foreach (string key in new[] { "pending", "failed", "abandoned" }) { Require(output[key]!.GetValue<string>() == "0", "Unresolved console work appeared."); }
        }
        if (!row["physical"]!.GetValue<bool>() || physicalBytes)
        {
            string selected = row["stream"]!.GetValue<int>() == 0 ? "stderr" : "stdout", other = selected == "stderr" ? "stdout" : "stderr";
            Require(actual[other]!.GetValue<string>() == "", "Records reached the unselected console stream.");
            CheckLines(catalog, row, actual[selected]!.GetValue<string>(), "console", row["format"]!.GetValue<int>(), row["detail"]!.GetValue<int>());
        }
        CheckLines(catalog, row, actual["file"]!.GetValue<string>(), "file", 1, 0);
    }
    private static void CheckLines(JsonObject catalog, JsonObject row, string token, string destination, int format, int detail)
    {
        byte[] bytes = Convert.FromBase64String(token); int cap = row["maxEntryBytes"]!.GetValue<int>();
        var levels = row["expected"]!["results"]!.AsArray().Where(result => result!["outputs"]!.AsArray().Any(output => output!["name"]!.GetValue<string>() == destination && output["status"]!.GetValue<string>() == "Accepted")).Select(result => result!["level"]!.GetValue<int>()).ToArray();
        Require(bytes.Length == 0 || bytes[^1] == 10, "Console final LF missing."); Require(!bytes.Contains((byte)13) && !bytes.Contains((byte)27), "Raw console controls appeared.");
        string text = new UTF8Encoding(false, true).GetString(bytes); string[] lines = text.Length == 0 ? [] : text[..^1].Split('\n');
        Require(lines.Length == levels.Length, "Console line count differs.");
        for (int index = 0; index < lines.Length; index++)
        {
            byte[] line = Encoding.UTF8.GetBytes(lines[index] + "\n"); Require(line.Length <= cap, "Console entry cap exceeded.");
            Require(line.Length < 3 || !line.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "Console line BOM appeared.");
            JsonObject wanted = catalog["baseRecord"]!.DeepClone().AsObject(); int level = levels[index];
            wanted["status"] = new[] { "trace", "debug", "info", "warn", "error", "critical" }[level];
            bool large = row["mode"]!.GetValue<string>() == "cap", capturedLoss = row["expected"]!["captureTruncated"]!.GetValue<bool>();
            wanted["message"] = large ? new string('\u200d', 8192) : "message-" + level + " caf\u00e9 \U0001F642\nnext \u202e";
            if (large) { wanted["attributes"] = new JsonObject { ["payload"] = new string('\u200d', 8192) }; }
            if (format == 1)
            {
                JsonObject value = JsonNode.Parse(line)!.AsObject();
                if (!capturedLoss) { Require(JsonNode.DeepEquals(wanted, value), "Console JSONL facts/types differ."); }
                else
                {
                    foreach (string key in new[] { "schema_version", "timestamp", "status", "event_name", "service", "env", "version", "component", "resource", "operation_id" })
                    { Require(JsonNode.DeepEquals(wanted[key], value[key]), "Protected truncated JSONL fact differs."); }
                    Require(value["truncated"]!.GetValue<bool>() && value["message"]!.GetValue<string>().Length is > 0 and < 8192 && value["message"]!.GetValue<string>().All(c => c == '\u200d'), "Captured truncation differs.");
                }
            }
            else
            {
                Dictionary<string, JsonElement> fields = HumanRenderingExpectations.Read(line, cap);
                foreach ((string display, string json) in new[] { ("timestamp", "timestamp"), ("severity", "status"), ("component", "component"), ("event_name", "event_name") })
                { Require(JsonNode.DeepEquals(JsonNode.Parse(fields[display].GetRawText()), wanted[json]), "Console human identity differs."); }
                string message = fields["message"].GetString()!;
                Require(!capturedLoss ? message == wanted["message"]!.GetValue<string>() : message.Length is > 0 and < 8192 && message.All(c => c == '\u200d'), "Console human message differs.");
                bool loss = row["expected"]!["truncatedEvents"]!.GetValue<int>() != 0;
                Require(fields.TryGetValue("truncated", out JsonElement marker) ? loss && marker.GetBoolean() : !loss, "Console human loss marker differs.");
                if (detail == 0) { Require(fields.Count == (loss ? 6 : 5), "Normal detail includes extra fields."); }
                else
                {
                    foreach (string key in new[] { "service", "env", "version", "operation_id" }) { Require(JsonNode.DeepEquals(JsonNode.Parse(fields[key].GetRawText()), wanted[key]), "Verbose identity differs."); }
                    foreach (var value in wanted["resource"]!.AsObject()) { Require(JsonNode.DeepEquals(JsonNode.Parse(fields["resource." + value.Key].GetRawText()), value.Value), "Verbose resource differs."); }
                    bool omitted = large && cap <= 65536;
                    Require(fields.ContainsKey("attributes") == !omitted, "Verbose payload selection differs.");
                    if (!omitted) { Require(JsonNode.DeepEquals(JsonNode.Parse(fields["attributes"].GetRawText()), wanted["attributes"]), "Verbose typed attributes differ."); }
                    bool hasTemplate = !large || cap >= 65536;
                    Require(fields.ContainsKey("message_template") == hasTemplate, "Verbose template selection differs.");
                    if (hasTemplate) { Require(JsonNode.DeepEquals(JsonNode.Parse(fields["message_template"].GetRawText()), wanted["message_template"]), "Verbose literal template differs."); }
                    var keys = new HashSet<string>(new[] { "timestamp", "severity", "component", "event_name", "message", "service", "env", "version", "operation_id" }, StringComparer.Ordinal);
                    foreach (var value in wanted["resource"]!.AsObject()) { keys.Add("resource." + value.Key); }
                    if (!omitted) { keys.Add("attributes"); }
                    if (hasTemplate) { keys.Add("message_template"); }
                    if (loss) { keys.Add("truncated"); }
                    Require(keys.SetEquals(fields.Keys), "Verbose human field set differs.");
                }
            }
        }
    }
    internal static void Damage(JsonObject actual, string fault)
    {
        switch (fault)
        {
            case "stream": (actual["stdout"], actual["stderr"]) = (actual["stderr"]!.DeepClone(), actual["stdout"]!.DeepClone()); break;
            case "bom": actual["stderr"] = Convert.ToBase64String(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Convert.FromBase64String(actual["stderr"]!.GetValue<string>())).ToArray()); break;
            case "outcome": actual["results"]![0]!["outcome"] = "Accepted"; break;
            case "ticket": actual["results"]![1]!["outputs"]![0]!["ticket"] = "99"; break;
            case "loss": actual["truncated"] = "99"; break;
            case "clock": actual["clockCalls"] = 99; break;
            case "identity":
                byte[] bytes = Convert.FromBase64String(actual["stderr"]!.GetValue<string>()); string text = Encoding.UTF8.GetString(bytes).Replace("Console.App", "Wrong.App", StringComparison.Ordinal);
                actual["stderr"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
