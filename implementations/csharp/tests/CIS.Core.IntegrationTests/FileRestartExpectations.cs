using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;

namespace CIS.Tests.IntegrationTests;

// Independent literal reader for the held-producer restart matrix.
internal static class FileRestartExpectations
{
    internal const string Active = "logs/Ω-🙂.jsonl";
    internal static void Validate(string mode, JsonObject seed, JsonObject resume, JsonObject? probe)
    {
        Require((string?)seed["mode"] == mode && (string?)resume["mode"] == mode && (string?)seed["phase"] == "hold" && (string?)resume["phase"] == "resume"
            && (string?)seed["composition"] == "actual-core-seed-with-staged-physical-restart" && (string?)resume["composition"] == (string?)seed["composition"], "composition");
        int seedPid = seed["pid"]!.GetValue<int>(), resumePid = resume["pid"]!.GetValue<int>();
        Require(seedPid != resumePid && seed["seedWritten"]!.GetValue<long>() == 2 && seed["leaseHeld"]!.GetValue<bool>() && resume["leaseReleased"]!.GetValue<bool>(), "owned process/lease");
        string group = (string)seed["group"]!;
        Require(Guid.TryParseExact(group, "N", out Guid id) && id != Guid.Empty && id.ToString("N") == group, "group identity");
        byte[][] records = seed["seedRecords"]!.AsArray().Select(Bytes).ToArray(); Require(records.Length == 2, "seed count");
        for (int index = 0; index < 2; index++) { Record(records[index], "restart.seed." + index, seedPid); }
        bool rotation = mode.StartsWith("rotation", StringComparison.Ordinal) || mode is "unproven-gap" or "ambiguous-both", recovery = mode.StartsWith("recovery", StringComparison.Ordinal),
            staged = rotation || recovery, partial = mode.StartsWith("partial", StringComparison.Ordinal), failure = mode.StartsWith("collision", StringComparison.Ordinal) || mode is "unproven-gap" or "ambiguous-both";
        string Name(int number, string state) => ".cis-log-" + group + "-" + number.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + "." + state;
        JsonObject Member(int number, string state, bool active = false) => new() { ["id"] = number, ["fileName"] = active ? Active[5..] : Name(number, state), ["state"] = state };
        var old = staged ? new JsonArray(Member(1, "damaged"), Member(2, "closed"), Member(3, "active", true)) : new JsonArray(Member(1, "active", true));
        JsonArray next = rotation ? new(Member(2, "closed"), Member(3, "closed"), Member(4, "active", true))
            : recovery ? new(Member(1, "damaged"), Member(2, "closed"), Member(3, "damaged"), Member(4, "active", true))
            : partial ? new(Member(1, "damaged"), Member(2, "active", true)) : old.DeepClone().AsArray();
        JsonObject Inventory(JsonArray members) => new() { ["schema"] = 1, ["groupId"] = group, ["format"] = "jsonl", ["encoding"] = "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = members.DeepClone() };
        byte[] suffix = mode == "partial-utf8" || recovery ? [.. Encoding.UTF8.GetBytes("{\"partial\":\""), 0xf0, 0x9f] : Encoding.UTF8.GetBytes("{\"partial\":");
        byte[] original = staged ? recovery ? [.. records[1], .. suffix] : records[1] : [.. records[0], .. records[1], .. (partial || mode.StartsWith("collision", StringComparison.Ordinal) ? suffix : Array.Empty<byte>())];
        string metadata = Active + ".cis.inventory.json", pending = Active + ".cis.transition.json";
        var preimage = new JsonObject
        {
            ["neighbor.bin"] = Base([0xff, 0, 0xfe]),
            ["logs/" + Name(50, "closed")] = Base(records[0]),
            ["logs/.cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed"] = Base(records[1]),
            [metadata + ".unlisted.tmp"] = Base(Encoding.UTF8.GetBytes("unlisted candidate\n"))
        };
        string stage = mode.Contains('-') ? mode[(mode.IndexOf('-', StringComparison.Ordinal) + 1)..] : "";
        string archive = "logs/" + Name(staged ? 3 : 1, recovery || partial ? "damaged" : "closed");
        if (staged) { preimage["logs/" + Name(2, "closed")] = Base(records[0]); if (recovery) { preimage["logs/" + Name(1, "damaged")] = Base([0xff, 10]); } }
        preimage[stage is "renamed" or "published" or "created" ? archive : Active] = Base(original);
        if (stage == "created") { preimage[Active] = Base([]); }
        if (mode == "ambiguous-both") { preimage[archive] = Base(original); }
        if (mode.StartsWith("collision", StringComparison.Ordinal)) { preimage["logs/" + Name(1, "damaged") + (mode == "collision-directory" ? "/keep.txt" : "")] = Base(Encoding.UTF8.GetBytes("reserved collision\n")); }
        JsonObject files = seed["files"]!.AsObject(), after = resume["after"]!.AsObject();
        Require(JsonNode.DeepEquals(Inventory(stage is "published" or "created" ? next : old), JsonNode.Parse(Bytes(files[metadata]))), "initial inventory");
        preimage[metadata] = files[metadata]!.DeepClone();
        if (staged && mode != "unproven-gap")
        {
            byte[] raw = Bytes(files[pending]);
            JsonArray retirements = [];
            if (rotation) { byte[] retired = [0xff, 10]; retirements.Add(new JsonObject { ["id"] = 1, ["fileName"] = Name(1, "damaged"), ["bytes"] = retired.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(retired)) }); }
            var intent = new JsonObject
            {
                ["schema"] = 2,
                ["recovery"] = recovery,
                ["retainedLimit"] = recovery ? 3 : 2,
                ["originalBytes"] = original.Length,
                ["originalSha256"] = Convert.ToHexStringLower(SHA256.HashData(original)),
                ["previous"] = Inventory(old),
                ["next"] = Inventory(next),
                ["retirements"] = retirements
            };
            Require(Framed(raw) && JsonNode.DeepEquals(intent, JsonNode.Parse(raw)), "pending identity"); preimage[pending] = files[pending]!.DeepClone();
        }
        Require(JsonNode.DeepEquals(preimage, files), "literal stage preimage");
        Require(seed["names"]!.AsArray().Select(n => (string)n!).SequenceEqual(Names(files)), "stage membership");
        Require(JsonNode.DeepEquals(files, resume["before"]) && JsonNode.DeepEquals(seed["names"], resume["namesBefore"]), "cross-process preimage");
        if (probe is not null)
        {
            Require(JsonNode.DeepEquals(Error("FileOwnershipUnavailable"), probe["error"]) && probe["final"] is null && !probe["leaseReleased"]!.GetValue<bool>(), "held competing owner");
            Require(JsonNode.DeepEquals(files, probe["before"]) && JsonNode.DeepEquals(files, probe["after"]) && JsonNode.DeepEquals(seed["names"], probe["namesBefore"]) && JsonNode.DeepEquals(seed["names"], probe["namesAfter"]), "held probe preservation");
        }
        if (failure)
        {
            string code = mode.StartsWith("collision", StringComparison.Ordinal) ? "FileRecoveryCollision" : mode == "unproven-gap" ? "FileInventoryIncompatible" : "FileTransitionIncompatible";
            Require(JsonNode.DeepEquals(Error(code), resume["error"]), "safe failure");
            Require(JsonNode.DeepEquals(resume["before"], after) && JsonNode.DeepEquals(resume["namesBefore"], resume["namesAfter"]), "unchanged restart failure");
            Require(resume["final"] is null && resume["outcome"] is null, "failed publication"); return;
        }
        Require(resume["error"] is null && (string?)resume["outcome"] == "Accepted", "admission");
        Require(JsonNode.DeepEquals(new JsonObject { ["state"] = "Stopped", ["accepted"] = 1, ["written"] = 1, ["failed"] = 0, ["abandoned"] = 0, ["pending"] = 0, ["recovered"] = partial || recovery, ["fault"] = null }, resume["final"]), "current accounting");
        byte[] activeBytes = Bytes(after[Active]); byte[][] parts = Split(activeBytes);
        Require(parts.Length == (mode == "intact" ? 3 : 1), "fresh record count");
        if (mode == "intact") { Require(parts[0].AsSpan().SequenceEqual(records[0]) && parts[1].AsSpan().SequenceEqual(records[1]), "intact append preimage"); }
        Record(parts[^1], "restart.resume", resumePid);
        byte[] metadataBytes = Bytes(after[metadata]); Require(Framed(metadataBytes) && JsonNode.DeepEquals(Inventory(next), JsonNode.Parse(metadataBytes)), "next inventory");
        preimage.Remove(pending); preimage[metadata] = after[metadata]!.DeepClone(); preimage[Active] = after[Active]!.DeepClone();
        if (mode != "intact") { preimage[archive] = Base(original); }
        Require(JsonNode.DeepEquals(preimage, after) && resume["namesAfter"]!.AsArray().Select(n => (string)n!).SequenceEqual(Names(after)), "exact restart transition");
    }

    internal static string[] Names(JsonObject files) => files.SelectMany(p => p.Key.EndsWith("/keep.txt", StringComparison.Ordinal) ? new[] { p.Key, p.Key[..p.Key.LastIndexOf('/')] } : new[] { p.Key })
        .Append("logs").Append(Active + ".cis.lock").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    internal static byte[][] Split(byte[] bytes)
    {
        var result = new List<byte[]>(); int start = 0;
        for (int index = 0; index < bytes.Length; index++) { if (bytes[index] == 10) { result.Add(bytes[start..(index + 1)]); start = index + 1; } }
        Require(start == bytes.Length, "record framing"); return result.ToArray();
    }
    internal static void Record(byte[] raw, string name, int pid) { try { FileRecordChecks.Record(raw, false, "Restart", name, "Ω 🙂", pid); } catch (InvalidDataException error) { throw new InvalidDataException("actual record producer", error); } }
    private static bool Framed(byte[] bytes) => bytes.Length is > 0 and <= 2097152 && bytes[^1] == 10 && !bytes.Contains((byte)13) && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
    private static JsonObject Error(string code) => new() { ["code"] = code, ["output"] = "file", ["message"] = "Logging output 'file' could not initialize (" + code + ").", ["inner"] = false, ["dataCount"] = 0 };
    private static byte[] Bytes(JsonNode? value) => Convert.FromBase64String((string)value!);
    private static string Base(byte[] value) => Convert.ToBase64String(value);
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
}
