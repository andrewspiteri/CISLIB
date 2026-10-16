using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushCoordinationExpectations
{
    internal static void Validate(string mode, JsonObject actual, int? processId = null)
    {
        FlushReaderChecks.Coordination(mode, actual, processId);
        void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
        int Number(string key) => actual[key]!.GetValue<int>();
        Require(actual["runtime"]!.GetValue<string>() == ".NET 10.0.12", "pinned framework storage");
        Require(actual["mode"]!.GetValue<string>() == mode && actual["physicalFiles"]!.GetValue<int>() == 0, "actual controlled runtime");
        Require(Number("retainedAfterDetach") == 0 && Number("finalControls") == 0, "detached coordination");
        Require(actual["shared"]!.GetValue<bool>(), "shared controls");
        bool repeated = mode is "write-cancel" or "write-deadline" or "flush-cancel" or "flush-deadline";
        Require(Number("iterations") == (repeated ? 10000 : 0), "full caller count");
        Require(Number("fileControlsPeak") == (mode == "cohort" ? 3 : mode == "dual" ? 1 : 0), "bounded file target controls");
        Require(Number("controlsPeak") == (repeated ? 4 : mode == "cohort" ? 3 : mode is "dual" or "fault" ? 2 : 1), "bounded target controls");
        Require(Number("attachedPeak") == (repeated || mode is "dual" or "fault" ? 1 : mode == "cohort" ? 64 : mode == "complete-race" ? 16 : 0), "attached callers");
        Require(Number("continuationSlotsPeak") == (repeated ? 4 : Number("attachedPeak")) && Number("continuationSlotsAfterDetach") == (repeated ? 4 : 0), "bounded continuation storage");
        Require(Number("deadlineFires") == (mode.EndsWith("deadline", StringComparison.Ordinal) ? 10000 : mode == "cohort" ? 16 : 0), "actual deadline callback");
        if (mode == "complete-race")
        {
            Require(Number("cancelled") + Number("succeeded") == 16 && Number("failed") == 0, "settled race cohort");
            Require(actual["race"]!.AsArray().Count == 16 && actual["race"]!.AsArray().All(value => value!.GetValue<string>() is "succeeded" or "cancelled"), "allowed race outcomes");
        }
        else
        {
            Require(Number("cancelled") == (repeated ? 10000 : mode == "cohort" ? 48 : mode == "pre-cancel" ? 2 : mode == "dual" ? 1 : 0), "caller cancellation");
            Require(Number("succeeded") == (mode == "cohort" ? 16 : mode == "fault" ? 0 : 1) && Number("failed") == (mode == "fault" ? 1 : 0), "actual writer status");
        }
        Require(actual["completedTokenCancelled"]!.GetValue<bool>() == (mode == "pre-cancel"), "cancelled completed caller");
        JsonArray phases = actual["phases"]!.AsArray();
        int Phase(int index, string key) => phases[index]![key]!.GetValue<int>();
        if (mode == "cohort")
        {
            Require(phases.Count == 2 && Phase(0, "consoleAttached") == 64 && Phase(1, "consoleAttached") == 16
                && Phase(0, "fileAttached") == 0 && Phase(1, "fileAttached") == 0, "actual surviving cohort attachments");
        }
        if (mode == "dual")
        {
            Require(phases.Count == 3 && Phase(0, "consoleAttached") == 1 && Phase(0, "fileAttached") == 0
                && Phase(1, "consoleAttached") == 0 && Phase(1, "fileAttached") == 1
                && Phase(2, "consoleAttached") == 0 && Phase(2, "fileAttached") == 0, "actual output attachment transition");
        }
        JsonArray snapshots = actual["snapshots"]!.AsArray();
        long Count(JsonNode value, string key) => long.Parse(value[key]!.GetValue<string>(), CultureInfo.InvariantCulture);
        if (repeated)
        {
            Require(snapshots.Count == 6, "bounded owned snapshots");
            Require(Count(snapshots[^2]!["console"]!, "boundary") == (mode.StartsWith("write", StringComparison.Ordinal) ? 4 : 3)
                && Count(snapshots[^2]!["console"]!, "queueFull") == 1 && Count(snapshots[^1]!["console"]!, "queueFull") == 4, "original caller observations");
            Require(Count(snapshots[^2]!, "invalid") == 1 && Count(snapshots[^1]!, "invalid") == 1, "initial global invalid");
        }
        if (mode == "dual")
        {
            Require(Count(snapshots[1]!["console"]!, "boundary") == 2 && Count(snapshots[1]!["file"]!, "boundary") == 1, "independent targets");
            Require(Count(snapshots[1]!, "invalid") == 0 && Count(snapshots[2]!, "invalid") == 1, "original global observations");
        }
        bool both = mode is "cohort" or "dual";
        Require(actual["outputs"]!.AsArray().Count == (both ? 2 : 1), "enabled workers");
        foreach (JsonNode? value in actual["outputs"]!.AsArray())
        {
            JsonNode output = value!; string name = output["name"]!.GetValue<string>();
            long accepted = repeated ? mode.StartsWith("write", StringComparison.Ordinal) ? 4 : 3 : mode == "cohort" ? 3 : mode == "dual" ? name == "console" ? 2 : 1 : mode == "fault" ? 2 : 1;
            Require(Count(output, "accepted") == accepted && Count(output, "written") == (mode == "fault" ? 0 : accepted)
                && Count(output, "failed") == (mode == "fault" ? 1 : 0) && Count(output, "abandoned") == (mode == "fault" ? 1 : 0) && Count(output, "pending") == 0, "terminal record accounting");
            JsonNode writer = actual["writers"]![name]!;
            Require(writer["maximum"]!.GetValue<int>() == 1, "serialized writer");
            string[] order = writer["order"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
            string[] expected = mode == "fault" ? [] : mode == "dual" && name == "console" ? ["write.1", "flush.1", "write.2", "flush.2", "flush.2"] :
                mode == "dual" ? ["flush.0", "write.1", "flush.1"] :
                Enumerable.Range(1, (int)accepted).SelectMany(index => new[] { "write." + index, "flush." + index }).ToArray();
            if (mode.StartsWith("flush", StringComparison.Ordinal)) { expected = new[] { "flush.0" }.Concat(expected).ToArray(); }
            Require(order.SequenceEqual(expected), "fixed target writer order");
        }
    }
}
