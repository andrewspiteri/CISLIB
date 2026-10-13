using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTransitionFixture
{
    internal static readonly string[] Modes = ["rotation-intent", "rotation-renamed", "rotation-published", "rotation-created",
        "recovery-intent", "recovery-renamed", "recovery-published", "recovery-created",
        "human-intent", "human-renamed", "human-published", "human-created", "ambiguous-both", "mismatch-bytes", "nonempty-fresh", "retirement-no-intent"];
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    internal static int Run(string mode, string root) { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown transition mode.", nameof(mode)); }
        bool recovery = mode.StartsWith("recovery", StringComparison.Ordinal), human = mode.StartsWith("human", StringComparison.Ordinal);
        LogOutputFormat format = human ? LogOutputFormat.Human : LogOutputFormat.JsonLines;
        string directory = Path.Combine(root, "logs"), path = Path.Combine(directory, "\u03a9-\U0001f642.log");
        using (FileOutputOwner.Initialize(path, format)) { }
        JsonObject metadata = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
        Guid group = Guid.ParseExact(metadata["groupId"]!.GetValue<string>(), "N");
        string Name(int id, FileSegmentState state) => FileGroupInventory.ClosedName(group, id, state);
        JsonArray segments = metadata["segments"]!.AsArray(); segments.Clear();
        int retained = recovery ? 1 : 2;
        for (int id = 1; id <= retained; id++)
        {
            FileSegmentState state = id == 1 ? FileSegmentState.Damaged : FileSegmentState.Closed;
            string name = Name(id, state); File.WriteAllText(Path.Combine(directory, name), id == 1 ? "older damage" : human ? "older=true\n" : "{\"older\":true}\n", new UTF8Encoding(false));
            segments.Add(new JsonObject { ["id"] = id, ["fileName"] = name, ["state"] = state == FileSegmentState.Damaged ? "damaged" : "closed" });
        }
        segments.Add(new JsonObject { ["id"] = retained + 1, ["fileName"] = Path.GetFileName(path), ["state"] = "active" });
        File.WriteAllText(path + ".cis.inventory.json", metadata.ToJsonString(), new UTF8Encoding(false));
        byte[] original = Encoding.UTF8.GetBytes(recovery ? "{\"earlier\":true}\n{\"partial\":" : human ? "earlier=true\n" : "{\"earlier\":true}\n");
        File.WriteAllBytes(path, original);
        FileGroupInventory next;
        using (var owner = FileOutputOwner.Initialize(path, format))
        {
            next = recovery ? owner.Inventory!.AfterPartialRecovery(path, 2) : owner.Inventory!.AfterRotation(path, 2);
            if (!recovery) { File.Delete(Path.Combine(directory, owner.Inventory!.Segments[0].FileName)); }
            FileGroupTransition.Begin(owner, next, 2, recovery);
        }
        string archived = Path.Combine(directory, next.Segments[^2].FileName);
        string stage = mode.Contains('-', StringComparison.Ordinal) ? mode[(mode.IndexOf('-', StringComparison.Ordinal) + 1)..] : "";
        if (mode is "mismatch-bytes" or "nonempty-fresh") { stage = mode == "mismatch-bytes" ? "renamed" : "created"; }
        if (stage is "renamed" or "published" or "created") { File.Move(path, archived); }
        if (stage is "published" or "created") { next.PublishReplacement(path); }
        if (stage == "created") { File.WriteAllBytes(path, []); }
        if (mode == "ambiguous-both") { File.WriteAllBytes(archived, original); }
        if (mode == "mismatch-bytes") { File.WriteAllText(archived, "{\"other\":true}\n", new UTF8Encoding(false)); }
        if (mode == "nonempty-fresh") { File.WriteAllText(path, "{\"fresh\":true}\n", new UTF8Encoding(false)); }
        if (mode == "retirement-no-intent") { File.Delete(FileGroupTransition.PendingPath(path)); }
        File.WriteAllBytes(Path.Combine(root, "neighbor.bin"), [0xff, 0x00, 0xfe]);
        File.WriteAllText(Path.Combine(directory, Name(50, FileSegmentState.Closed)), "unlisted neighbor\n", new UTF8Encoding(false));
        JsonObject Snapshot() => new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file =>
            new KeyValuePair<string, JsonNode?>(Path.GetRelativePath(root, file).Replace('\\', '/'), JsonValue.Create(Convert.ToBase64String(File.ReadAllBytes(file))))));
        JsonObject before = Snapshot();
        var options = new LoggingOptions { FilePath = path, ConsoleLevel = CoreLogLevel.None, FileFormat = format, HumanDetail = HumanOutputDetail.Verbose, MaxEntryBytes = 16384, FileMaxBytes = 65536, FileRetainedFiles = 2 }; options.Prepare(root);
        JsonObject? error = null, observation = null; string? outcome = null; bool flag = false, restartFlag = false;
        try
        {
            var runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock());
            try
            {
                flag = runtime.GetDiagnostics().Outputs.Single().RecoveredPartialSegment;
                outcome = runtime.CreateLogger("Transition").Write(CoreLogLevel.Information, new LogEventData { EventName = "transition.fresh", Message = "\u03a9 \U0001f642", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } }).Outcome.ToString();
            }
            finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
            LogOutputDiagnostics file = runtime.GetDiagnostics().Outputs.Single();
            observation = new JsonObject { ["state"] = file.State.ToString(), ["accepted"] = file.Accepted, ["written"] = file.Written, ["failed"] = file.Failed, ["abandoned"] = file.Abandoned, ["pending"] = file.Pending, ["recovered"] = file.RecoveredPartialSegment, ["fault"] = file.LastFaultCode };
            var restarted = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock());
            try { restartFlag = restarted.GetDiagnostics().Outputs.Single().RecoveredPartialSegment; }
            finally { restarted.CloseAdmission(); await restarted.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
        }
        catch (CoreLogInitializationException failure)
        { error = new JsonObject { ["code"] = failure.ErrorCode, ["output"] = failure.OutputName, ["message"] = failure.Message, ["inner"] = failure.InnerException is not null, ["dataCount"] = failure.Data.Count }; }
        bool released;
        using (var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { released = true; }
        bool dataReleased;
        using (var data = new FileStream(File.Exists(path) ? path : archived, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { dataReleased = true; }
        return new JsonObject
        {
            ["mode"] = mode,
            ["composition"] = "staged-physical-transition-to-configured-runtime",
            ["pid"] = Environment.ProcessId,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["path"] = path,
            ["group"] = group.ToString("N"),
            ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
            ["fixtureSha256"] = Hash(typeof(FileTransitionFixture).Assembly.Location),
            ["before"] = before,
            ["after"] = Snapshot(),
            ["error"] = error,
            ["outcome"] = outcome,
            ["recovered"] = flag,
            ["restartRecovered"] = restartFlag,
            ["final"] = observation,
            ["leaseReleased"] = released,
            ["dataReleased"] = dataReleased
        };
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
