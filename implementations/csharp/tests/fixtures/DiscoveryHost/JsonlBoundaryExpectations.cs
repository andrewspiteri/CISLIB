using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class JsonlBoundaryExpectations
{
    internal static void Validate(string mode, JsonObject actual)
    {
        bool rejectedIdentity = mode is "missing-inventory" or "wrong-format" or "wrong-encoding", success = mode is "empty" or "object" or "unicode" or "two-records" or "chunk-boundary";
        string? error = mode == "missing-inventory" ? "FileInventoryMissing" : rejectedIdentity ? "FileInventoryIncompatible" : success ? null : "FileRecordBoundaryInvalid";
        Require(actual["mode"]!.GetValue<string>() == mode && actual["inspectionReleased"]!.GetValue<bool>() && actual["finalReleased"]!.GetValue<bool>(), "actual inspection/release");
        Require(actual["inspected"]!.GetValue<int>() == (rejectedIdentity ? 0 : 1), "identity before inspection");
        Require(actual["inspectionError"]?.GetValue<string>() == (rejectedIdentity ? error : null) && actual["startupError"]?.GetValue<string>() == error, "safe failure");
        byte[] before = Convert.FromBase64String(actual["before"]!.GetValue<string>()), after = Convert.FromBase64String(actual["after"]!.GetValue<string>());
        if (!rejectedIdentity)
        {
            string expected = mode == "empty" ? "Empty" : success ? "Complete" : mode is "missing-lf" or "partial-json" or "partial-utf8" ? "Partial" : "Invalid";
            Require(actual["status"]!.GetValue<string>() == expected && actual["length"]!.GetValue<long>() == before.LongLength, "boundary status/length"); Require(actual["originalPosition"]!.GetValue<long>() == (before.Length == 0 ? 0 : 3) && JsonNode.DeepEquals(actual["originalPosition"], actual["restoredPosition"]), "stream position");
        }
        else { Require(actual["status"] is null && actual["length"] is null && actual["originalPosition"] is null && actual["restoredPosition"] is null, "uninspected identity"); }
        Require(JsonNode.DeepEquals(actual["inventoryBefore"], actual["inventoryAfter"]) && Encoding.UTF8.GetString(Convert.FromBase64String(actual["neighbor"]!.GetValue<string>())) == "untouched neighbor\n", "inventory/neighbor preservation");
        if (success)
        {
            Require(actual["outcome"]!.GetValue<string>() == "Accepted" && actual["written"]!.GetValue<long>() == 1 && after.AsSpan().StartsWith(before), "actual append"); byte[] appended = after[before.Length..]; Require(appended.Length > 0 && appended[^1] == 10 && appended.AsSpan().Count((byte)10) == 1, "complete appended record"); JsonObject record = JsonNode.Parse(appended)!.AsObject();
            Require(record["event_name"]!.GetValue<string>() == "boundary.append" && record["component"]!.GetValue<string>() == "Boundary" && record["message"]!.GetValue<string>() == "\u03a9 \U0001f642" && record["attributes"]!["producer_pid"]!.GetValue<long>() == actual["pid"]!.GetValue<int>() && record["truncated"] is null, "literal append");
        }
        else { Require(actual["outcome"] is null && actual["written"] is null && before.AsSpan().SequenceEqual(after), "failed startup preserves bytes"); }
        string active = "\u03a9-\U0001f642.jsonl"; string[] names = [".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed", active, active + ".cis.lock", .. mode == "missing-inventory" ? Array.Empty<string>() : new[] { active + ".cis.inventory.json" }];
        Require(actual["names"]!.AsArray().Select(x => x!.GetValue<string>()).SequenceEqual(names.Order(StringComparer.Ordinal)), "exact group membership");
    }
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
}
