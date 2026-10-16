using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.FileTimerReader;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerQualificationExpectations
{
    internal static readonly string[] Modes = ["stopped-rearm", "faulted-rearm"];
    internal static void Validate(string mode, JsonObject a, int? pid = null)
    {
        Require(S(a, "mode") == mode && S(a, "runtime") == ".NET 10.0.12", "runtime mode"); Pid(a, pid); bool fault = mode == "faulted-rearm";
        Require(B(a, "sameWorker") && B(a, "sameSharedCompletion") && B(a, "sameDisposal"), "original task identities");
        Require(B(a, "workerFaulted") == fault && B(a, "sharedFaulted") == fault, "actual terminal worker"); Require(I(a, "finalControls") == 0, "no terminal controls");
        JsonNode before = a["before"]!, after = a["after"]!; Require(JsonNode.DeepEquals(before, after), "stable terminal diagnostic");
        Require(S(before, "state") == (fault ? "Faulted" : "Stopped"), "terminal state"); Require(S(before, "fault") == (fault ? "FlushFailed" : null), "safe terminal fault");
        Require(new[] { "accepted", "written", "failed", "abandoned", "pending", "unavailable" }.Select(key => N(before, key)).SequenceEqual(new long[] { 1, 1, 0, 0, 0, 0 }), "terminal event accounting");
        JsonArray probes = a["probes"]!.AsArray(); Require(probes.Count == 64, "restart probe count");
        for (int index = 0; index < probes.Count; index++)
        {
            JsonNode p = probes[index]!; Require(I(p, "created") == index + 2 && I(p, "disposed") == index + 2 && I(p, "active") == 0 && I(p, "fires") == 1 && I(p, "controls") == 0, "inactive restart probes");
        }
        JsonNode t = a["timers"]!, bt = a["beforeTimers"]!;
        Require(I(bt, "created") == 1 && I(bt, "disposed") == 1 && I(bt, "active") == 0 && I(bt, "periodicFires") == 1, "original timer disposal");
        Require(I(t, "created") == 65 && I(t, "disposed") == 65 && I(t, "active") == 0 && I(t, "activePeriodic") == 0 && I(t, "periodicFires") == 1 && I(t, "deadlineFires") == 0 && I(t, "maximumActive") == 1, "final timer work");
        Require(B(t, "periodicSuppressed"), "suppressed periodic creation");
        JsonNode w = a["writer"]!; Require(Writer(w, 1, 1, 1), "original sequential I/O"); Require(Strings(w["order"]!, fault ? ["write.1"] : ["write.1", "flush.1"]), "actual I/O order");
        Names(w["bytes"]!, a["pid"]!.GetValue<int>(), ["one"], "actual original record");
        Require(S(a, "lateOutcome") == (fault ? "Rejected" : "Stopped") && S(a, "lateOutput") == (fault ? "Faulted" : "Stopped"), "late admission outcome");
        JsonObject late = before.DeepClone().AsObject(); late["unavailable"] = fault ? "1" : "0";
        Require(JsonNode.DeepEquals(late, a["afterLate"]), "late admission accounting");
    }
}
