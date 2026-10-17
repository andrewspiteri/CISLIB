using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopLifecycleExpectations
{
    internal static readonly string[] Modes = ["normal", "normal-dual", "producer-stop-race", "pre-cancelled", "cancelled-joiners", "held-write", "held-flush", "late-failure", "kill-queued", "kill-partial", "kill-after-stop"];
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    private static long N(JsonNode? value)
    {
        string token = (string)value!;
        Require(long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long count) && count >= 0 && count.ToString(CultureInfo.InvariantCulture) == token, "canonical counter");
        return count;
    }
    private static byte[] Bytes(JsonObject a, string key) => Convert.FromBase64String(a[key]!.GetValue<string>());
    private static JsonObject[] Records(byte[] bytes, int? pid)
    {
        Require(bytes.Length > 0 && bytes[^1] == (byte)'\n' && !bytes.Contains((byte)13) && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "record framing");
        string[] lines = new UTF8Encoding(false, true).GetString(bytes)[..^1].Split('\n'); Require(lines.All(line => line.Length > 0), "record line shape");
        JsonObject[] records = lines.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        Require(records.All(x => x["message"]!.GetValue<string>() == "Ω 🙂" && (string?)x["status"] == "info" && x["attributes"]!["producer_pid"]!.GetValue<int>() > 0 && (pid is null || x["attributes"]!["producer_pid"]!.GetValue<int>() == pid)), "record identity");
        Require(bytes.SplitRecordsAreBounded(), "record byte cap"); return records;
    }
    private static bool SplitRecordsAreBounded(this byte[] bytes) => Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).All(s => Encoding.UTF8.GetByteCount(s) + 1 <= 16384);
    private static void Identity(string mode, JsonObject a, int? pid = null) => Require(a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12" && a["pid"]!.GetValue<int>() > 0 && (!pid.HasValue || a["pid"]!.GetValue<int>() == pid), "lifecycle identity");
    private static void StopShape(JsonObject result, bool dual)
    {
        string[] fields = ["completion", "succeeded", "loss", "captured", "observed", "outputs"];
        Require(result.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(fields), "stop result shape");
        Require(result["succeeded"] is JsonValue succeeded && succeeded.TryGetValue<bool>(out _) && result["loss"] is JsonValue loss && loss.TryGetValue<bool>(out _), "stop flag types");
        foreach (string key in new[] { "captured", "observed" })
        {
            Require(DateTimeOffset.TryParseExact((string?)result[key], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset stamp) && stamp.Offset == TimeSpan.Zero, "stop UTC labels");
        }
        Require(result["outputs"]!.AsArray().Select(output => (string)output!["name"]!).SequenceEqual(dual ? ["console", "file"] : ["file"]), "stop output names");
    }

    internal static void Validate(string mode, JsonObject a, int? processId = null)
    {
        Identity(mode, a, processId); int pid = a["pid"]!.GetValue<int>(); byte[] seed = Bytes(a, "seed"); Require(Records(seed, pid).Single()["event_name"]!.GetValue<string>() == "seed", "seed record");
        Require(a["sameTask"]!.GetValue<bool>() && a["immutable"]!.GetValue<bool>() && a["joiners"]!.GetValue<int>() == 64 && a["leaseReleased"]!.GetValue<bool>() && a["stoppedChecks"]!.GetValue<bool>(), "lifecycle completion identity");
        JsonObject result = a["result"]!.AsObject(); bool dual = mode == "normal-dual"; StopShape(result, dual); Require(result["outputs"]!.AsArray().Count == (dual ? 2 : 1), "stop output count"); JsonNode output = result["outputs"]!.AsArray()[^1]!; Require(output["name"]!.GetValue<string>() == "file", "file output name");
        byte[] bytes = Bytes(a, "bytes"); Require(bytes.AsSpan().StartsWith(seed), "seed prefix");
        if (a["kind"]!.GetValue<string>() == "public")
        {
            JsonArray receipts = a["receipts"]!.AsArray(); Require(receipts.Count > 0 && receipts.Select((x, i) => N(x!["ticket"]) == i + 1).All(x => x), "admission tickets");
            JsonNode incoming = a["incoming"]!;
            Require(N(incoming["accepted"]) == receipts.Count && N(incoming["processed"]) == receipts.Count && N(incoming["failed"]) == 0 &&
                N(incoming["abandoned"]) == 0 && N(incoming["pending"]) == 0 && N(incoming["queueFull"]) == 0, "incoming completion");
            Require(result["completion"]!.GetValue<string>() == "Completed" && result["succeeded"]!.GetValue<bool>() && !result["loss"]!.GetValue<bool>(), "public stop summary");
            Require(N(output["boundary"]) == receipts.Count && N(output["written"]) == receipts.Count && N(output["failed"]) == 0 && N(output["abandoned"]) == 0 && N(output["pending"]) == 0 && output["flush"]!.GetValue<string>() == "Succeeded", "public file dispositions");
            if (dual)
            {
                JsonNode console = result["outputs"]![0]!; Require(console["name"]!.GetValue<string>() == "console" && N(console["boundary"]) == receipts.Count && N(console["written"]) == receipts.Count && N(console["failed"]) == 0 && N(console["abandoned"]) == 0 && N(console["pending"]) == 0 && console["flush"]!.GetValue<string>() == "Succeeded", "public console dispositions");
            }
            JsonObject[] rows = Records(bytes, pid); Require(rows.Length == receipts.Count + 1, "public record count");
            Require(rows.Skip(1).Select(x => x["event_name"]!.GetValue<string>()).SequenceEqual(receipts.Select(x => x!["event"]!.GetValue<string>())), "receipt record order");
            Require(a["attempts"]!.GetValue<int>() == receipts.Count + a["stopped"]!.GetValue<int>(), "attempt accounting");
            Require(a["attempts"]!.GetValue<int>() == (mode is "normal" or "normal-dual" ? 120 : mode == "pre-cancelled" ? 40 : 514), "producer attempt count");
            Require(a["cancelled"]!.GetValue<int>() == (mode == "pre-cancelled" ? 1 : 0) && a["racingStoppers"]!.GetValue<int>() == (mode == "producer-stop-race" ? 16 : 0), "caller/starter counts");
            Require(a["capturingStopped"]!.GetValue<bool>() == (mode == "producer-stop-race"), "stopped capture");
            if (mode == "producer-stop-race") { Require(a["producerStopped"]!.GetValue<int>() + a["producerAccepted"]!.GetValue<int>() == 512 && a["producerAccepted"]!.GetValue<int>() == receipts.Count - 1, "producer race totals"); Require(a["stopped"]!.GetValue<int>() >= 1 && rows.All(x => x["event_name"]!.GetValue<string>() != "capturing"), "rejected capture absence"); }
            else { Require(a["stopped"]!.GetValue<int>() == 0, "unexpected stopped writes"); }
            return;
        }
        Require(a["kind"]!.GetValue<string>() == "kernel" && a["heldLease"]!.GetValue<bool>(), "kernel ownership"); bool flushing = mode == "held-flush", failure = mode == "late-failure";
        Require(result["completion"]!.GetValue<string>() == "TimedOut" && !result["succeeded"]!.GetValue<bool>() && result["loss"]!.GetValue<bool>() == !flushing, "kernel stop summary");
        Require(N(output["boundary"]) == (flushing ? 3 : 6) && N(output["written"]) == (flushing ? 3 : 0) && N(output["failed"]) == 0 && N(output["abandoned"]) == (flushing ? 0 : 5) && N(output["pending"]) == (flushing ? 0 : 1) && output["flush"]!.GetValue<string>() == (flushing ? "Pending" : "Failed"), "kernel dispositions");
        JsonNode live = a["live"]![0]!; Require(a["live"]!.AsArray().Count == 1 && (string?)live["name"] == "file" && N(live["accepted"]) == (flushing ? 3 : 6) && N(live["written"]) == (flushing ? 3 : failure ? 0 : 1) && N(live["failed"]) == (failure ? 1 : 0) && N(live["abandoned"]) == (flushing ? 0 : 5) && N(live["pending"]) == 0, "kernel live dispositions");
        Require(live["fault"]?.GetValue<string>() == (failure ? "WriteFailed" : null), "kernel live fault");
        JsonNode writer = a["writer"]!; Require(writer["writes"]!.GetValue<int>() == (flushing ? 3 : 1) && writer["flushes"]!.GetValue<int>() == (flushing ? 1 : 0) && writer["cancellations"]!.GetValue<int>() == 1 && writer["disposals"]!.GetValue<int>() == 1 && !writer["unsafeDispose"]!.GetValue<bool>() && !writer["inFlight"]!.GetValue<bool>(), "kernel writer lifetime");
        Require(a["cancelled"]!.GetValue<int>() == (mode == "cancelled-joiners" ? 64 : 0), "cancelled callers");
        Require(a["outputCancellationRequestedBeforeDeadline"]!.AsArray().Count == 2 && a["outputCancellationRequestedBeforeDeadline"]!.AsArray().All(x => !x!.GetValue<bool>()), "output token isolation");
        Require(a["outputCancellationsBeforeDeadline"]!.AsArray().Count == 2 && a["outputCancellationsBeforeDeadline"]!.AsArray().All(x => x!.GetValue<int>() == 0), "output cancellation counts");
        JsonNode timers = a["timers"]!; Require(timers["created"]!.GetValue<int>() == 1 && timers["disposed"]!.GetValue<int>() == 1 && timers["fires"]!.GetValue<int>() == 1 && timers["active"]!.GetValue<int>() == 0 && timers["maximumActive"]!.GetValue<int>() == 1, "kernel timer ownership");
        byte[] pending = Bytes(a, "pendingBytes"), line = Bytes(a, "line"); Require(Records(line, pid).Single()["event_name"]!.GetValue<string>() == "held.0", "held record");
        if (flushing) { Require(pending.SequenceEqual(bytes) && Records(bytes, pid).Select(x => x["event_name"]!.GetValue<string>()).SequenceEqual(new[] { "seed", "held.0", "held.1", "held.2" }), "settled flush bytes"); }
        else { Require(pending.SequenceEqual(seed.Concat(line.Take(32))), "pending write prefix"); Require(bytes.SequenceEqual(failure ? pending : seed.Concat(line).ToArray()), "settled write bytes"); }
    }
    internal static void PhysicalConsole(JsonObject report, string stdout) =>
        PhysicalConsole(report, new UTF8Encoding(false, true).GetBytes(stdout).AsMemory());

    internal static void PhysicalConsole(JsonObject report, ReadOnlyMemory<byte> stdout)
    {
        ReadOnlySpan<byte> marker = "application-after-stop\n"u8; Require(stdout.Span.EndsWith(marker), "physical console marker");
        byte[] actual = stdout[..^marker.Length].ToArray(); byte[] seed = Bytes(report, "seed"), file = Bytes(report, "bytes");
        Require(actual.SequenceEqual(file.Skip(seed.Length)), "physical console bytes"); _ = Records(actual, report["pid"]!.GetValue<int>());
    }
    internal static void Checkpoint(string mode, JsonObject a, int? processId = null)
    {
        Identity(mode, a, processId); Require(mode.StartsWith("kill-", StringComparison.Ordinal) && a["kind"]!.GetValue<string>() == "checkpoint" && a["heldLease"]!.GetValue<bool>(), "checkpoint ownership");
        byte[] seed = Bytes(a, "seed"), line = Bytes(a, "line"); Require(Records(seed, a["pid"]!.GetValue<int>()).Single()["event_name"]!.GetValue<string>() == "seed", "checkpoint seed");
        Require(Records(line, a["pid"]!.GetValue<int>()).Single()["event_name"]!.GetValue<string>() == "held.0", "checkpoint held record");
        Require(Bytes(a, "bytes").SequenceEqual(mode == "kill-queued" ? seed : seed.Concat(line.Take(32)).ToArray()), "checkpoint physical prefix");
        JsonNode writer = a["writer"]!; Require(writer["writes"]!.GetValue<int>() == 1 && writer["flushes"]!.GetValue<int>() == 0 && writer["disposals"]!.GetValue<int>() == 0 && !writer["unsafeDispose"]!.GetValue<bool>() && writer["inFlight"]!.GetValue<bool>(), "checkpoint writer lifetime");
        Require(writer["cancellations"]!.GetValue<int>() == (mode == "kill-after-stop" ? 1 : 0), "checkpoint cancellation");
        JsonNode live = a["live"]![0]!; Require(a["live"]!.AsArray().Count == 1 && (string?)live["name"] == "file" && N(live["accepted"]) == 6 && N(live["written"]) == 0 && N(live["failed"]) == 0 && N(live["abandoned"]) == (mode == "kill-after-stop" ? 5 : 0) && N(live["pending"]) == (mode == "kill-after-stop" ? 1 : 6), "checkpoint live dispositions");
        if (mode == "kill-after-stop")
        { JsonNode stop = a["result"]!; StopShape(stop.AsObject(), false); Require(N(stop["outputs"]![0]!["boundary"]) == 6 && N(stop["outputs"]![0]!["written"]) == 0 && N(stop["outputs"]![0]!["failed"]) == 0 && (string?)stop["outputs"]![0]!["flush"] == "Failed", "checkpoint output dispositions"); Require(stop["completion"]!.GetValue<string>() == "TimedOut" && !stop["succeeded"]!.GetValue<bool>() && stop["loss"]!.GetValue<bool>() && N(stop["outputs"]![0]!["pending"]) == 1 && N(stop["outputs"]![0]!["abandoned"]) == 5, "checkpoint stop summary"); }
        else { Require(a["result"] is null, "checkpoint absent stop"); }
    }
    internal static void Restart(string mode, JsonObject checkpoint, JsonObject restarted, int? processId = null)
    {
        Identity("restart", restarted, processId); Require((string?)restarted["kind"] == "restart", "restart kind"); StopShape(restarted["result"]!.AsObject(), false); Require(restarted["pid"]!.GetValue<int>() != checkpoint["pid"]!.GetValue<int>(), "restart independent PID");
        byte[] before = Bytes(checkpoint, "bytes"), bytes = Bytes(restarted, "bytes"); bool partial = mode != "kill-queued";
        Require(Bytes(restarted, "beforeBytes").SequenceEqual(before) && restarted["recovered"]!.GetValue<bool>() == partial && restarted["leaseReleased"]!.GetValue<bool>(), "restart recovery");
        Require(restarted["result"]!["succeeded"]!.GetValue<bool>() && restarted["result"]!["completion"]!.GetValue<string>() == "Completed" && !restarted["result"]!["loss"]!.GetValue<bool>(), "restart stop summary");
        Require(restarted["result"]!["outputs"]!.AsArray().Count == 1, "restart output count"); JsonNode output = restarted["result"]!["outputs"]![0]!;
        Require(output["name"]!.GetValue<string>() == "file" && N(output["boundary"]) == 1 && N(output["written"]) == 1 && N(output["failed"]) == 0 && N(output["abandoned"]) == 0 && N(output["pending"]) == 0 && output["flush"]!.GetValue<string>() == "Succeeded", "restart delivery");
        if (partial) { Require(Bytes(restarted, "damagedBytes").SequenceEqual(before), "restart damaged bytes"); }
        else { Require(restarted["damagedBytes"] is null && bytes.AsSpan().StartsWith(before), "restart retained prefix"); }
        JsonObject[] rows = Records(bytes, null); Require(rows.Length == (partial ? 1 : 2), "restart record count");
        Require(rows.All(row => row["attributes"]!["producer_pid"]!.GetValue<int>() == checkpoint["pid"]!.GetValue<int>() || row["attributes"]!["producer_pid"]!.GetValue<int>() == restarted["pid"]!.GetValue<int>()), "restart record PIDs");
        Require(rows[^1]["event_name"]!.GetValue<string>() == "restarted" && rows[^1]["attributes"]!["producer_pid"]!.GetValue<int>() == restarted["pid"]!.GetValue<int>(), "restart last record");
        Require(rows.All(x => !x["event_name"]!.GetValue<string>().StartsWith("held.", StringComparison.Ordinal)), "restart uncertain-tail absence");
    }
}
