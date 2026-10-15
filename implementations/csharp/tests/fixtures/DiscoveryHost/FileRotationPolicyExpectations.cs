using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileRotationPolicyExpectations
{
    internal static void Validate(string mode, JsonObject actual)
    {
        bool write = mode.StartsWith("write", StringComparison.Ordinal), limited = mode == "write-jsonl-os-limit", metadata = mode.StartsWith("metadata", StringComparison.Ordinal);
        Require(actual["mode"]!.GetValue<string>() == mode && actual["composition"]!.GetValue<string>() == (limited ? "configured-runtime-os-capacity" : write ? "controlled-write-with-real-first-record" : "configured-runtime"), "composition");
        Require(actual["directoryInitiallyMissing"]!.GetValue<bool>() && actual["leaseReleased"]!.GetValue<bool>() && actual["dataReleased"]!.GetValue<bool>(), "physical creation/release");
        Require(actual["borrowedDisposes"]!.GetValue<int>() == 0 && actual["factories"]!.GetValue<int>() == 4 && actual["launches"]!.GetValue<int>() == 2 && actual["stopped"]!.GetValue<string>() == "Stopped", "lifecycle");
        Require(limited ? actual["exceptionType"]!.GetValue<string>() == "ArgumentOutOfRangeException" : actual["exceptionType"]!.GetValue<string>() is "IOException" or "UnauthorizedAccessException", "expected IO failure");
        Require(metadata ? actual["restartError"]!.GetValue<string>() == "FileTransitionInitializationFailed" : actual["restartError"] is null, "safe restart");
        long written = write && !limited ? 1 : 0;
        foreach (string phase in new[] { "fault", "final" })
        {
            JsonObject d = actual[phase]!.AsObject(); Require(d["state"]!.GetValue<string>() == (phase == "fault" ? "Running" : "Stopped"), "runtime state");
            JsonObject file = d["outputs"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "file")!.AsObject(), console = d["outputs"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "console")!.AsObject();
            Require(file["state"]!.GetValue<string>() == (phase == "fault" ? "Faulted" : "Stopped") && file["faultCode"]!.GetValue<string>() == "WriteFailed" && file["faultAt"] is not null, "terminal fault");
            Require(file["accepted"]!.GetValue<long>() == 3 && file["written"]!.GetValue<long>() == written && file["failed"]!.GetValue<long>() == 1 && file["abandoned"]!.GetValue<long>() == 2 - written && file["pending"]!.GetValue<long>() == 0 && file["unavailable"]!.GetValue<long>() == (phase == "fault" ? 0 : 1), "file accounting");
            Require(console["faultCode"] is null && console["faultAt"] is null && console["failed"]!.GetValue<long>() == 0 && console["abandoned"]!.GetValue<long>() == 0 && console["unavailable"]!.GetValue<long>() == 0, "healthy console");
            Require(console["state"]!.GetValue<string>() == (phase == "fault" ? "Running" : "Stopped"), "healthy console state");
            if (phase == "final") { Require(console["accepted"]!.GetValue<long>() == 4 && console["written"]!.GetValue<long>() == 4 && console["pending"]!.GetValue<long>() == 0, "console progress"); }
            else { Require(console["accepted"]!.GetValue<long>() == 3 && console["written"]!.GetValue<long>() + console["pending"]!.GetValue<long>() == 3, "console prior boundary"); }
        }
        Require(JsonNode.DeepEquals(actual["fault"]!["outputs"]![1]!["faultAt"], actual["final"]!["outputs"]![1]!["faultAt"]), "retained fault");
        JsonArray results = actual["results"]!.AsArray(); Require(results.Count == 4, "result count");
        for (int index = 0; index < 4; index++)
        {
            JsonObject result = results[index]!.AsObject(); Require(result["outcome"]!.GetValue<string>() == (index == 3 ? "PartiallyAccepted" : "Accepted") && !result["truncated"]!.GetValue<bool>(), "outcome");
            JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 2, "output count");
            for (int slot = 0; slot < 2; slot++) { JsonObject o = outputs[slot]!.AsObject(); bool fault = index == 3 && slot == 1; Require(o["name"]!.GetValue<string>() == (slot == 0 ? "console" : "file") && o["status"]!.GetValue<string>() == (fault ? "Faulted" : "Accepted") && (fault ? o["sequence"] is null : o["sequence"]!.GetValue<long>() == index + 1), "tickets"); }
        }
        JsonObject expected = actual["before"]!.DeepClone().AsObject(); const string active = "logs/\u03a9-\U0001f642.log"; string closed = "logs/.cis-log-" + actual["group"]!.GetValue<string>() + "-00000000000000000002.closed", oldest = closed.Replace("00000000000000000002.closed", "00000000000000000001.closed", StringComparison.Ordinal);
        if (metadata)
        {
            byte[] retired = Convert.FromBase64String(expected[oldest]!.GetValue<string>());
            JsonNode seed = expected[active]!.DeepClone(); expected.Remove(active); expected.Remove(oldest); expected[closed] = seed;
            string prefix = active + ".cis.inventory.json.";
            Require(!actual["after"]!.AsObject().Any(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) && x.Key.EndsWith(".tmp", StringComparison.Ordinal)), "private inventory candidate");
            var next = new JsonObject { ["schema"] = 1, ["groupId"] = actual["group"]!.DeepClone(), ["format"] = mode.Contains("human", StringComparison.Ordinal) ? "human" : "jsonl", ["encoding"] = "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = new JsonArray(new JsonObject { ["id"] = 2, ["fileName"] = closed[5..], ["state"] = "closed" }, new JsonObject { ["id"] = 3, ["fileName"] = active[5..], ["state"] = "active" }) };
            string pending = active + ".cis.transition.json";
            byte[] pendingBytes = Convert.FromBase64String(actual["after"]![pending]!.GetValue<string>());
            JsonObject previous = next.DeepClone().AsObject(); previous["segments"] = new JsonArray(new JsonObject { ["id"] = 1, ["fileName"] = oldest[5..], ["state"] = "closed" }, new JsonObject { ["id"] = 2, ["fileName"] = active[5..], ["state"] = "active" });
            byte[] prior = Convert.FromBase64String(seed.GetValue<string>());
            var intent = new JsonObject
            {
                ["schema"] = 2,
                ["recovery"] = false,
                ["retainedLimit"] = 1,
                ["originalBytes"] = prior.Length,
                ["originalSha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(prior)),
                ["previous"] = previous,
                ["next"] = next,
                ["retirements"] = new JsonArray(new JsonObject { ["id"] = 1, ["fileName"] = oldest[5..], ["bytes"] = retired.Length, ["sha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(retired)) })
            };
            Require(pendingBytes.Length > 0 && pendingBytes[^1] == 10 && JsonNode.DeepEquals(intent, JsonNode.Parse(pendingBytes)), "transition intent");
            expected[pending] = actual["after"]![pending]!.DeepClone();
        }
        if (write && !limited) { Require(actual["controlledWriteAttempts"]!.GetValue<int>() == 2 && actual["firstRecordBytes"] is not null, "controlled attempts"); expected[active] = actual["firstRecordBytes"]!.DeepClone(); }
        else { Require(actual["controlledWriteAttempts"] is null && actual["firstRecordBytes"] is null, "configured writer"); }
        Require(JsonNode.DeepEquals(expected, actual["after"]), "exact filesystem transition");
        JsonArray names = actual["namesBefore"]!.DeepClone().AsArray();
        if (metadata) { string activeName = "\u03a9-\U0001f642.log", oldName = oldest[5..]; names = new JsonArray(names.Select(x => x!.GetValue<string>()).Where(x => x != activeName && x != oldName).Append(closed[5..]).Append(active[5..] + ".cis.transition.json").Order(StringComparer.Ordinal).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()); }
        Require(JsonNode.DeepEquals(names, actual["namesAfter"]), "exact membership");
        byte[] bytes = Convert.FromBase64String(actual["consoleBytes"]!.GetValue<string>()); Require(bytes.Length > 0 && bytes[^1] == 10 && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "console framing"); string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n'); Require(lines.Length == 5, "console record count");
        for (int index = 0; index < 4; index++) { JsonObject record = JsonNode.Parse(lines[index])!.AsObject(); Require(record["component"]!.GetValue<string>() == "Policy" && record["event_name"]!.GetValue<string>() == "policy." + index && record["message"]!.GetValue<string>() == "\u03a9 \U0001f642 e\u0301\nline" && record["attributes"]!["producer_pid"]!.GetValue<long>() == actual["pid"]!.GetValue<int>() && record["truncated"] is null, "literal console records"); }
    }
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
