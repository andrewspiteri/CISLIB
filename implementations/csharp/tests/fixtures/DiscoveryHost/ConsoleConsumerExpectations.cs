using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConsoleConsumerExpectations
{
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    internal static Dictionary<string, string?> EnvironmentFor(JsonObject row)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in System.Environment.GetEnvironmentVariables().Keys)
        { if (key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase) || key is "DD_SERVICE" or "DD_ENV" or "DD_VERSION") { values[key] = null; } }
        values["DD_SERVICE"] = "unused-alias"; values["DD_ENV"] = "unused-alias"; values["DD_VERSION"] = "unused-alias";
        values["TERM"] = "dumb"; values["NO_COLOR"] = "1";
        foreach (var pair in row["environment"]!.AsObject()) { values[pair.Key] = pair.Value!.GetValue<string>(); }
        return values;
    }
    internal static void Validate(JsonObject catalog, JsonObject row, JsonObject actual, bool physicalBytes = false)
    {
        Require(actual["ok"]!.GetValue<bool>() && JsonNode.DeepEquals(row["id"], actual["caseId"]), "Consumer command result differs.");
        JsonObject expected = row["expected"]!.AsObject();
        foreach ((string key, string message) in new[]
        {
            ("settings", "Environment-selected settings differ."), ("results", "Consumer outcomes/tickets differ."),
            ("factoryCalls", "Consumer factory calls differ."), ("clockCalls", "Consumer clock calls differ."),
            ("final", "Consumer final accounting differs."), ("checkpoints", "Independent file progress differs."),
            ("selectedStreamArgument", "Consumer stream selector argument differs."), ("borrowedDisposes", "Borrowed console disposal differs."), ("afterApplication", "Borrowed application bytes differ.")
        }) { Require(actual.ContainsKey(key) && JsonNode.DeepEquals(expected[key], actual[key]), message); }
        JsonObject resource = actual["resource"]!.AsObject();
        Require(resource["build_version"]!.GetValue<string>() == catalog["buildVersion"]!.GetValue<string>()
            && resource["environment_kind"]!.GetValue<string>() == "console"
            && resource["os_family"]!.GetValue<string>() is "windows" or "linux"
            && resource["process_id"]!.GetValue<int>() > 0
            && resource["runtime_description"]!.GetValue<string>() == ".NET 10.0.12", "Consumer discovered facts differ.");
        string instance = resource["instance_id"]!.GetValue<string>();
        Require(Guid.TryParseExact(instance, "D", out Guid uuid) && uuid != Guid.Empty && uuid.ToString("D") == instance, "Consumer instance identity differs.");
        foreach (string key in new[] { "initialized_at", "process_started_at" })
        {
            if (resource[key] is null && key == "process_started_at") { continue; }
            Require(DateTimeOffset.TryParseExact(resource[key]!.GetValue<string>(), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out _), "Consumer discovery timestamp differs.");
        }
        var allowed = new HashSet<string>(new[] { "build_version", "instance_id", "initialized_at", "environment_kind", "os_family", "process_id", "process_started_at", "host_name", "os_description", "os_architecture", "process_architecture", "runtime_description" }, StringComparer.Ordinal);
        Require(resource.All(pair => allowed.Contains(pair.Key)), "Consumer resource includes non-facts.");
        if (!row["physical"]!.GetValue<bool>() || physicalBytes)
        {
            string selected = expected["settings"]!["stream"]!.GetValue<string>() == "StandardError" ? "stderr" : "stdout", other = selected == "stderr" ? "stdout" : "stderr";
            Require(actual[other]!.GetValue<string>() == "", "Unselected consumer stream has records.");
            CheckLines(catalog, row, actual[selected]!.GetValue<string>(), "console", resource);
        }
        CheckLines(catalog, row, actual["file"]!.GetValue<string>(), "file", resource);
    }
    private static void CheckLines(JsonObject catalog, JsonObject row, string token, string destination, JsonObject resource)
    {
        byte[] bytes = Convert.FromBase64String(token); int cap = row["expected"]!["settings"]!["maxEntryBytes"]!.GetValue<int>();
        Require(bytes.Length == 0 || bytes[^1] == 10, "Consumer entry framing/cap differs.");
        string text = new UTF8Encoding(false, true).GetString(bytes); string[] lines = text.Length == 0 ? [] : text[..^1].Split('\n');
        JsonArray events = row["expected"]![destination + "Events"]!.AsArray(); Require(lines.Length == events.Count, "Consumer record count differs.");
        for (int index = 0; index < lines.Length; index++)
        {
            byte[] line = Encoding.UTF8.GetBytes(lines[index] + "\n");
            Require(line.Length <= cap && !line.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) && !line.Contains((byte)13) && !line.Contains((byte)27), "Consumer entry framing/cap differs.");
            JsonObject wanted = catalog["baseRecord"]!.DeepClone().AsObject(); JsonObject entry = events[index]!.AsObject(); int id = entry["id"]!.GetValue<int>();
            wanted["status"] = entry["level"]!.DeepClone(); wanted["message"] = "consumer-" + id.ToString(CultureInfo.InvariantCulture) + " caf\u00e9 \U0001F642\nnext";
            if (row["mode"]!.GetValue<string>() == "pressure") { wanted["message"] = wanted["message"]!.GetValue<string>() + new string('p', 2048); }
            wanted["resource"] = resource.DeepClone(); wanted["attributes"] = new JsonObject { ["n"] = id };
            if (destination == "file" || row["expected"]!["settings"]!["format"]!.GetValue<string>() == "JsonLines")
            {
                Require(JsonNode.DeepEquals(JsonNode.Parse(line), wanted), "Consumer JSONL facts differ.");
            }
            else
            {
                Dictionary<string, JsonElement> fields = HumanRenderingExpectations.Read(line, cap);
                var display = new JsonObject();
                foreach ((string key, string json) in new[] { ("timestamp", "timestamp"), ("severity", "status"), ("component", "component"), ("event_name", "event_name"), ("message", "message") })
                { display[key] = wanted[json]!.DeepClone(); }
                if (row["expected"]!["settings"]!["detail"]!.GetValue<string>() == "Verbose")
                {
                    foreach (string key in new[] { "service", "env", "version", "operation_id", "message_template", "attributes" }) { display[key] = wanted[key]!.DeepClone(); }
                    foreach (var pair in resource) { display["resource." + pair.Key] = pair.Value!.DeepClone(); }
                }
                Require(fields.Count == display.Count && display.All(pair => fields.TryGetValue(pair.Key, out JsonElement found)
                    && JsonNode.DeepEquals(JsonNode.Parse(found.GetRawText()), pair.Value)), "Consumer human facts differ.");
            }
        }
    }
    internal static void Damage(JsonObject actual, string fault)
    {
        switch (fault)
        {
            case "settings": actual["settings"]!["format"] = "Human"; break;
            case "outcome": actual["results"]![0]!["outcome"] = "Filtered"; break;
            case "factory": actual["factoryCalls"]![0] = 0; break;
            case "clock": actual["clockCalls"] = 99; break;
            case "final": actual["final"]!["outputs"]![0]!["abandoned"] = "99"; break;
            case "checkpoint": actual["checkpoints"]![0]!["consoleWorkerCompleted"] = true; break;
            case "dispose": actual["borrowedDisposes"] = 1; break;
            case "application": actual["afterApplication"] = "lost"; break;
            case "route": (actual["stdout"], actual["stderr"]) = (actual["stderr"]!.DeepClone(), actual["stdout"]!.DeepClone()); break;
            case "identity": ChangeBytes(text => text.Replace("Consumer.App", "Wrong.App", StringComparison.Ordinal)); break;
            case "resource": ChangeBytes(text => text.Replace(actual["resource"]!["instance_id"]!.GetValue<string>(), "22222222-2222-2222-2222-222222222222", StringComparison.Ordinal)); break;
            case "bom": actual["stderr"] = Convert.ToBase64String(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Convert.FromBase64String(actual["stderr"]!.GetValue<string>())).ToArray()); break;
            case "records": ChangeBytes(text => text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..]); break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        void ChangeBytes(Func<string, string> change) { actual["stderr"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(change(Encoding.UTF8.GetString(Convert.FromBase64String(actual["stderr"]!.GetValue<string>()))))); }
    }
}
