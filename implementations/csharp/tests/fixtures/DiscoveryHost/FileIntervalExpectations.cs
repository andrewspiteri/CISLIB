using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.FileTimerReader;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileIntervalExpectations
{
    internal static readonly string[] Modes = ["idle-min", "idle-default", "idle-max", "configured", "console-only", "file-none", "create-failure", "arm-failure", "below", "above", "env"];
    internal static void Validate(string mode, JsonObject a, int? pid = null)
    {
        Require(S(a, "mode") == mode && S(a, "runtime") == ".NET 10.0.12", "actual runtime/mode"); Pid(a, pid);
        JsonNode t = a["timers"]!;
        Require(I(t, "active") == 0 && I(t, "created") == I(t, "disposed"), "timer cleanup");
        if (I(t, "created") > 0) { Require(B(t, "flowSuppressed"), "timer context flow"); }
        bool invalid = mode is "below" or "above", failure = mode.EndsWith("-failure", StringComparison.Ordinal), disabled = mode is "console-only" or "file-none";
        if (invalid)
        {
            Require(S(a, "errorType") == "ArgumentOutOfRangeException" && a["safeCode"] is null && I(a, "workers") == 0 && I(a, "selections") == 0 && I(t, "attempts") == 0 && I(t, "created") == 0 && I(t, "fires") == 0, "before acquisition");
            Require(B(a, "noOutputBeforeFailure") && Bytes(a["fileBytes"]!).Length == 0, "no startup output"); return;
        }
        if (failure)
        {
            Require(S(a, "errorType") == "CoreLogInitializationException" && S(a, "safeCode") == "FileFlushTimerInitializationFailed", "safe initialization category");
            Require(I(a, "workers") == 2 && I(a, "selections") == 1 && B(a, "joined"), "startup workers joined");
            Require(B(a, "borrowedOpen"), "borrowed stream ownership"); Require(B(a, "ownershipReleased"), "file ownership release");
            Require(B(a, "noOutputBeforeFailure") && Bytes(a["fileBytes"]!).Length == 0, "no startup output");
            Require(I(t, "attempts") == 1 && I(t, "created") == (mode == "create-failure" ? 0 : 1) && I(t, "disposed") == (mode == "create-failure" ? 0 : 1) && I(t, "fires") == 0 && I(t, "period") == 0, "timer acquisition rollback"); return;
        }
        int? interval = mode switch { "idle-min" => 10, "idle-default" => 1000, "idle-max" => 60000, "configured" => 500, "env" => 250, _ => null };
        Require(I(t, "attempts") == (disabled ? 0 : 1) && I(t, "created") == (disabled ? 0 : 1) && I(t, "disposed") == (disabled ? 0 : 1) && I(t, "fires") == (disabled ? 0 : mode.StartsWith("idle-", StringComparison.Ordinal) ? 3 : 2), "periodic timer ownership");
        Require((disabled ? a["interval"] is null : I(a, "interval") == interval) && I(t, "period") == (interval ?? 0), "frozen configured interval");
        Require(Strings(a["targets"]!, disabled ? [] : ["1", "1"]), "idle fixed boundary"); Require(B(a, "beforeDue") && B(a, "idleNoNewEvents"), "idle scheduling facts");
        JsonArray diagnostics = a["diagnostics"]!.AsArray(); Require(diagnostics.Count == 1 && S(diagnostics[0]!, "name") == (disabled ? "console" : "file"), "diagnostic destination");
        Require(Counts(diagnostics[0]!, 1, 1, 0, 0, 0, 0, 0, 0) && diagnostics[0]!["fault"] is null, "actual event counts");
        JsonNode encoded;
        if (mode.StartsWith("idle-", StringComparison.Ordinal))
        {
            JsonNode w = a["writer"]!; Require(Writer(w, 1, 2, 1), "actual writer calls");
            Require(Strings(w["order"]!, ["write.1", "flush.1", "flush.1"]), "serialized periodic order"); encoded = w["bytes"]!;
        }
        else
        {
            Require(B(a, "ownershipReleased"), "file ownership release"); Require(B(a, "borrowedOpen"), "borrowed stream ownership"); Require(B(a, "optionMutationIgnored"), "frozen option mutation"); encoded = a["bytes"]!;
        }
        Names(encoded, a["pid"]!.GetValue<int>(), ["one"], "actual record order");
    }
}
