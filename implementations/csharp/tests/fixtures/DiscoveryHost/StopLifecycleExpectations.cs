using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopLifecycleExpectations
{
    internal static readonly string[] Modes = ["normal", "normal-dual", "producer-stop-race", "pre-cancelled", "cancelled-joiners", "held-write", "held-flush", "late-failure", "kill-queued", "kill-partial", "kill-after-stop"];
    private static void Require(bool value) { if (!value) { throw new InvalidDataException("Lifecycle expectation failed."); } }
    private static long N(JsonNode? value) => long.Parse(value!.GetValue<string>(), CultureInfo.InvariantCulture);
    private static byte[] Bytes(JsonObject a, string key) => Convert.FromBase64String(a[key]!.GetValue<string>());
    private static JsonObject[] Records(byte[] bytes, int? pid)
    {
        Require(bytes.Length > 0 && bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        JsonObject[] records = lines.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        Require(records.All(x => x["message"]!.GetValue<string>() == "Ω 🙂" && (pid is null || x["attributes"]!["producer_pid"]!.GetValue<int>() == pid)));
        Require(bytes.SplitRecordsAreBounded()); return records;
    }
    private static bool SplitRecordsAreBounded(this byte[] bytes) => Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).All(s => Encoding.UTF8.GetByteCount(s) + 1 <= 16384);
    private static void Identity(string mode, JsonObject a) => Require(a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12" && a["pid"]!.GetValue<int>() > 0);
    internal static void Validate(string mode, JsonObject a)
    {
        Identity(mode, a); int pid = a["pid"]!.GetValue<int>(); byte[] seed = Bytes(a, "seed"); Require(Records(seed, pid).Single()["event_name"]!.GetValue<string>() == "seed");
        Require(a["sameTask"]!.GetValue<bool>() && a["immutable"]!.GetValue<bool>() && a["joiners"]!.GetValue<int>() == 64 && a["leaseReleased"]!.GetValue<bool>() && a["stoppedChecks"]!.GetValue<bool>());
        JsonObject result = a["result"]!.AsObject(); bool dual = mode == "normal-dual"; Require(result["outputs"]!.AsArray().Count == (dual ? 2 : 1)); JsonNode output = result["outputs"]!.AsArray()[^1]!; Require(output["name"]!.GetValue<string>() == "file");
        byte[] bytes = Bytes(a, "bytes"); Require(bytes.AsSpan().StartsWith(seed));
        if (a["kind"]!.GetValue<string>() == "public")
        {
            JsonArray receipts = a["receipts"]!.AsArray(); Require(receipts.Count > 0 && receipts.Select((x, i) => N(x!["ticket"]) == i + 1).All(x => x));
            JsonNode incoming = a["incoming"]!;
            Require(N(incoming["accepted"]) == receipts.Count && N(incoming["processed"]) == receipts.Count && N(incoming["failed"]) == 0 &&
                N(incoming["abandoned"]) == 0 && N(incoming["pending"]) == 0 && N(incoming["queueFull"]) == 0);
            Require(result["completion"]!.GetValue<string>() == "Completed" && result["succeeded"]!.GetValue<bool>() && !result["loss"]!.GetValue<bool>());
            Require(N(output["boundary"]) == receipts.Count && N(output["written"]) == receipts.Count && N(output["failed"]) == 0 && N(output["abandoned"]) == 0 && N(output["pending"]) == 0 && output["flush"]!.GetValue<string>() == "Succeeded");
            if (dual)
            {
                JsonNode console = result["outputs"]![0]!; Require(console["name"]!.GetValue<string>() == "console" && N(console["boundary"]) == receipts.Count && N(console["written"]) == receipts.Count && N(console["failed"]) == 0 && N(console["abandoned"]) == 0 && N(console["pending"]) == 0 && console["flush"]!.GetValue<string>() == "Succeeded");
            }
            JsonObject[] rows = Records(bytes, pid); Require(rows.Length == receipts.Count + 1);
            Require(rows.Skip(1).Select(x => x["event_name"]!.GetValue<string>()).SequenceEqual(receipts.Select(x => x!["event"]!.GetValue<string>())));
            Require(a["attempts"]!.GetValue<int>() == receipts.Count + a["stopped"]!.GetValue<int>());
            Require(a["attempts"]!.GetValue<int>() == (mode is "normal" or "normal-dual" ? 120 : mode == "pre-cancelled" ? 40 : 514));
            Require(a["cancelled"]!.GetValue<int>() == (mode == "pre-cancelled" ? 1 : 0) && a["racingStoppers"]!.GetValue<int>() == (mode == "producer-stop-race" ? 16 : 0));
            Require(a["capturingStopped"]!.GetValue<bool>() == (mode == "producer-stop-race"));
            if (mode == "producer-stop-race") { Require(a["producerStopped"]!.GetValue<int>() + a["producerAccepted"]!.GetValue<int>() == 512 && a["producerAccepted"]!.GetValue<int>() == receipts.Count - 1); Require(a["stopped"]!.GetValue<int>() >= 1 && rows.All(x => x["event_name"]!.GetValue<string>() != "capturing")); }
            else { Require(a["stopped"]!.GetValue<int>() == 0); }
            return;
        }
        Require(a["kind"]!.GetValue<string>() == "kernel" && a["heldLease"]!.GetValue<bool>()); bool flushing = mode == "held-flush", failure = mode == "late-failure";
        Require(result["completion"]!.GetValue<string>() == "TimedOut" && !result["succeeded"]!.GetValue<bool>() && result["loss"]!.GetValue<bool>() == !flushing);
        Require(N(output["boundary"]) == (flushing ? 3 : 6) && N(output["written"]) == (flushing ? 3 : 0) && N(output["failed"]) == 0 && N(output["abandoned"]) == (flushing ? 0 : 5) && N(output["pending"]) == (flushing ? 0 : 1) && output["flush"]!.GetValue<string>() == (flushing ? "Pending" : "Failed"));
        JsonNode live = a["live"]![0]!; Require(a["live"]!.AsArray().Count == 1 && N(live["accepted"]) == (flushing ? 3 : 6) && N(live["written"]) == (flushing ? 3 : failure ? 0 : 1) && N(live["failed"]) == (failure ? 1 : 0) && N(live["abandoned"]) == (flushing ? 0 : 5) && N(live["pending"]) == 0);
        Require(live["fault"]?.GetValue<string>() == (failure ? "WriteFailed" : null));
        JsonNode writer = a["writer"]!; Require(writer["writes"]!.GetValue<int>() == (flushing ? 3 : 1) && writer["flushes"]!.GetValue<int>() == (flushing ? 1 : 0) && writer["cancellations"]!.GetValue<int>() == 1 && writer["disposals"]!.GetValue<int>() == 1 && !writer["unsafeDispose"]!.GetValue<bool>() && !writer["inFlight"]!.GetValue<bool>());
        Require(a["cancelled"]!.GetValue<int>() == (mode == "cancelled-joiners" ? 64 : 0));
        Require(a["outputCancellationRequestedBeforeDeadline"]!.AsArray().Count == 2 && a["outputCancellationRequestedBeforeDeadline"]!.AsArray().All(x => !x!.GetValue<bool>()));
        Require(a["outputCancellationsBeforeDeadline"]!.AsArray().Count == 2 && a["outputCancellationsBeforeDeadline"]!.AsArray().All(x => x!.GetValue<int>() == 0));
        JsonNode timers = a["timers"]!; Require(timers["created"]!.GetValue<int>() == 1 && timers["disposed"]!.GetValue<int>() == 1 && timers["fires"]!.GetValue<int>() == 1 && timers["active"]!.GetValue<int>() == 0);
        byte[] pending = Bytes(a, "pendingBytes"), line = Bytes(a, "line"); Require(Records(line, pid).Single()["event_name"]!.GetValue<string>() == "held.0");
        if (flushing) { Require(pending.SequenceEqual(bytes) && Records(bytes, pid).Select(x => x["event_name"]!.GetValue<string>()).SequenceEqual(new[] { "seed", "held.0", "held.1", "held.2" })); }
        else { Require(pending.SequenceEqual(seed.Concat(line.Take(32)))); Require(bytes.SequenceEqual(failure ? pending : seed.Concat(line).ToArray())); }
    }
    internal static void PhysicalConsole(JsonObject report, string stdout)
    {
        const string marker = "application-after-stop\n"; Require(stdout.EndsWith(marker, StringComparison.Ordinal));
        byte[] actual = new UTF8Encoding(false, true).GetBytes(stdout[..^marker.Length]); byte[] seed = Bytes(report, "seed"), file = Bytes(report, "bytes");
        Require(actual.SequenceEqual(file.Skip(seed.Length))); _ = Records(actual, report["pid"]!.GetValue<int>());
    }
    internal static void Checkpoint(string mode, JsonObject a)
    {
        Identity(mode, a); Require(mode.StartsWith("kill-", StringComparison.Ordinal) && a["kind"]!.GetValue<string>() == "checkpoint" && a["heldLease"]!.GetValue<bool>());
        byte[] seed = Bytes(a, "seed"), line = Bytes(a, "line"); Require(Records(seed, a["pid"]!.GetValue<int>()).Single()["event_name"]!.GetValue<string>() == "seed");
        Require(Records(line, a["pid"]!.GetValue<int>()).Single()["event_name"]!.GetValue<string>() == "held.0");
        Require(Bytes(a, "bytes").SequenceEqual(mode == "kill-queued" ? seed : seed.Concat(line.Take(32)).ToArray()));
        JsonNode writer = a["writer"]!; Require(writer["writes"]!.GetValue<int>() == 1 && writer["flushes"]!.GetValue<int>() == 0 && writer["disposals"]!.GetValue<int>() == 0 && !writer["unsafeDispose"]!.GetValue<bool>() && writer["inFlight"]!.GetValue<bool>());
        Require(writer["cancellations"]!.GetValue<int>() == (mode == "kill-after-stop" ? 1 : 0));
        JsonNode live = a["live"]![0]!; Require(N(live["accepted"]) == 6 && N(live["written"]) == 0 && N(live["failed"]) == 0 && N(live["abandoned"]) == (mode == "kill-after-stop" ? 5 : 0) && N(live["pending"]) == (mode == "kill-after-stop" ? 1 : 6));
        if (mode == "kill-after-stop")
        { JsonNode stop = a["result"]!; Require(stop["completion"]!.GetValue<string>() == "TimedOut" && !stop["succeeded"]!.GetValue<bool>() && stop["loss"]!.GetValue<bool>() && N(stop["outputs"]![0]!["pending"]) == 1 && N(stop["outputs"]![0]!["abandoned"]) == 5); }
        else { Require(a["result"] is null); }
    }
    internal static void Restart(string mode, JsonObject checkpoint, JsonObject restarted)
    {
        Identity("restart", restarted); Require(restarted["pid"]!.GetValue<int>() != checkpoint["pid"]!.GetValue<int>());
        byte[] before = Bytes(checkpoint, "bytes"), bytes = Bytes(restarted, "bytes"); bool partial = mode != "kill-queued";
        Require(Bytes(restarted, "beforeBytes").SequenceEqual(before) && restarted["recovered"]!.GetValue<bool>() == partial && restarted["leaseReleased"]!.GetValue<bool>());
        Require(restarted["result"]!["succeeded"]!.GetValue<bool>() && restarted["result"]!["completion"]!.GetValue<string>() == "Completed" && !restarted["result"]!["loss"]!.GetValue<bool>());
        Require(restarted["result"]!["outputs"]!.AsArray().Count == 1); JsonNode output = restarted["result"]!["outputs"]![0]!;
        Require(output["name"]!.GetValue<string>() == "file" && N(output["boundary"]) == 1 && N(output["written"]) == 1 && N(output["failed"]) == 0 && N(output["abandoned"]) == 0 && N(output["pending"]) == 0 && output["flush"]!.GetValue<string>() == "Succeeded");
        if (partial) { Require(Bytes(restarted, "damagedBytes").SequenceEqual(before)); }
        else { Require(restarted["damagedBytes"] is null && bytes.AsSpan().StartsWith(before)); }
        JsonObject[] rows = Records(bytes, null); Require(rows.Length == (partial ? 1 : 2));
        Require(rows[^1]["event_name"]!.GetValue<string>() == "restarted" && rows[^1]["attributes"]!["producer_pid"]!.GetValue<int>() == restarted["pid"]!.GetValue<int>());
        Require(rows.All(x => !x["event_name"]!.GetValue<string>().StartsWith("held.", StringComparison.Ordinal)));
    }
}
