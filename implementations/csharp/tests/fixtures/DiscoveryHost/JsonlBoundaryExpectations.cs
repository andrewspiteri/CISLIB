using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class JsonlBoundaryExpectations
{
    internal static void Validate(string mode, JsonObject actual)
    {
        bool identity = mode is "missing-inventory" or "wrong-format" or "wrong-encoding";
        bool partial = mode is "missing-lf" or "partial-json" or "partial-utf8" or "repeat" or "with-retained" or "collision-file" or "collision-directory" or "retention-full" or "identity-exhausted";
        bool recovery = mode is "missing-lf" or "partial-json" or "partial-utf8" or "repeat" or "with-retained";
        bool success = recovery || mode is "empty" or "object" or "unicode" or "two-records" or "chunk-boundary";
        string? error = mode switch
        {
            "missing-inventory" => "FileInventoryMissing",
            "wrong-format" or "wrong-encoding" => "FileInventoryIncompatible",
            "collision-file" or "collision-directory" => "FileRecoveryCollision",
            "retention-full" => "FileRetentionLimitExceeded",
            "identity-exhausted" => "FileRecoveryIdentityExhausted",
            _ => success ? null : "FileRecordBoundaryInvalid"
        };
        Require(actual["mode"]!.GetValue<string>() == mode && actual["inspectionReleased"]!.GetValue<bool>() && actual["finalReleased"]!.GetValue<bool>(), "actual inspection/release");
        Require(actual["inspected"]!.GetValue<int>() == (identity ? 0 : 1), "identity before inspection");
        Require(actual["inspectionError"]?.GetValue<string>() == (identity ? error : null) && actual["startupError"]?.GetValue<string>() == error, "safe failure");
        byte[] before = Bytes(actual, "before"), after = Bytes(actual, "after");
        if (identity)
        { Require(actual["status"] is null && actual["length"] is null && actual["originalPosition"] is null && actual["restoredPosition"] is null, "uninspected identity"); }
        else
        {
            string status = mode == "empty" ? "Empty" : partial ? "Partial" : success ? "Complete" : "Invalid";
            Require(actual["status"]!.GetValue<string>() == status && actual["length"]!.GetValue<long>() == before.LongLength, "boundary status/length");
            Require(actual["originalPosition"]!.GetValue<long>() == (before.Length == 0 ? 0 : 3) && JsonNode.DeepEquals(actual["originalPosition"], actual["restoredPosition"]), "stream position");
        }
        JsonObject expected = actual["preimage"]!.DeepClone().AsObject(), filesystem = actual["filesystem"]!.AsObject();
        string active = "\u03a9-\U0001f642.jsonl", metadata = active + ".cis.inventory.json";
        Require(expected[active]!.GetValue<string>() == actual["before"]!.GetValue<string>(), "physical original");
        Require(Encoding.UTF8.GetString(Bytes(actual, "neighbor")) == "untouched neighbor\n", "neighbor preservation");
        if (!success)
        {
            Require(actual["outcome"] is null && actual["written"] is null && actual["recovered"] is null && actual["heldRecovered"] is null && actual["restartRecovered"] is null && actual["phases"]!.AsArray().Count == 0 && before.AsSpan().SequenceEqual(after), "failed startup preserves bytes");
            Require(JsonNode.DeepEquals(actual["inventoryBefore"], actual["inventoryAfter"]), "inventory unchanged");
        }
        else
        {
            Require(actual["outcome"]!.GetValue<string>() == "Accepted" && actual["written"]!.GetValue<long>() == 1 && actual["recovered"]!.GetValue<bool>() == recovery && actual["heldRecovered"]!.GetValue<bool>() == recovery && !actual["restartRecovered"]!.GetValue<bool>(), "actual append/recovery diagnostics");
            byte[] appended = recovery ? after : after[before.Length..];
            if (!recovery) { Require(after.AsSpan().StartsWith(before), "intact append"); }
            Record(appended, actual["pid"]!.GetValue<int>());
            JsonArray phases = actual["phases"]!.AsArray();
            Require(phases.Count == (mode == "repeat" ? 2 : 1), "creation phases");
            foreach (JsonNode? item in phases)
            {
                JsonObject phase = item!.AsObject();
                Require(phase["recovered"]!.GetValue<bool>() == recovery && phase["heldRecovered"]!.GetValue<bool>() == recovery && phase["heldAccepted"]!.GetValue<long>() == 0 && phase["heldFailed"]!.GetValue<long>() == 0 && phase["heldFault"] is null, "owned startup recovery");
                Require(phase["state"]!.GetValue<string>() == "Stopped" && phase["accepted"]!.GetValue<long>() == 1 && phase["written"]!.GetValue<long>() == 1 && phase["failed"]!.GetValue<long>() == 0 && phase["abandoned"]!.GetValue<long>() == 0 && phase["pending"]!.GetValue<long>() == 0 && phase["fault"] is null && phase["faultAt"] is null, "current delivery accounting");
            }
            if (recovery)
            {
                JsonObject old = JsonNode.Parse(Bytes(actual, "inventoryBefore"))!.AsObject(), next = old.DeepClone().AsObject();
                JsonArray entries = next["segments"]!.AsArray();
                long id = entries[^1]!["id"]!.GetValue<long>();
                string group = old["groupId"]!.GetValue<string>();
                string damaged = ".cis-log-" + group + "-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".damaged";
                expected[damaged] = actual["before"]!.DeepClone();
                entries[^1]!["fileName"] = damaged;
                entries[^1]!["state"] = "damaged";
                entries.Add(new JsonObject { ["id"] = id + 1, ["fileName"] = active, ["state"] = "active" });
                if (mode == "repeat")
                {
                    byte[] first = Bytes(actual, "firstActive");
                    Record(first, actual["pid"]!.GetValue<int>());
                    byte[] interrupted = [.. first, .. Encoding.UTF8.GetBytes("{\"again\":\""), 0xf0, 0x9f];
                    Require(Bytes(actual, "repeatBefore").AsSpan().SequenceEqual(interrupted), "second interrupted preimage");
                    string second = ".cis-log-" + group + "-" + (id + 1).ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".damaged";
                    expected[second] = actual["repeatBefore"]!.DeepClone();
                    entries[^1]!["fileName"] = second;
                    entries[^1]!["state"] = "damaged";
                    entries.Add(new JsonObject { ["id"] = id + 2, ["fileName"] = active, ["state"] = "active" });
                }
                Require(JsonNode.DeepEquals(next, JsonNode.Parse(Bytes(actual, "inventoryAfter"))), "persistent damaged inventory");
                expected[metadata] = actual["inventoryAfter"]!.DeepClone();
            }
            else { Require(JsonNode.DeepEquals(actual["inventoryBefore"], actual["inventoryAfter"]), "inventory unchanged"); }
            expected[active] = actual["after"]!.DeepClone();
        }
        Require(JsonNode.DeepEquals(expected, filesystem), "exact physical transition");
        string[] names = filesystem.Select(x => x.Key.Split('/')[0]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(actual["names"]!.AsArray().Select(x => x!.GetValue<string>()).SequenceEqual(names), "exact membership");
    }
    private static byte[] Bytes(JsonObject value, string key) => Convert.FromBase64String(value[key]!.GetValue<string>());
    private static void Record(byte[] line, int pid)
    {
        Require(line.Length > 0 && line[^1] == 10 && line.AsSpan().Count((byte)10) == 1 && !line.AsSpan().Contains((byte)13), "complete appended record");
        JsonObject record = JsonNode.Parse(line)!.AsObject();
        Require(record["event_name"]!.GetValue<string>() == "boundary.append" && record["component"]!.GetValue<string>() == "Boundary" && record["message"]!.GetValue<string>() == "\u03a9 \U0001f642" && record["attributes"]!["producer_pid"]!.GetValue<long>() == pid && record["truncated"] is null, "literal append");
    }
    private static void Require(bool value, string reason)
    { if (!value) { throw new InvalidDataException(reason); } }
}
