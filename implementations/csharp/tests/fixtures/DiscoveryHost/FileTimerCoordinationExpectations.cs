using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.FileTimerReader;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerCoordinationExpectations
{
    internal static readonly string[] Modes = ["blocked-write", "blocked-flush", "continuous", "duplicate-start", "closed-start", "detach-before-arm", "detach-system-before-arm", "arm-false", "arm-disposed"];
    internal static void Validate(string mode, JsonObject a, int? pid = null)
    {
        Require(S(a, "mode") == mode && S(a, "runtime") == ".NET 10.0.12", "actual runtime/mode"); Pid(a, pid);
        Require(B(a, "workerCompleted"), "worker joined"); Require(I(a, "finalControls") == 0, "control cleanup");
        JsonNode t = a["timers"]!, w = a["writer"]!;
        Require(I(t, "active") == 0 && I(t, "activePeriodic") == 0 && I(t, "created") == I(t, "disposed"), "timer cleanup");
        Require(I(t, "deadlineFires") == 0, "caller deadlines did not expire"); Require(I(w, "maximumIo") <= 1, "serialized writer");
        if (I(t, "periodicFires") > 0) { Require(B(t, "periodicSuppressed"), "periodic context flow"); }
        Require(I(t, "systemCreated") == (mode == "detach-system-before-arm" ? 1 : 0) && I(t, "systemFalseChanges") == (mode == "detach-system-before-arm" ? 1 : 0), "actual system disposal signal");
        bool blocked = mode.StartsWith("blocked-", StringComparison.Ordinal), continuous = mode == "continuous"; int n = blocked ? 4 : continuous ? 128 : 0, actualPid = a["pid"]!.GetValue<int>();
        if (blocked)
        {
            Require(B(a, "controlsIdentical"), "retained control identity"); string[] expected = ["1", "2", "3", "4"];
            foreach (string key in new[] { "beforePulses", "afterPulses" })
            {
                JsonNode g = a[key]!; Require(I(g, "controls") == 4 && I(g, "buffered") == 3 && I(g, "attached") == 0 && I(g, "slots") == 0, "bounded pulse controls");
                Require(Strings(g["targets"]!, expected), "fixed pulse targets");
            }
            JsonNode held = a["heldWriter"]!; bool flush = mode == "blocked-flush";
            Require(Strings(held["order"]!, flush ? ["write.1"] : []), "held writer order");
            Names(held["bytes"]!, actualPid, flush ? ["record.1"] : [], "held record bytes");
            Require(Writer(held, 1, flush ? 1 : 0, 1), "held writer stayed blocked");
            JsonNode detach = a["afterDetach"]!;
            Require(I(detach, "controls") == 4 && Strings(detach["targets"]!, expected) && I(detach, "buffered") == 3, "bounded retained controls");
            Require(I(detach, "attached") == 1, "remaining live caller"); Require(I(detach, "slots") <= 4, "bounded continuation storage");
            JsonNode c = a["repeated"]!; Require(I(c, "ticks") == 3000 && I(c, "cancelled") == 500, "actual repeated callers");
            Require(I(c, "peakAttached") == 2 && I(c, "peakSlots") <= 4, "bounded caller storage");
            Require(I(c, "createdBeforeTicks") == 1 && I(c, "createdAfterTicks") == 1, "no per-tick timer allocation");
            Require(I(c, "workerIdBefore") == I(c, "workerIdAfter") && I(c, "workerIdBefore") > 0, "same worker completion");
            Require(I(t, "periodicFires") == 3004, "periodic requests");
            Require(I(t, "created") == 502 && I(t, "disposed") == 502 && I(t, "maximumActive") == 3, "caller timer counts");
        }
        else if (continuous)
        {
            Require(Strings(a["boundaries"]!, Enumerable.Range(1, 128).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))), "continuous fixed boundaries");
            Require(I(a, "pulseCreations") == 0, "no per-tick timer allocation");
            Require(I(t, "created") == 129 && I(t, "disposed") == 129 && I(t, "periodicFires") == 128 && I(t, "maximumActive") == 2, "continuous timer counts");
        }
        else
        {
            Require(S(a, "startError") == (mode is "duplicate-start" or "arm-false" ? "InvalidOperationException" : mode == "arm-disposed" ? "ObjectDisposedException" : null), "timer start classification");
            bool duplicate = mode == "duplicate-start";
            Require(I(t, "created") == (duplicate ? 2 : 1) && I(t, "disposed") == (duplicate ? 2 : 1) && I(t, "periodicFires") == (duplicate ? 1 : 0), "startup timer work");
            JsonNode h = a["heldTimers"]!; Require(I(h, "created") == (duplicate ? 2 : 1) && I(h, "disposed") == 1 && I(h, "active") == (duplicate ? 1 : 0) && I(h, "activePeriodic") == (duplicate ? 1 : 0), "original or detached timer ownership");
            Require(I(w, "writes") == 0 && I(w, "flushes") == (duplicate ? 1 : 0), "startup writer calls");
            Require(Strings(w["order"]!, duplicate ? ["flush.0"] : []), "startup writer order");
        }
        if (blocked || continuous)
        {
            JsonNode result = a["result"]!; JsonArray outputs = result["outputs"]!.AsArray();
            Require(S(result, "completion") == "Completed" && B(result, "succeeded") && B(result, "loss") == blocked, "actual final result");
            Require(N(result, "invalid") == 0 && N(result, "truncated") == 0 && outputs.Count == 1, "initial global observations");
            JsonNode o = outputs[0]!;
            Require(S(o, "name") == "file" && new[] { "boundary", "written", "failed", "abandoned", "pending" }.Select(key => N(o, key)).SequenceEqual(new long[] { n, n, 0, 0, 0 }), "fixed final coverage");
            Require(S(o, "flush") == "Succeeded" && B(o, "flushSucceeded") && N(o, "filtered") == 0 && N(o, "full") == (blocked ? 1 : 0) && N(o, "unavailable") == 0, "writer flush result");
            Require(Writer(w, n, n, 1), "actual writer calls");
            Require(Strings(w["order"]!, Enumerable.Range(1, n).SelectMany(i => new[] { "write." + i, "flush." + i })), "serialized record/flush order");
        }
        Names(w["bytes"]!, actualPid, Enumerable.Range(1, n).Select(i => "record." + i), "actual record order");
        JsonArray d = a["diagnostics"]!.AsArray(); Require(d.Count == 1 && S(d[0]!, "name") == "file", "diagnostic destination");
        Require(Counts(d[0]!, n, n, 0, 0, 0, blocked ? 1 : 0, 0, 0) && d[0]!["fault"] is null, "actual event accounting");
    }
}
