using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderMappingExpectations
{
    internal static readonly string[] Modes = ["levels", "names", "ids", "payload"];
    private static void Require(bool condition) { if (!condition) { throw new InvalidDataException("Provider mapping expectation failed."); } }
    internal static void Validate(string mode, JsonObject a)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        bool physical = a["physical"]!.GetValue<bool>(); Require(a["borrowedConsoleOpen"]!.GetValue<bool>() && a["leaseReleased"]!.GetValue<bool>() == physical && a["invalid"]!.GetValue<string>() == "0");
        int pairs = mode switch { "levels" => 6, "names" => 3, "ids" => 5, _ => 1 };
        Require(a["formatterCalls"]!.GetValue<int>() == pairs && a["noneFormatterCalls"]!.GetValue<int>() == 0 && a["formatterExceptionReceived"]!.GetValue<bool>() == (mode == "payload"));
        Require(a["result"]!["completion"]!.GetValue<string>() == "Completed" && a["result"]!["succeeded"]!.GetValue<bool>() && !a["result"]!["loss"]!.GetValue<bool>());
        JsonArray outputs = a["result"]!["outputs"]!.AsArray(); Require(outputs.Count == 1); JsonNode output = outputs[0]!;
        Require(output["name"]!.GetValue<string>() == (physical ? "file" : "console") && long.Parse(output["boundary"]!.GetValue<string>(), CultureInfo.InvariantCulture) == pairs * 2 && long.Parse(output["written"]!.GetValue<string>(), CultureInfo.InvariantCulture) == pairs * 2 && output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" && output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>()); Require(bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        JsonObject[] rows = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonNode.Parse(s)!.AsObject()).ToArray(); Require(rows.Length == pairs * 2);
        string[] statuses = mode == "levels" ? ["trace", "debug", "info", "warn", "error", "critical"] : Enumerable.Repeat(mode == "payload" ? "error" : "info", pairs).ToArray();
        int[] ids = mode switch { "levels" => [17, 17, 17, 17, 17, 17], "names" => [9, -4, 0], "ids" => [0, 1, -1, int.MinValue, int.MaxValue], _ => [42] };
        string[] categories = mode == "names" ? ["Map.First", "Map.Second", "Map.Empty"] : Enumerable.Repeat("Map." + (mode == "levels" ? "Levels" : mode == "ids" ? "Ids" : "Payload"), pairs).ToArray();
        string[] names = mode switch { "levels" => Enumerable.Repeat("level", pairs).ToArray(), "names" => ["named.event", "log.message", "log.message"], "ids" => Enumerable.Repeat("id", pairs).ToArray(), _ => ["payload"] };
        string[] messages = mode switch { "levels" => ["level 0", "level 1", "level 2", "level 3", "level 4", "level 5"], "names" => ["named", "absent", "empty"], "ids" => ["0", "1", "-1", "-2147483648", "2147483647"], _ => ["rendered {x} Ω 🙂"] };
        for (int i = 0; i < pairs; i++)
        {
            foreach (JsonObject row in rows.Skip(i * 2).Take(2))
            {
                Require(row["status"]!.GetValue<string>() == statuses[i] && row["component"]!.GetValue<string>() == categories[i] && row["event_name"]!.GetValue<string>() == names[i] && row["message"]!.GetValue<string>() == messages[i]);
                Require(row["attributes"]!.AsObject().Count == 1 && row["attributes"]!["event_id"]!.GetValue<int>() == ids[i]);
                if (mode == "payload") { Require(row["exception"]!["type"]!.GetValue<string>() == "System.InvalidOperationException" && row["exception"]!["message"]!.GetValue<string>() == "controlled mapping failure" && row["exception"]!.AsObject().Count == 2); }
                else { Require(row["exception"] is null); }
                row.Remove("timestamp");
            }
            Require(JsonNode.DeepEquals(rows[i * 2], rows[i * 2 + 1]));
        }
    }
}
