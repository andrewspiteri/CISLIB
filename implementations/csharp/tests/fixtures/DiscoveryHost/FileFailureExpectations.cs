using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileFailureExpectations
{
    internal static void Validate(string mode, JsonObject actual)
    {
        bool render = mode.StartsWith("render", StringComparison.Ordinal); string code = render ? "RenderTooLarge" : "RetentionDeleteFailed";
        Require((string?)actual["mode"] == mode && (string?)actual["composition"] == (render ? "trusted-internal-small-segment" : "configured-runtime"), "composition");
        Require(actual["segmentMaxBytes"]!.GetValue<int>() == (render ? 16 : 65536), "segment limit");
        int pid = actual["pid"]!.GetValue<int>();
        if (render)
        {
            byte[] line = Bytes(actual["attemptedFileBytes"]!); Require(line.Length > 16 && actual["writeAttempts"]!.GetValue<int>() == 1, "single oversized Human attempt");
            FileRecordChecks.Record(line, true, "Failure", "failure.0", "preserved", pid);
        }
        else { Require(actual["attemptedFileBytes"] is null && actual["writeAttempts"] is null, "configured writer composition"); }
        Require(JsonNode.DeepEquals(new JsonObject { ["code"] = code, ["message"] = "Logging file output failed (" + code + ").", ["inner"] = false, ["dataCount"] = 0 }, actual["error"]), "safe fixed error");
        Require(actual["leaseReleased"]!.GetValue<bool>() && actual["activeReleased"]!.GetValue<bool>(), "owned handle release");
        Require(actual["borrowedDisposes"]!.GetValue<int>() == 0 && actual["launches"]!.GetValue<int>() == 2 && actual["factories"]!.GetValue<int>() == 4 && (string?)actual["stopped"] == "Stopped", "lifecycle/borrowed");
        Require(JsonNode.DeepEquals(actual["before"], actual["after"]) && JsonNode.DeepEquals(actual["namesBefore"], actual["namesAfter"]), "no further growth/preimage");
        JsonArray results = actual["results"]!.AsArray(); Require(results.Count == 4, "result count");
        for (int index = 0; index < 4; index++)
        {
            JsonObject result = results[index]!.AsObject();
            Require(result.Count == 3 && (string?)result["outcome"] == (index == 3 ? "PartiallyAccepted" : "Accepted") && !result["truncated"]!.GetValue<bool>(), "write outcome");
            JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 2, "output fanout");
            for (int slot = 0; slot < 2; slot++)
            {
                bool failed = index == 3 && slot == 1;
                Require(JsonNode.DeepEquals(new JsonObject { ["name"] = slot == 0 ? "console" : "file", ["status"] = failed ? "Faulted" : "Accepted", ["sequence"] = failed ? null : index + 1 }, outputs[slot]), "admission tickets");
            }
        }
        foreach (string phase in new[] { "fault", "final" })
        {
            bool final = phase == "final"; JsonObject diagnostics = actual[phase]!.AsObject();
            Require((string?)diagnostics["state"] == (final ? "Stopped" : "Running"), "runtime state");
            Require(diagnostics["invalid"]!.GetValue<long>() == 0 && diagnostics["truncated"]!.GetValue<long>() == 0, "loss counters");
            JsonArray outputs = diagnostics["outputs"]!.AsArray(); Require(outputs.Count == 2, "output count");
            JsonObject file = outputs[1]!.AsObject(), console = outputs[0]!.AsObject();
            Require((string?)file["name"] == "file" && (string?)file["state"] == (final ? "Stopped" : "Faulted") && (string?)file["faultCode"] == code, "terminal fault code");
            DateTimeOffset at = DateTimeOffset.ParseExact((string)file["faultAt"]!, "O", CultureInfo.InvariantCulture); Require(at.Offset == TimeSpan.Zero, "fault UTC");
            var wantedFile = new JsonObject { ["name"] = "file", ["state"] = final ? "Stopped" : "Faulted", ["accepted"] = 3, ["written"] = 0, ["failed"] = 1, ["abandoned"] = 2, ["pending"] = 0, ["unavailable"] = final ? 1 : 0, ["faultCode"] = code, ["faultAt"] = file["faultAt"]!.DeepClone() };
            Require(JsonNode.DeepEquals(wantedFile, file), "file loss accounting");
            Require((string?)console["name"] == "console" && (string?)console["state"] == (final ? "Stopped" : "Running") && console["faultCode"] is null && console["faultAt"] is null, "healthy console state");
            long written = console["written"]!.GetValue<long>(), pending = console["pending"]!.GetValue<long>();
            Require(final ? written == 4 && pending == 0 : written >= 0 && pending >= 0 && written + pending == 3, "console progress");
            var wantedConsole = new JsonObject { ["name"] = "console", ["state"] = final ? "Stopped" : "Running", ["accepted"] = final ? 4 : 3, ["written"] = written, ["failed"] = 0, ["abandoned"] = 0, ["pending"] = pending, ["unavailable"] = 0, ["faultCode"] = null, ["faultAt"] = null };
            Require(JsonNode.DeepEquals(wantedConsole, console), "console progress");
        }
        Require(JsonNode.DeepEquals(actual["fault"]!["outputs"]![1]!["faultAt"], actual["final"]!["outputs"]![1]!["faultAt"]), "retained fault");
        byte[] raw = Bytes(actual["consoleBytes"]!); string[] lines = new UTF8Encoding(false, true).GetString(raw).Split('\n');
        Require(lines.Length == 5 && lines[^1] == "", "console record count");
        for (int index = 0; index < 4; index++) { FileRecordChecks.Record(Encoding.UTF8.GetBytes(lines[index] + "\n"), false, "Failure", "failure." + index, "preserved", pid); }
        Preimages(mode, actual);
    }

    private static void Preimages(string mode, JsonObject actual)
    {
        const string active = "logs/Ω-🙂.log"; string metadata = active + ".cis.inventory.json";
        bool empty = mode == "render-empty", render = mode.StartsWith("render", StringComparison.Ordinal), human = render || mode.StartsWith("delete-human", StringComparison.Ordinal);
        JsonObject before = actual["before"]!.AsObject(), inventory = JsonNode.Parse(Bytes(before[metadata]!))!.AsObject();
        string group = (string)inventory["groupId"]!; Require(Guid.TryParseExact(group, "N", out Guid id) && id != Guid.Empty && id.ToString("N") == group, "group identity");
        string state = mode.Contains("-damaged-", StringComparison.Ordinal) ? "damaged" : "closed", oldest = ".cis-log-" + group + "-00000000000000000001." + state;
        var segments = new JsonArray();
        if (!empty) { segments.Add(new JsonObject { ["id"] = 1, ["fileName"] = oldest, ["state"] = state }); }
        segments.Add(new JsonObject { ["id"] = empty ? 1 : 2, ["fileName"] = "Ω-🙂.log", ["state"] = "active" });
        var wanted = new JsonObject { ["schema"] = 1, ["groupId"] = group, ["format"] = human ? "human" : "jsonl", ["encoding"] = "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = segments };
        Require(JsonNode.DeepEquals(wanted, inventory), "literal failure inventory");
        string seed = empty ? "" : render ? "seed=\"prior\"\n" : human ? "seed=\"" + new string('a', 65528) + "\"\n" : "{\"seed\":\"" + new string('a', 65524) + "\"}\n";
        Require(Bytes(before[active]!).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(seed)), "literal failure active preimage");
        string[] neighbors = ["archive/keep.log", "logs/other.log", "logs/.cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed", "logs/.cis-log-" + group + "-00000000000000000050.closed"];
        var keys = new HashSet<string>(neighbors, StringComparer.Ordinal) { active, metadata };
        foreach (string name in neighbors) { Require(Bytes(before[name]!).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes("untouched neighbor\n")), "literal failure neighbor"); }
        if (!empty)
        {
            string victim = "logs/" + oldest + (mode.EndsWith("directory", StringComparison.Ordinal) ? "/keep.txt" : ""); keys.Add(victim);
            string contents = mode.EndsWith("directory", StringComparison.Ordinal) ? "blocked deletion\n" : human ? "retained=true\n" : "{\"retained\":true}\n";
            Require(Bytes(before[victim]!).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(contents)), "literal failure victim");
        }
        Require(keys.SetEquals(before.Select(p => p.Key)), "literal failure preimage membership");
        string[] names = keys.Where(k => k.StartsWith("logs/", StringComparison.Ordinal)).Select(k => k[5..].Split('/')[0]).Append("Ω-🙂.log.cis.lock").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(actual["namesBefore"]!.AsArray().Select(n => (string)n!).SequenceEqual(names), "literal failure physical membership");
    }

    private static byte[] Bytes(JsonNode value) => Convert.FromBase64String((string)value!);
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
}
