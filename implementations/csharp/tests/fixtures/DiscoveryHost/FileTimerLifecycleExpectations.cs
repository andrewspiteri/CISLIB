using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.FileTimerReader;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerLifecycleExpectations
{
    internal static readonly string[] Modes = ["close-write", "close-flush", "close-callback", "flush-failure", "flush-failure-callback", "admission-fault", "detached-arm-throw"];
    internal static void Validate(string mode, JsonObject a, int? pid = null)
    {
        Require(S(a, "mode") == mode, "mode"); Pid(a, pid); Require(S(a, "runtime") == ".NET 10.0.12", "actual runtime");
        Require(B(a, "sameSharedCompletion"), "shared completion identity"); Require(B(a, "sameDisposal"), "shared disposal identity");
        Require(B(a, "sharedJoined") && I(a, "finalControls") == 0, "joined cleanup"); Require(I(a, "repeatedClosures") == 64, "concurrent repeated closure");
        bool closing = mode.StartsWith("close-", StringComparison.Ordinal), physical = mode == "close-callback", callback = mode is "close-callback" or "flush-failure-callback", admission = mode == "admission-fault";
        JsonNode t = a["timers"]!, h = a["held"]!, ht = h["timers"]!;
        Require(I(t, "periodicDisposed") == 1 && I(t, "created") == I(t, "disposed") && I(t, "created") == (mode.StartsWith("flush-failure", StringComparison.Ordinal) ? 2 : 1), "owned timer accounting");
        Require(I(t, "active") == 0 && I(t, "callbacks") == 0 && I(t, "pendingJoins") == 0, "final timer join");
        foreach ((string key, string reason) in new[] { ("suppressedCreation", "timer creation flow"), ("outsideGate", "disposal outside admission gate"), ("withoutCaller", "disposal execution flow"), ("published", "published closure before disposal"), ("afterCloser", "disposal after outer closer") })
        { Require(B(t, key) && B(ht, key), reason); }
        Require(B(h, "timerDetached"), "early timer detach");
        Require(I(ht, "created") == 1 && I(ht, "disposed") == 1 && I(ht, "periodicDisposed") == 1 && I(ht, "active") == 0, "early timer cancellation");
        Require(I(t, "fires") == (mode == "detached-arm-throw" ? 0 : mode == "flush-failure-callback" ? 2 : 1), "no post-cancel periodic work");
        if (mode == "detached-arm-throw")
        {
            Require(S(a, "startError") == "ObjectDisposedException", "detached throwing arm classification"); Require(I(t, "syncDisposes") == 0, "no duplicate synchronous disposal");
            Require(!B(h, "sharedPending") && !B(h, "workerPending"), "startup closure joined"); JsonNode writer = a["writer"]!;
            Require(Writer(writer, 0, 0, 0) && Strings(writer["order"]!, []) && Bytes(writer["bytes"]!).Length == 0, "no startup I/O"); return;
        }
        int actualPid = a["pid"]!.GetValue<int>();
        if (closing || admission) { Require(B(h, "sharedPending") && B(h, "workerPending"), "blocked shared drain"); }
        if (physical)
        {
            Require(B(h, "ownershipHeld") && B(a, "ownershipReleased"), "physical ownership join"); Require(I(h, "controls") == 0, "closed callback registers no work");
            Require(I(ht, "callbacks") == 1 && I(ht, "pendingJoins") == 1, "active callback join"); Names(a["fileBytes"]!, actualPid, ["record.1"], "physical record");
        }
        else
        {
            JsonNode w = a["writer"]!, hw = h["writer"]!; bool bw = mode == "close-write", fault = !closing;
            Require(B(a, "workerFaulted") == mode.StartsWith("flush-failure", StringComparison.Ordinal), "worker failure category");
            Require(Writer(w, 1, 1, 1), "actual sequential writer"); Require(Writer(hw, 1, bw ? 0 : 1, 1), "held actual I/O");
            Require(Strings(hw["order"]!, bw ? [] : ["write.1"]), "held I/O order"); Names(hw["bytes"]!, actualPid, bw ? [] : ["record.1"], "held file records");
            Require(Strings(w["order"]!, closing || admission ? ["write.1", "flush.1"] : ["write.1"]), "actual final I/O order"); Names(w["bytes"]!, actualPid, ["record.1"], "actual file records");
            Require(B(h, "flushPending") == (closing || admission), "in-flight flush observation"); Require(I(h, "controls") == (closing || admission ? 1 : 0), "retained flush controls");
            if (callback) { JsonNode join = a["heldJoin"]!; Require(B(h, "workerPending") && I(join, "callbacks") == 1 && I(join, "pendingJoins") == 1, "fault callback join"); }
            JsonArray hd = h["diagnostics"]!.AsArray(); Require(hd.Select(d => S(d!, "name")).SequenceEqual(closing ? ["file"] : ["console", "file"]), "held diagnostic destinations");
            JsonNode fd = hd[^1]!; Require(S(fd, "state") == (closing ? "Stopping" : "Faulted"), "held lifecycle state");
            Require(closing ? Counts(fd, 1, bw ? 0 : 1, 0, 0, bw ? 1 : 0, 0, 0, 0) : Counts(fd, 3, 1, 0, 2, 0, 0, admission ? 1 : 0, 0), "held event accounting");
            Require(S(fd, "fault") == (closing ? null : admission ? "AdmissionExhausted" : "FlushFailed"), "held safe fault");
            if (!closing) { Utc(fd, "held fault UTC observation"); } else { Require(fd["faultAt"] is null, "held healthy fault absence"); }
            if (fault)
            {
                JsonNode c = a["console"]!; Names(c["bytes"]!, actualPid, ["record.1", "record.2", "record.3", "console.4"], "healthy console continuation");
                Require(Writer(c, 4, admission ? 1 : 2, 1), "healthy console I/O");
                Require(Strings(c["order"]!, new[] { "write.1", "write.2", "write.3", "write.4" }.Concat(Enumerable.Repeat("flush.4", admission ? 1 : 2))), "healthy console order");
                if (!admission)
                {
                    JsonNode result = a["result"]!; Require(S(result, "completion") == "Completed" && !B(result, "succeeded") && B(result, "loss"), "public fault result");
                    Require(N(result, "invalid") == 0 && N(result, "truncated") == 0 && result["outputs"]!.AsArray().Count == 2, "fault result global observations");
                    for (int slot = 0; slot < 2; slot++)
                    {
                        JsonNode o = result["outputs"]![slot]!; bool file = slot == 1;
                        Require(S(o, "name") == (file ? "file" : "console") && new[] { "boundary", "written", "failed", "abandoned", "pending" }.Select(key => N(o, key)).SequenceEqual(file ? new long[] { 3, 1, 0, 2, 0 } : new long[] { 4, 4, 0, 0, 0 }) && S(o, "flush") == (file ? "Failed" : "Succeeded"), "fault fixed boundary result");
                        Require(N(o, "full") == 0 && N(o, "filtered") == 0 && N(o, "unavailable") == (file ? 1 : 0), "fault result rejections");
                    }
                }
            }
        }
        JsonArray diagnostics = a["diagnostics"]!.AsArray(); Require(diagnostics.Select(d => S(d!, "name")).SequenceEqual(closing ? ["file"] : ["console", "file"]), "diagnostic destinations");
        foreach (JsonNode? d in diagnostics)
        {
            bool file = S(d!, "name") == "file"; int n = closing ? 1 : file ? 3 : 4;
            Require(Counts(d!, n, file ? 1 : n, 0, file && !closing ? 2 : 0, 0, 0, file && !closing ? 1 : 0, 0), "final event accounting");
            Require(S(d!, "state") == "Stopped", "final lifecycle state"); string? fault = file && !closing ? admission ? "AdmissionExhausted" : "FlushFailed" : null;
            Require(S(d!, "fault") == fault, "safe output fault"); if (fault is not null) { Utc(d!, "fault UTC observation"); } else { Require(d!["faultAt"] is null, "healthy fault absence"); }
        }
    }
}
