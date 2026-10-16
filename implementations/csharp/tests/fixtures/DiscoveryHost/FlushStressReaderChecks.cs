using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushStressReaderChecks
{
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidOperationException(reason); } }
    private static long N(JsonNode? value) => LifecycleReportChecks.Number(value);

    private static void Timer(JsonNode timer, int created, int maximum, int fires)
    {
        Require(timer["created"]!.GetValue<int>() == created && timer["disposed"]!.GetValue<int>() == created && timer["maximumActive"]!.GetValue<int>() == maximum && timer["fires"]!.GetValue<int>() == fires, "timer ownership");
        Require(new[] { "active", "callbacks", "pendingJoins" }.All(key => timer[key]!.GetValue<int>() == 0) && timer["dueTicks"]!.AsArray().Count == 0, "timer ownership");
    }

    private static void Result(JsonNode result, (string Name, long Boundary, long Written, long Failed, long Abandoned, long Pending, string Flush)[]? expected = null, long invalid = 0, (long Filtered, long Full, long Unavailable)[]? rejections = null)
    {
        foreach (string key in new[] { "captured", "observed" }) { Require(DateTimeOffset.TryParseExact((string?)result[key], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset stamp) && stamp.Offset == TimeSpan.Zero, "UTC labels"); }
        Require(N(result["invalid"]) == invalid && N(result["truncated"]) == 0, "initial global facts");
        JsonArray outputs = result["outputs"]!.AsArray();
        if (expected is not null)
        {
            Require(outputs.Count == expected.Length, "covered dispositions");
            for (int index = 0; index < outputs.Count; index++)
            {
                JsonNode output = outputs[index]!;
                Require(((string)output["name"]!, N(output["boundary"]), N(output["written"]), N(output["failed"]), N(output["abandoned"]), N(output["pending"]), (string)output["flush"]!) == expected[index], "covered dispositions");
            }
        }
        if (rejections is not null)
        {
            Require(outputs.Count == rejections.Length, "initial rejection facts");
            for (int index = 0; index < outputs.Count; index++) { JsonNode output = outputs[index]!; Require((N(output["filtered"]), N(output["full"]), N(output["unavailable"])) == rejections[index], "initial rejection facts"); }
        }
    }

    private static void Writer(JsonNode writer, int pid, string[] names, string[]? order = null, string[]? statuses = null)
    {
        Require(writer["maximumIo"]!.GetValue<int>() == 1, "writer serialization");
        JsonObject[] records = LifecycleReportChecks.Records(writer["bytes"], pid);
        Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(names) && writer["writes"]!.GetValue<int>() == names.Length, "actual record order");
        Require(records.Select(record => (string)record["status"]!).SequenceEqual(statuses ?? Enumerable.Repeat("info", names.Length).ToArray()), "wire severity");
        if (order is not null) { Require(writer["order"]!.AsArray().Select(value => (string)value!).SequenceEqual(order), "serialized order"); }
    }

    private static void Diagnostics(JsonNode report, (string Name, long Accepted, long Written, long Failed, long Abandoned, long Pending, long Full, long Unavailable, long Filtered, string? Fault)[] expected)
    {
        JsonArray outputs = report["diagnostics"]!.AsArray(); Require(outputs.Count == expected.Length, "diagnostic outputs");
        for (int index = 0; index < outputs.Count; index++)
        {
            JsonNode output = outputs[index]!;
            Require(((string)output["name"]!, N(output["accepted"]), N(output["written"]), N(output["failed"]), N(output["abandoned"]), N(output["pending"]), N(output["full"]), N(output["unavailable"]), N(output["filtered"]), (string?)output["fault"]) == expected[index], "final diagnostic facts");
        }
    }

    private static string[] Order(int count) => Enumerable.Range(1, count).SelectMany(index => new[] { "write." + index, "flush." + index }).ToArray();

    internal static void Validate(string mode, JsonObject a, int? processId)
    {
        LifecycleReportChecks.Identity(mode, a, processId); int pid = a["pid"]!.GetValue<int>();
        switch (mode)
        {
            case "repeat-write": case "repeat-flush": case "repeat-progress": Repeat(mode, a, pid); break;
            case "saturation": Saturation(a, pid); break;
            case "cohort": case "failed-flush": Cohort(mode, a, pid); break;
            case "concurrent": Concurrent(a, pid); break;
            case "deadline-race": Deadline(a, pid); break;
            default: throw new InvalidOperationException("Unknown flush stress mode.");
        }
    }

    private static void Repeat(string mode, JsonObject a, int pid)
    {
        bool progress = mode == "repeat-progress", flush = mode == "repeat-flush"; long target = progress ? long.MaxValue : flush ? 3 : 4;
        JsonNode repeated = a["repeated"]!;
        Require(repeated["peakSlots"]!.GetValue<int>() is >= 1 and <= 4, "bounded backing slots");
        Require(a["before"]!["controls"]!.GetValue<int>() == (progress ? 1 : 4) && a["afterDetach"]!["controls"]!.GetValue<int>() == (progress ? 1 : 4), "bounded controls");
        Require(a["progressCleared"]!.GetValue<bool>(), "progress cleared");
        (long, long, long)[] rejection = progress ? [(0, 0, 1)] : [(0, 1, 0)];
        (string, long, long, long, long, long, string)[] initial = [("console", target, progress ? long.MaxValue - 1 : 0, 0, 0, progress ? 1 : target, progress ? "Failed" : "Pending")];
        Result(repeated["first"]!, initial, progress ? 0 : 1, rejection); Result(repeated["last"]!, initial, progress ? 0 : 1, rejection);
        Result(a["final"]!, [("console", target, target, 0, 0, 0, progress ? "Failed" : "Succeeded")], progress ? 0 : 1, rejection);
        if (progress)
        {
            Require(a["progressSlots"]!.GetValue<int>() is >= 0 and <= 4, "progress storage");
            Writer(a["writer"]!, pid, ["first"], ["write." + long.MaxValue.ToString(CultureInfo.InvariantCulture)]);
        }
        else
        {
            string[] order = (flush ? new[] { "flush.0" } : Array.Empty<string>()).Concat(Order((int)target)).ToArray();
            Writer(a["writer"]!, pid, Enumerable.Range(0, (int)target).Select(index => "record." + index).ToArray(), order);
        }
        Timer(a["timers"]!, flush ? 5002 : 5001, 1, 2500);
        Diagnostics(a, [("console", target, target, 0, 0, 0, progress ? 0 : 1, progress ? 1 : 0, 0, progress ? "AdmissionExhausted" : null)]);
    }

    private static void Saturation(JsonObject a, int pid)
    {
        Require(a["dueBeforeTimeout"]!.AsArray().Select(value => (string)value!).SequenceEqual(["50000000", "50000000"]), "monotonic due");
        Result(a["early"]!, [("console", 1, 0, 0, 0, 1, "Pending"), ("file", 1, 1, 0, 0, 0, "Pending")], 0, [(0, 0, 0), (0, 0, 0)]);
        Result(a["late"]!, [("console", 4, 0, 0, 0, 4, "Pending"), ("file", 1, 1, 0, 0, 0, "Pending")], 1, [(0, 61, 0), (64, 0, 0)]);
        Result(a["final"]!, [("console", 4, 4, 0, 0, 0, "Succeeded"), ("file", 1, 1, 0, 0, 0, "Succeeded")], 1, [(0, 61, 0), (64, 0, 0)]);
        Require((a["heldConsole"]!["controls"]!.GetValue<int>(), a["heldConsole"]!["attached"]!.GetValue<int>(), a["heldConsole"]!["buffered"]!.GetValue<int>(), a["heldFile"]!["controls"]!.GetValue<int>()) == (2, 2, 3, 1), "saturated controls");
        Require((a["detachedConsole"]!["controls"]!.GetValue<int>(), a["detachedConsole"]!["attached"]!.GetValue<int>()) == (2, 0), "retained callers");
        Require((a["detachedFile"]!["controls"]!.GetValue<int>(), a["detachedFile"]!["attached"]!.GetValue<int>()) == (1, 0), "retained file control");
        JsonArray writes = a["writes"]!.AsArray();
        var accepted = writes.Where(row => (string?)row!["outputs"]![0]!["status"] == "Accepted").OrderBy(row => N(row!["outputs"]![0]!["ticket"])).ToArray();
        Require(writes.Count == 65 && accepted.Select(row => N(row!["outputs"]![0]!["ticket"])).SequenceEqual([1L, 2L, 3L, 4L]), "admission tickets");
        Require(writes.All(row => (string?)row!["outputs"]![1]!["status"] == ((string?)row["name"] == "first" ? "Accepted" : "Filtered")), "independent threshold");
        Writer(a["console"]!, pid, accepted.Select(row => (string)row!["name"]!).ToArray(), ["write.1", "flush.1", "write.2", "write.3", "write.4", "flush.4"], ["error", "info", "info", "info"]);
        Writer(a["file"]!, pid, ["first"], ["write.1", "flush.1"], ["error"]);
        Timer(a["timers"]!, 3, 2, 6);
        Diagnostics(a, [("console", 4, 4, 0, 0, 0, 61, 0, 0, null), ("file", 1, 1, 0, 0, 0, 0, 0, 64, null)]);
    }

    private static void Cohort(string mode, JsonObject a, int pid)
    {
        bool failure = mode == "failed-flush"; long boundary = failure ? 1 : 4, written = failure ? 1 : 0, pending = failure ? 0 : 4;
        foreach (JsonNode? result in a["timeouts"]!.AsArray())
        {
            Result(result!, [("console", boundary, written, 0, 0, pending, "Pending"), ("file", boundary, written, 0, 0, pending, "Pending")], failure ? 0 : 1, [(0, failure ? 0 : 1, 0), (0, failure ? 0 : 1, 0)]);
        }
        foreach (JsonNode? result in a["results"]!.AsArray())
        {
            Result(result!, [("console", boundary, boundary, 0, 0, 0, failure ? "Failed" : "Succeeded"), ("file", boundary, boundary, 0, 0, 0, "Succeeded")], failure ? 0 : 1, [(0, failure ? 0 : 1, 0), (0, failure ? 0 : 1, 0)]);
        }
        JsonArray phases = a["phases"]!.AsArray(); int controls = failure ? 1 : 4;
        Require(phases.Count == (failure ? 3 : 4), "cohort phases");
        int P(int index, string name, string key) => phases[index]![name]![key]!.GetValue<int>();
        int T(int index) => phases[index]!["timers"]!.GetValue<int>();
        Require((P(0, "console", "controls"), P(0, "file", "controls"), P(0, "console", "attached"), P(0, "file", "attached"), T(0)) == (controls, controls, 64, 0, 64), "initial cohort");
        Require(P(0, "console", "slots") == 64 && P(1, "console", "slots") == 64, "cohort backing capacity");
        Require((P(1, "console", "attached"), P(1, "file", "attached"), T(1)) == (16, 0, 16), "cohort detachment");
        Require(P(1, "console", "controls") == controls && P(1, "file", "controls") == controls, "detached controls retained");
        Require((P(2, "console", "controls"), P(2, "file", "controls"), P(2, "console", "attached"), P(2, "file", "attached"), T(2)) == (0, controls, 0, 16, 16), "file caller phase");
        Require(P(2, "file", "slots") == 16, "file backing capacity");
        if (!failure)
        {
            Require((P(3, "file", "attached"), T(3)) == (8, 8), "file detachment");
            Require(phases[2]!["dueTicks"]!.AsArray().Select(value => (string)value!).SequenceEqual(Enumerable.Repeat("50000000", 16))
                && phases[3]!["dueTicks"]!.AsArray().Select(value => (string)value!).SequenceEqual(Enumerable.Repeat("50000000", 8)), "monotonic due");
            string[] names = Enumerable.Range(0, 4).Select(index => "record." + index).ToArray();
            Writer(a["console"]!, pid, names, Order(4)); Writer(a["file"]!, pid, names, Order(4));
            Timer(a["timers"]!, 64, 64, 48);
            Diagnostics(a, [("console", 4, 4, 0, 0, 0, 1, 0, 0, null), ("file", 4, 4, 0, 0, 0, 1, 0, 0, null)]);
        }
        else
        {
            JsonArray accepted = a["laterAccepted"]!.AsArray(); Require(accepted.Select(row => N(row!["ticket"])).SequenceEqual([2L, 3L, 4L]), "later wire mapping");
            string[] names = new[] { "record.0" }.Concat(accepted.Select(row => (string)row!["name"]!)).ToArray();
            Writer(a["console"]!, pid, ["record.0"], ["write.1"]); Require(a["console"]!["flushes"]!.GetValue<int>() == 1, "failed flush attempts");
            Writer(a["file"]!, pid, names, ["write.1", "flush.1", "write.2", "write.3", "write.4"]);
            Timer(a["timers"]!, 64, 64, 16);
            Diagnostics(a, [("console", 4, 1, 0, 3, 0, 61, 0, 0, "FlushFailed"), ("file", 4, 4, 0, 0, 0, 61, 0, 0, null)]);
        }
    }

    private static void Concurrent(JsonObject a, int pid)
    {
        string[] names = ["console", "file"]; JsonArray writes = a["writes"]!.AsArray();
        var maps = names.Select((name, index) => (name, values: writes.Where(row => (string?)row!["outputs"]![index]!["status"] == "Accepted")
            .Select(row => (Ticket: N(row!["outputs"]![index]!["ticket"]), Name: row["name"]!.GetValue<string>())).OrderBy(value => value.Ticket).ToArray()))
            .ToDictionary(pair => pair.name, pair => pair.values, StringComparer.Ordinal);
        foreach (string name in names) { Require(maps[name].Select(value => value.Ticket).SequenceEqual(Enumerable.Range(1, name == "console" ? 257 : 129).Select(index => (long)index)), "admission tickets"); }
        var targets = names.ToDictionary(name => name, _ => new HashSet<long>(), StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? row in a["observations"]!.AsArray())
        {
            Require(seen.Add(row!["caller"]!.ToJsonString()), "unique caller"); JsonNode initial = row["initial"]!, final = row["final"]!;
            Result(initial); Result(final);
            Require(initial["outputs"]!.AsArray().Select(output => (string)output!["name"]!).SequenceEqual(names), "fixed concurrent boundary");
            for (int index = 0; index < names.Length; index++)
            {
                string name = names[index]; JsonNode before = initial["outputs"]![index]!, after = final["outputs"]![index]!;
                long boundary = N(before["boundary"]); targets[name].Add(boundary);
                Require(boundary >= 1 && boundary <= (name == "console" ? 257 : 129), "captured target range");
                Require((N(before["written"]), N(before["failed"]), N(before["abandoned"]), N(before["pending"]), (string?)before["flush"]) == (0, 0, 0, boundary, "Pending"), "initial pending target");
                Require(((string?)after["name"], N(after["boundary"]), N(after["written"]), N(after["failed"]), N(after["abandoned"]), N(after["pending"]), (string?)after["flush"]) == (name, boundary, boundary, 0, 0, 0, "Succeeded"), "fixed concurrent boundary");
                Require(new[] { "filtered", "full", "unavailable" }.All(key => N(before[key]) == N(after[key])), "original concurrent facts");
            }
            Require(N(initial["outputs"]![0]!["boundary"]) == N(initial["outputs"]![1]!["boundary"]) + N(initial["outputs"]![1]!["filtered"]), "atomic fanout facts");
            Require(JsonNode.DeepEquals(initial["caller"], row["caller"]) && JsonNode.DeepEquals(final["captured"], initial["captured"]), "original boundary label");
        }
        foreach (string name in names)
        {
            JsonNode held = a[name == "console" ? "heldConsole" : "heldFile"]!;
            Require(held["targets"]!.AsArray().Select(value => N(value)).SequenceEqual(targets[name].Order()) && held["controls"]!.GetValue<int>() == targets[name].Count && targets[name].Count <= 64, "distinct target controls");
            Require(held["attached"]!.GetValue<int>() == (name == "console" ? 64 : 0) && held["slots"]!.GetValue<int>() <= 256 && held["buffered"]!.GetValue<int>() == (name == "console" ? 256 : 128), "concurrent live storage");
            string[] records = maps[name].Select(value => value.Name).ToArray();
            string[] statuses = records.Select(value => value == "first" ? "error" : int.Parse(value[(value.LastIndexOf('.') + 1)..], CultureInfo.InvariantCulture) % 2 == 0 ? "warn" : "info").ToArray();
            Writer(a[name]!, pid, records, statuses: statuses);
            long written = 0; var flushed = new List<long>();
            foreach (JsonNode? item in a[name]!["order"]!.AsArray())
            {
                string[] parts = item!.GetValue<string>().Split('.');
                Require(parts.Length == 2 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _), "serialized tickets");
                long ticket = long.Parse(parts[1], CultureInfo.InvariantCulture);
                if (parts[0] == "write") { Require(ticket == ++written, "serialized tickets"); }
                else { Require(parts[0] == "flush" && ticket == written, "serialized flush"); flushed.Add(ticket); }
            }
            Require(flushed.Count == targets[name].Count && flushed.ToHashSet().SetEquals(targets[name]), "actual fixed flush targets");
        }
        Timer(a["timers"]!, 64, 64, 0);
        Diagnostics(a, [("console", 257, 257, 0, 0, 0, 0, 0, 0, null), ("file", 129, 129, 0, 0, 0, 0, 0, 128, null)]);
    }

    private static void Deadline(JsonObject a, int pid)
    {
        JsonArray rows = a["observations"]!.AsArray();
        for (int index = 0; index < rows.Count; index++)
        {
            JsonNode row = rows[index]!;
            Require(row["during"]!["active"]!.GetValue<int>() == 0, "active callback join");
            if (index % 2 != 0) { Result(row["result"]!, [("console", 1, 0, 0, 0, 1, "Pending")], 0, [(0, 0, 0)]); }
            Timer(row["timers"]!, 1, 1, 1);
            Writer(row["writer"]!, pid, ["race." + index], ["write.1", "flush.1"]);
            Diagnostics(row, [("console", 1, 1, 0, 0, 0, 0, 0, 0, null)]);
        }
    }
}
