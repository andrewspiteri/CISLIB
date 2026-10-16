using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class LifecycleReportChecks
{
    internal static void Require(bool condition, string reason)
    {
        if (!condition) { throw new InvalidOperationException(reason); }
    }

    internal static long Number(JsonNode? value)
    {
        Require(value is JsonValue token && token.TryGetValue<string>(out _), "Noncanonical lifecycle counter.");
        string text = (string)value!;
        Require(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number >= 0 && number.ToString(CultureInfo.InvariantCulture) == text, "Noncanonical lifecycle counter.");
        return number;
    }

    internal static void Identity(string mode, JsonObject report, int? processId)
    {
        Require((string?)report["mode"] == mode && (string?)report["runtime"] == ".NET 10.0.12", "Lifecycle mode/runtime differs.");
        Require(report["pid"] is JsonValue value && value.TryGetValue<int>(out int pid) && pid > 0 && (!processId.HasValue || pid == processId), "Actual child PID differs.");
    }

    internal static void Result(JsonObject result, bool globalCounts)
    {
        string[] fields = globalCounts ? ["completion", "succeeded", "loss", "captured", "observed", "invalid", "truncated", "outputs"]
            : ["completion", "succeeded", "loss", "captured", "observed", "outputs"];
        Require(result.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(fields), "Lifecycle result shape differs.");
        foreach (string key in new[] { "captured", "observed" })
        {
            Require(DateTimeOffset.TryParseExact((string?)result[key], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset stamp) && stamp.Offset == TimeSpan.Zero, "Lifecycle UTC label differs.");
        }
        foreach (JsonNode? output in result["outputs"]!.AsArray())
        {
            Require(Number(output!["boundary"]) == (decimal)Number(output["written"]) + Number(output["failed"]) + Number(output["abandoned"]) + Number(output["pending"]), "Lifecycle boundary partition differs.");
        }
    }

    internal static JsonObject[] Records(JsonNode? encoded, int pid)
    {
        byte[] bytes = Convert.FromBase64String((string)encoded!);
        Require(!bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) && !bytes.Contains((byte)13) && (bytes.Length == 0 || bytes[^1] == 10), "Lifecycle record framing differs.");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        string[] lines = bytes.Length == 0 ? [] : text[..^1].Split('\n');
        return lines.Select(line =>
        {
            Require(Encoding.UTF8.GetByteCount(line) + 1 <= 16384, "Lifecycle record byte cap differs.");
            JsonObject row = JsonNode.Parse(line)!.AsObject();
            Require(row["attributes"]!["producer_pid"]!.GetValue<int>() == pid && (string?)row["message"] == "\u03a9 \U0001F642", "Lifecycle original record/PID differs.");
            return row;
        }).ToArray();
    }

    internal static void Names(JsonArray values, string[] names) =>
        Require(values.Select(value => (string)value!["name"]!).SequenceEqual(names), "Lifecycle output set/order differs.");
}
