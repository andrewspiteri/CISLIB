using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class JsonlBoundaryExpectations
{
    internal static readonly string[] Modes = ["empty", "object", "unicode", "two-records", "chunk-boundary", "missing-lf", "partial-json", "partial-utf8", "invalid-json", "invalid-utf8", "array", "blank", "crlf", "bom", "missing-inventory", "wrong-format", "wrong-encoding", "repeat", "with-retained", "collision-file", "collision-directory", "retention-full", "identity-exhausted"];
    internal static void Validate(string mode, JsonObject actual)
    {
        bool identity = mode is "missing-inventory" or "wrong-format" or "wrong-encoding";
        bool partial = mode is "missing-lf" or "partial-json" or "partial-utf8" or "repeat" or "with-retained" or "collision-file" or "collision-directory" or "retention-full" or "identity-exhausted";
        bool recovery = mode is "missing-lf" or "partial-json" or "partial-utf8" or "repeat" or "with-retained" or "retention-full";
        bool success = recovery || mode is "empty" or "object" or "unicode" or "two-records" or "chunk-boundary";
        string? error = mode switch
        {
            "missing-inventory" => "FileInventoryMissing",
            "wrong-format" or "wrong-encoding" => "FileInventoryIncompatible",
            "collision-file" or "collision-directory" => "FileRecoveryCollision",
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
        JsonObject expected = LiteralPreimage(mode, actual), filesystem = actual["filesystem"]!.AsObject();
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
                if (mode == "retention-full")
                {
                    expected.Remove(entries[0]!["fileName"]!.GetValue<string>());
                    entries.RemoveAt(0);
                }
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
                byte[] published = Bytes(actual, "inventoryAfter"); Require(published.Length > 0 && published[^1] == 10 && !published.Contains((byte)13) && !published.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), "published inventory framing");
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
    private static JsonObject LiteralPreimage(string mode, JsonObject actual)
    {
        const string active = "Ω-🙂.jsonl", foreign = ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed";
        byte[] seed = mode switch
        {
            "repeat" or "with-retained" or "collision-file" or "collision-directory" or "retention-full" or "identity-exhausted" => Encoding.UTF8.GetBytes("{\"stage\":"),
            "empty" => [],
            "unicode" => Encoding.UTF8.GetBytes("{\"message\":\"Ω 🙂 é\"}\n"),
            "two-records" => Encoding.UTF8.GetBytes("{\"first\":true}\n{\"next\":true}\n"),
            "chunk-boundary" => Encoding.UTF8.GetBytes("{\"prior\":true}\n{\"message\":\"" + new string('x', 4081) + "\"}\n"),
            "missing-lf" => Encoding.UTF8.GetBytes("{\"a\":1}"),
            "partial-json" => Encoding.UTF8.GetBytes("{\"prior\":true}\n{\"message\":"),
            "partial-utf8" => [.. Encoding.UTF8.GetBytes("{\"prior\":true}\n{\"message\":\""), 0xf0, 0x9f],
            "invalid-json" => Encoding.UTF8.GetBytes("{\"a\":\n"),
            "invalid-utf8" => [.. Encoding.UTF8.GetBytes("{\"a\":\""), 0xff, .. Encoding.UTF8.GetBytes("\"}\n")],
            "array" => Encoding.UTF8.GetBytes("[]\n"),
            "blank" => [10],
            "crlf" => Encoding.UTF8.GetBytes("{}\r\n"),
            "bom" => [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("{}\n")],
            "wrong-format" or "wrong-encoding" => [0xff, 10],
            _ => Encoding.UTF8.GetBytes("{\"a\":1}\n")
        };
        Require(seed.AsSpan().SequenceEqual(Bytes(actual, "before")), "literal seed");
        var expected = new JsonObject { [active] = Convert.ToBase64String(seed), [active + ".cis.lock"] = "", [foreign] = Convert.ToBase64String(Encoding.UTF8.GetBytes("untouched neighbor\n")) };
        if (mode == "missing-inventory") { Require(actual["inventoryBefore"] is null, "missing inventory"); }
        else
        {
            JsonObject old = JsonNode.Parse(Bytes(actual, "inventoryBefore"))!.AsObject(); string group = (string)old["groupId"]!;
            Require(Guid.TryParseExact(group, "N", out Guid value) && value != Guid.Empty && value.ToString("N") == group, "persistent group");
            string Name(long id, string state) => ".cis-log-" + group + "-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + "." + state;
            JsonObject Segment(long id, string name, string state) => new() { ["id"] = id, ["fileName"] = name, ["state"] = state };
            var segments = new JsonArray();
            if (mode is "with-retained" or "retention-full") { string name = Name(1, "closed"); segments.Add(Segment(1, name, "closed")); expected[name] = Convert.ToBase64String(Encoding.UTF8.GetBytes("prior closed evidence\n")); }
            if (mode == "with-retained") { string name = Name(3, "damaged"); segments.Add(Segment(3, name, "damaged")); expected[name] = Convert.ToBase64String([0xff, 10]); }
            long id = mode == "with-retained" ? 7 : mode == "retention-full" ? 2 : mode == "identity-exhausted" ? long.MaxValue : 1;
            segments.Add(Segment(id, active, "active"));
            var inventory = new JsonObject { ["schema"] = 1, ["groupId"] = group, ["format"] = mode == "wrong-format" ? "human" : "jsonl", ["encoding"] = mode == "wrong-encoding" ? "utf-16" : "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = segments };
            Require(JsonNode.DeepEquals(inventory, old), "literal inventory identity"); expected[active + ".cis.inventory.json"] = actual["inventoryBefore"]!.DeepClone();
            if (mode is "collision-file" or "collision-directory") { expected[Name(1, "damaged") + (mode == "collision-directory" ? "/keep.txt" : "")] = Convert.ToBase64String(Encoding.UTF8.GetBytes("reserved name\n")); }
        }
        Require(JsonNode.DeepEquals(expected, actual["preimage"]), "literal physical preimage"); return expected;
    }

    private static byte[] Bytes(JsonObject value, string key) => Convert.FromBase64String(value[key]!.GetValue<string>());
    private static void Record(byte[] line, int pid) => FileRecordChecks.Record(line, false, "Boundary", "boundary.append", "Ω 🙂", pid);
    private static void Require(bool value, string reason)
    { if (!value) { throw new InvalidDataException(reason); } }
}
