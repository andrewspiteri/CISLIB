using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerReader
{
    internal static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    internal static int I(JsonNode node, string key) => node[key]!.GetValue<int>();
    internal static bool B(JsonNode node, string key) => node[key]!.GetValue<bool>();
    internal static string? S(JsonNode node, string key) => (string?)node[key];
    internal static byte[] Bytes(JsonNode value) => Convert.FromBase64String((string)value!);
    internal static long N(JsonNode node, string key)
    {
        string text = (string)node[key]!;
        Require(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value >= 0 && value.ToString(CultureInfo.InvariantCulture) == text, "exact counter");
        return value;
    }
    internal static bool Strings(JsonNode value, IEnumerable<string> expected) => value.AsArray().Select(n => (string)n!).SequenceEqual(expected);
    internal static bool Counts(JsonNode node, params long[] expected) => new[] { "accepted", "written", "failed", "abandoned", "pending", "full", "unavailable", "filtered" }.Select(key => N(node, key)).SequenceEqual(expected);
    internal static bool Writer(JsonNode node, int writes, int flushes, int maximum) => I(node, "writes") == writes && I(node, "flushes") == flushes && I(node, "maximumIo") == maximum;
    internal static void Names(JsonNode encoded, int pid, IEnumerable<string> expected, string reason)
    {
        byte[] bytes = Bytes(encoded); Require(!bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) && (bytes.Length == 0 || bytes[^1] == 10) && !bytes.Contains((byte)13), "record framing");
        string text = new UTF8Encoding(false, true).GetString(bytes); var names = new List<string>();
        foreach (string line in bytes.Length == 0 ? [] : text[..^1].Split('\n'))
        {
            Require(line.Length > 0 && Encoding.UTF8.GetByteCount(line) + 1 <= 16384, "record bound");
            JsonObject record = JsonNode.Parse(line)!.AsObject();
            Require(JsonNode.DeepEquals(record["attributes"], new JsonObject { ["producer_pid"] = pid }), "wire PID");
            Require(S(record, "message") == "Ω 🙂" && S(record, "status") == "info" && !record.ContainsKey("truncated"), "actual event payload");
            Require(record["schema_version"]!.GetValue<int>() == 1 && S(record, "service") == "Console.App" && S(record, "env") == "test" && S(record, "version") == "1.2.3"
                && S(record["resource"]!, "instance_id") == "11111111-1111-1111-1111-111111111111", "protected timer record");
            Require(DateTimeOffset.TryParseExact(S(record, "timestamp"), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at) && at.Offset == TimeSpan.Zero, "record UTC");
            names.Add((string)record["event_name"]!);
        }
        Require(names.SequenceEqual(expected), reason);
    }
    internal static void Pid(JsonObject report, int? expected)
    { if (expected.HasValue) { Require(report["pid"]!.GetValue<int>() == expected.Value && expected.Value > 0, "actual child PID"); } }
    internal static void Utc(JsonNode diagnostic, string reason)
    { Require(DateTimeOffset.TryParse(S(diagnostic, "faultAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at) && at.Offset == TimeSpan.Zero, reason); }
}
