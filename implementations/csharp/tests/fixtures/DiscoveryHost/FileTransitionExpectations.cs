using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTransitionExpectations
{
    internal static readonly string[] Modes = ["rotation-intent", "rotation-renamed", "rotation-published", "rotation-created", "recovery-intent", "recovery-renamed", "recovery-published", "recovery-created", "human-intent", "human-renamed", "human-published", "human-created", "ambiguous-both", "mismatch-bytes", "nonempty-fresh", "retirement-no-intent"];

    internal static void Validate(string mode, JsonObject report)
    {
        Require((string?)report["mode"] == mode && (string?)report["composition"] == "staged-physical-transition-to-configured-runtime", "composition");
        Require(report["leaseReleased"]!.GetValue<bool>() && report["dataReleased"]!.GetValue<bool>(), "in-process release");
        bool recovery = mode.StartsWith("recovery", StringComparison.Ordinal), human = mode.StartsWith("human", StringComparison.Ordinal);
        bool failure = mode is "ambiguous-both" or "mismatch-bytes" or "nonempty-fresh" or "retirement-no-intent";
        const string active = "logs/Ω-🙂.log"; string metadata = active + ".cis.inventory.json", pending = active + ".cis.transition.json";
        string group = (string)report["group"]!; Require(Guid.TryParseExact(group, "N", out Guid id) && id != Guid.Empty && id.ToString("N") == group, "group identity");
        string Name(int value, string state) => ".cis-log-" + group + "-" + value.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + "." + state;
        JsonObject Segment(int value, string name, string state) => new() { ["id"] = value, ["fileName"] = name, ["state"] = state };
        JsonObject Inventory(JsonArray entries) => new() { ["schema"] = 1, ["groupId"] = group, ["format"] = human ? "human" : "jsonl", ["encoding"] = "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = entries };
        int oldId = recovery ? 2 : 3; string state = recovery ? "damaged" : "closed", archived = "logs/" + Name(oldId, state);
        var old = new JsonArray(Segment(1, Name(1, "damaged"), "damaged"));
        if (!recovery) { old.Add(Segment(2, Name(2, "closed"), "closed")); }
        old.Add(Segment(oldId, active[5..], "active"));
        JsonArray next = recovery ? new JsonArray(old[0]!.DeepClone()) : new JsonArray(old[1]!.DeepClone());
        next.Add(Segment(oldId, Name(oldId, state), state)); next.Add(Segment(oldId + 1, active[5..], "active"));
        JsonObject previousInventory = Inventory(old), nextInventory = Inventory(next);
        byte[] original = Encoding.UTF8.GetBytes(recovery ? "{\"earlier\":true}\n{\"partial\":" : human ? "earlier=true\n" : "{\"earlier\":true}\n");
        string stage = mode[(mode.IndexOf('-', StringComparison.Ordinal) + 1)..];
        if (mode == "mismatch-bytes") { stage = "renamed"; } else if (mode == "nonempty-fresh") { stage = "created"; }
        var expected = new JsonObject { ["neighbor.bin"] = Encode([0xff, 0, 0xfe]), ["logs/" + Name(50, "closed")] = Encode(Encoding.UTF8.GetBytes("unlisted neighbor\n")), [active + ".cis.lock"] = "" };
        expected["logs/" + Name(recovery ? 1 : 2, recovery ? "damaged" : "closed")] = Encode(Encoding.UTF8.GetBytes(recovery ? "older damage" : human ? "older=true\n" : "{\"older\":true}\n"));
        if (stage is "renamed" or "published" or "created") { expected[archived] = Encode(mode == "mismatch-bytes" ? Encoding.UTF8.GetBytes("{\"other\":true}\n") : original); }
        else { expected[active] = Encode(original); }
        if (mode == "ambiguous-both") { expected[archived] = Encode(original); }
        if (stage == "created") { expected[active] = Encode(mode == "nonempty-fresh" ? Encoding.UTF8.GetBytes("{\"fresh\":true}\n") : []); }
        JsonObject before = report["before"]!.AsObject(), after = report["after"]!.AsObject();
        Require(JsonNode.DeepEquals(stage is "published" or "created" ? nextInventory : previousInventory, JsonNode.Parse(Bytes(before[metadata]!))), "initial inventory");
        expected[metadata] = before[metadata]!.DeepClone();
        if (mode != "retirement-no-intent")
        {
            byte[] raw = Bytes(before[pending]!); Framing(raw);
            var retirements = new JsonArray();
            if (!recovery)
            {
                byte[] retired = Encoding.UTF8.GetBytes("older damage");
                retirements.Add(new JsonObject { ["id"] = 1, ["fileName"] = Name(1, "damaged"), ["bytes"] = retired.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(retired)) });
            }
            var intent = new JsonObject { ["schema"] = 2, ["recovery"] = recovery, ["retainedLimit"] = 2, ["originalBytes"] = original.Length, ["originalSha256"] = Convert.ToHexStringLower(SHA256.HashData(original)), ["previous"] = previousInventory.DeepClone(), ["next"] = nextInventory.DeepClone(), ["retirements"] = retirements };
            Require(JsonNode.DeepEquals(intent, JsonNode.Parse(raw)), "pending intent"); expected[pending] = before[pending]!.DeepClone();
        }
        Require(JsonNode.DeepEquals(expected, before), "literal stage preimage");
        if (failure)
        {
            string code = mode == "retirement-no-intent" ? "FileInventoryIncompatible" : "FileTransitionIncompatible";
            Require(JsonNode.DeepEquals(new JsonObject { ["code"] = code, ["output"] = "file", ["message"] = "Logging output 'file' could not initialize (" + code + ").", ["inner"] = false, ["dataCount"] = 0 }, report["error"]), "safe failure");
            Require(JsonNode.DeepEquals(before, after), "unchanged failure");
            Require(report["outcome"] is null && report["final"] is null && !report["recovered"]!.GetValue<bool>() && !report["restartRecovered"]!.GetValue<bool>(), "failed publication"); return;
        }
        Require(report["error"] is null && (string?)report["outcome"] == "Accepted", "successful admission");
        Require(report["recovered"]!.GetValue<bool>() == recovery && !report["restartRecovered"]!.GetValue<bool>() && report["final"]!["recovered"]!.GetValue<bool>() == recovery, "recovery diagnostics");
        var final = new JsonObject { ["state"] = "Stopped", ["accepted"] = 1, ["written"] = 1, ["failed"] = 0, ["abandoned"] = 0, ["pending"] = 0, ["recovered"] = recovery, ["fault"] = null };
        Require(JsonNode.DeepEquals(final, report["final"]), "delivery accounting");
        byte[] record = Bytes(after[active]!); FileRecordChecks.Record(record, human, "Transition", "transition.fresh", "Ω 🙂", report["pid"]!.GetValue<int>());
        byte[] published = Bytes(after[metadata]!); Framing(published); Require(JsonNode.DeepEquals(nextInventory, JsonNode.Parse(published)), "next inventory");
        expected.Remove(pending); expected[archived] = Encode(original); expected[active] = after[active]!.DeepClone(); expected[metadata] = after[metadata]!.DeepClone();
        Require(JsonNode.DeepEquals(expected, after), "exact physical transition");
    }

    private static void Framing(byte[] raw) => Require(raw.Length > 0 && raw[^1] == 10 && !raw.Contains((byte)13) && !raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), "metadata framing");
    private static byte[] Bytes(JsonNode value) => Convert.FromBase64String((string)value!);
    private static string Encode(byte[] value) => Convert.ToBase64String(value);
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
}
