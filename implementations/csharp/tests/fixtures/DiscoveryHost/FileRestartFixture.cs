using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileRestartFixture
{
    internal static readonly string[] Modes = ["intact", "partial-json", "partial-utf8", "rotation-intent", "rotation-renamed", "rotation-published", "rotation-created",
        "recovery-intent", "recovery-renamed", "recovery-published", "recovery-created", "collision-file", "collision-directory", "unproven-gap", "ambiguous-both"];
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    internal static int Run(string mode, string phase, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal) || phase is not ("seed" or "hold" or "resume" or "probe")) { throw new ArgumentException("Unknown restart fixture request."); }
        return phase is "seed" or "hold" ? SeedAsync(mode, phase, root).GetAwaiter().GetResult() : ResumeAsync(mode, phase, root).GetAwaiter().GetResult();
    }
    private static string Active(string root) => Path.Combine(root, "logs", "\u03a9-\U0001f642.jsonl");
    private static LoggingOptions Options(string root, string mode)
    {
        var options = new LoggingOptions { FilePath = Active(root), ConsoleLevel = CoreLogLevel.None, FileFormat = LogOutputFormat.JsonLines, MaxEntryBytes = 16384, FileMaxBytes = 65536, FileRetainedFiles = mode.StartsWith("rotation", StringComparison.Ordinal) || mode is "unproven-gap" or "ambiguous-both" ? 2 : 3 };
        options.Prepare(root); return options;
    }
    private static LogEventData Data(string name) => new() { EventName = name, Message = "\u03a9 \U0001f642", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } };
    private static async Task<int> SeedAsync(string mode, string phase, string root)
    {
        string path = Active(root), directory = Path.GetDirectoryName(path)!;
        var runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), Options(root, mode), new Clock());
        try
        {
            CoreLogger logger = runtime.CreateLogger("Restart");
            for (int index = 0; index < 2; index++) { if (logger.Write(CoreLogLevel.Information, Data("restart.seed." + index)).Outcome != LogWriteOutcome.Accepted) { throw new InvalidOperationException("Seed was not admitted."); } }
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
        byte[][] records = File.ReadAllBytes(path).SplitRecords();
        if (records.Length != 2 || runtime.GetDiagnostics().Outputs.Single().Written != 2) { throw new InvalidOperationException("Seed did not write two records."); }
        JsonObject metadata = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
        Guid group = Guid.ParseExact(metadata["groupId"]!.GetValue<string>(), "N");
        bool rotation = mode.StartsWith("rotation", StringComparison.Ordinal) || mode is "unproven-gap" or "ambiguous-both", recovery = mode.StartsWith("recovery", StringComparison.Ordinal);
        byte[] suffix = mode == "partial-utf8" || recovery ? [.. Encoding.UTF8.GetBytes("{\"partial\":\""), 0xf0, 0x9f] : Encoding.UTF8.GetBytes("{\"partial\":");
        if (rotation || recovery)
        {
            JsonArray members = metadata["segments"]!.AsArray(); members.Clear();
            string damage = FileGroupInventory.ClosedName(group, 1, FileSegmentState.Damaged), closed = FileGroupInventory.ClosedName(group, 2, FileSegmentState.Closed);
            File.WriteAllBytes(Path.Combine(directory, damage), [0xff, 10]); File.WriteAllBytes(Path.Combine(directory, closed), records[0]);
            members.Add(new JsonObject { ["id"] = 1, ["fileName"] = damage, ["state"] = "damaged" }); members.Add(new JsonObject { ["id"] = 2, ["fileName"] = closed, ["state"] = "closed" });
            members.Add(new JsonObject { ["id"] = 3, ["fileName"] = Path.GetFileName(path), ["state"] = "active" });
            File.WriteAllText(path + ".cis.inventory.json", metadata.ToJsonString(), new UTF8Encoding(false));
            File.WriteAllBytes(path, recovery ? [.. records[1], .. suffix] : records[1]);
        }
        else if (mode != "intact") { File.WriteAllBytes(path, [.. records[0], .. records[1], .. suffix]); }
        File.WriteAllBytes(Path.Combine(root, "neighbor.bin"), [0xff, 0, 0xfe]);
        File.WriteAllBytes(Path.Combine(directory, FileGroupInventory.ClosedName(group, 50, FileSegmentState.Closed)), records[0]);
        File.WriteAllBytes(Path.Combine(directory, ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed"), records[1]);
        File.WriteAllText(path + ".cis.inventory.json.unlisted.tmp", "unlisted candidate\n", new UTF8Encoding(false));
        using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines);
        FileStream? fresh = null;
        try
        {
            if (rotation || recovery)
            {
                FileGroupInventory next = recovery ? owner.Inventory!.AfterPartialRecovery(path, 3) : owner.Inventory!.AfterRotation(path, 2);
                if (mode != "unproven-gap")
                {
                    FileGroupTransition.Begin(owner, next, recovery ? 3 : 2, recovery);
                    if (rotation) { foreach (var member in owner.Inventory!.Segments.Take(owner.Inventory.Segments.Count + 1 - next.Segments.Count)) { File.Delete(Path.Combine(directory, member.FileName)); } }
                    string target = Path.Combine(directory, next.Segments[^2].FileName), stage = mode[(mode.IndexOf('-', StringComparison.Ordinal) + 1)..];
                    if (mode == "ambiguous-both") { File.WriteAllBytes(target, records[1]); }
                    else if (stage is "renamed" or "published" or "created")
                    {
                        owner.ActiveFile.Dispose(); File.Move(path, target);
                        if (stage is "published" or "created") { next.PublishReplacement(path); }
                        if (stage == "created") { fresh = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read); }
                    }
                }
                else if (rotation) { foreach (var member in owner.Inventory!.Segments.Take(owner.Inventory.Segments.Count + 1 - next.Segments.Count)) { File.Delete(Path.Combine(directory, member.FileName)); } }
            }
            if (mode.StartsWith("collision", StringComparison.Ordinal))
            {
                string target = Path.Combine(directory, FileGroupInventory.ClosedName(group, 1, FileSegmentState.Damaged));
                if (mode == "collision-file") { File.WriteAllText(target, "reserved collision\n", new UTF8Encoding(false)); }
                else { Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep.txt"), "reserved collision\n", new UTF8Encoding(false)); }
            }
            JsonObject result = Common(mode, phase, root); result["group"] = group.ToString("N"); result["files"] = Snapshot(root); result["names"] = Names(root);
            result["seedRecords"] = new JsonArray(records.Select(record => (JsonNode)JsonValue.Create(Convert.ToBase64String(record))!).ToArray()); result["seedWritten"] = 2L; result["leaseHeld"] = !Released(path + ".cis.lock");
            Console.WriteLine(result.ToJsonString()); Console.Out.Flush();
            if (phase == "hold") { _ = Console.ReadLine(); }
            return 0;
        }
        finally { fresh?.Dispose(); }
    }
    private static async Task<int> ResumeAsync(string mode, string phase, string root)
    {
        string path = Active(root); JsonObject before = Snapshot(root); JsonArray namesBefore = Names(root);
        JsonObject? error = null, final = null; string? outcome = null;
        try
        {
            var runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), Options(root, mode), new Clock());
            try { if (phase == "resume") { outcome = runtime.CreateLogger("Restart").Write(CoreLogLevel.Information, Data("restart.resume")).Outcome.ToString(); } }
            finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
            LogOutputDiagnostics file = runtime.GetDiagnostics().Outputs.Single();
            final = new JsonObject { ["state"] = file.State.ToString(), ["accepted"] = file.Accepted, ["written"] = file.Written, ["failed"] = file.Failed, ["abandoned"] = file.Abandoned, ["pending"] = file.Pending, ["recovered"] = file.RecoveredPartialSegment, ["fault"] = file.LastFaultCode };
        }
        catch (CoreLogInitializationException failure)
        { error = new JsonObject { ["code"] = failure.ErrorCode, ["output"] = failure.OutputName, ["message"] = failure.Message, ["inner"] = failure.InnerException is not null, ["dataCount"] = failure.Data.Count }; }
        JsonObject result = Common(mode, phase, root); result["before"] = before; result["after"] = Snapshot(root); result["namesBefore"] = namesBefore; result["namesAfter"] = Names(root);
        result["error"] = error; result["final"] = final; result["outcome"] = outcome; result["leaseReleased"] = Released(path + ".cis.lock");
        Console.WriteLine(result.ToJsonString()); return 0;
    }
    private static JsonObject Common(string mode, string phase, string root) => new()
    {
        ["mode"] = mode,
        ["phase"] = phase,
        ["composition"] = "actual-core-seed-with-staged-physical-restart",
        ["pid"] = Environment.ProcessId,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
        ["path"] = Active(root),
        ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
        ["fixtureSha256"] = Hash(typeof(FileRestartFixture).Assembly.Location)
    };
    private static JsonObject Snapshot(string root) => new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith(".cis.lock", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(file =>
        new KeyValuePair<string, JsonNode?>(Path.GetRelativePath(root, file).Replace('\\', '/'), JsonValue.Create(Convert.ToBase64String(Read(file))))));
    private static JsonArray Names(string root) => new(Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).Order(StringComparer.Ordinal).Select(name => (JsonNode)JsonValue.Create(name)!).ToArray());
    private static byte[] Read(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var buffer = new MemoryStream(); file.CopyTo(buffer); return buffer.ToArray(); }
    private static bool Released(string path) { try { using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static byte[][] SplitRecords(this byte[] bytes)
    {
        var result = new List<byte[]>(); int start = 0;
        for (int index = 0; index < bytes.Length; index++) { if (bytes[index] == 10) { result.Add(bytes[start..(index + 1)]); start = index + 1; } }
        if (start != bytes.Length) { throw new InvalidDataException("Incomplete seed."); }
        return result.ToArray();
    }
}
