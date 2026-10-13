using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class JsonlBoundaryFixture
{
    internal static readonly string[] Modes = ["empty", "object", "unicode", "two-records", "chunk-boundary", "missing-lf", "partial-json", "partial-utf8", "invalid-json", "invalid-utf8", "array", "blank", "crlf", "bom", "missing-inventory", "wrong-format", "wrong-encoding", "repeat", "with-retained", "collision-file", "collision-directory", "retention-full", "identity-exhausted"];
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    internal static int Run(string mode, string root) { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown JSONL boundary mode.", nameof(mode)); }
        string path = Path.Combine(root, "logs", "\u03a9-\U0001f642.jsonl"), metadata = path + ".cis.inventory.json";
        using (FileOutputOwner.Initialize(path, mode == "wrong-format" ? LogOutputFormat.Human : LogOutputFormat.JsonLines)) { }
        byte[] seed = mode switch
        {
            "repeat" or "with-retained" or "collision-file" or "collision-directory" or "retention-full" or "identity-exhausted" => Encoding.UTF8.GetBytes("{\"stage\":"),
            "empty" => [],
            "unicode" => Encoding.UTF8.GetBytes("{\"message\":\"\u03a9 \U0001f642 e\u0301\"}\n"),
            "two-records" => Encoding.UTF8.GetBytes("{\"first\":true}\n{\"next\":true}\n"),
            "chunk-boundary" => Encoding.UTF8.GetBytes("{\"prior\":true}\n{\"message\":\"" + new string('x', 4081) + "\"}\n"),
            "missing-lf" => Encoding.UTF8.GetBytes("{\"a\":1}"),
            "partial-json" => Encoding.UTF8.GetBytes("{\"prior\":true}\n{\"message\":"),
            "partial-utf8" => [.. Encoding.UTF8.GetBytes("{\"prior\":true}\n{\"message\":\""), 0xf0, 0x9f],
            "invalid-json" => Encoding.UTF8.GetBytes("{\"a\":\n"),
            "invalid-utf8" => [.. Encoding.UTF8.GetBytes("{\"a\":\""), 0xff, .. Encoding.UTF8.GetBytes("\"}\n")],
            "array" => Encoding.UTF8.GetBytes("[]\n"),
            "blank" => [10],
            "crlf" => Encoding.UTF8.GetBytes("{}\r\n"),
            "bom" => [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("{}\n")],
            "wrong-format" or "wrong-encoding" => [0xff, 10],
            _ => Encoding.UTF8.GetBytes("{\"a\":1}\n")
        };
        File.WriteAllBytes(path, seed);
        if (mode == "missing-inventory") { File.Delete(metadata); }
        if (mode == "wrong-encoding") { JsonObject doc = JsonNode.Parse(File.ReadAllBytes(metadata))!.AsObject(); doc["encoding"] = "utf-16"; File.WriteAllText(metadata, doc.ToJsonString()); }
        if (mode is "with-retained" or "retention-full" or "identity-exhausted" or "collision-file" or "collision-directory")
        {
            JsonObject doc = JsonNode.Parse(File.ReadAllBytes(metadata))!.AsObject();
            Guid group = Guid.ParseExact(doc["groupId"]!.GetValue<string>(), "N");
            JsonArray entries = doc["segments"]!.AsArray();
            if (mode is "with-retained" or "retention-full")
            {
                entries[0]!["id"] = mode == "with-retained" ? 7 : 2;
                string closed = FileGroupInventory.ClosedName(group, 1, FileSegmentState.Closed);
                entries.Insert(0, new JsonObject { ["id"] = 1, ["fileName"] = closed, ["state"] = "closed" });
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, closed), "prior closed evidence\n");
                if (mode == "with-retained")
                {
                    string damaged = FileGroupInventory.ClosedName(group, 3, FileSegmentState.Damaged);
                    entries.Insert(1, new JsonObject { ["id"] = 3, ["fileName"] = damaged, ["state"] = "damaged" });
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path)!, damaged), [0xff, 10]);
                }
            }
            if (mode == "identity-exhausted") { entries[0]!["id"] = long.MaxValue; }
            File.WriteAllText(metadata, doc.ToJsonString());
            string collision = Path.Combine(Path.GetDirectoryName(path)!, FileGroupInventory.ClosedName(group, 1, FileSegmentState.Damaged));
            if (mode == "collision-file") { File.WriteAllText(collision, "reserved name\n"); }
            if (mode == "collision-directory") { Directory.CreateDirectory(collision); File.WriteAllText(Path.Combine(collision, "keep.txt"), "reserved name\n"); }
        }
        byte[]? inventoryBefore = File.Exists(metadata) ? File.ReadAllBytes(metadata) : null; string foreign = Path.Combine(Path.GetDirectoryName(path)!, ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed"); File.WriteAllText(foreign, "untouched neighbor\n");
        JsonObject preimage = Snapshot(Path.GetDirectoryName(path)!);
        int inspected = 0; long? originalPosition = null, restoredPosition = null, length = null; string? status = null, inspectionError = null;
        try
        {
            using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); owner.ActiveFile.Position = seed.Length == 0 ? 0 : 3; originalPosition = owner.ActiveFile.Position; inspected++;
            JsonlRecordBoundary boundary = JsonlRecordBoundary.Inspect(owner); restoredPosition = owner.ActiveFile.Position; length = boundary.Length; status = boundary.Status.ToString();
        }
        catch (CoreLogInitializationException error) { inspectionError = error.ErrorCode; }
        // These exclusive probes run in the producer before exit, before retry/runtime creation.
        bool inspectionReleased = Released(path) && Released(path + ".cis.lock");
        string? startupError = null, outcome = null;
        long? written = null;
        bool? recovered = null, heldRecovered = null, restartRecovered = null;
        string? firstActive = null, repeatBefore = null;
        CoreLogRuntime? runtime = null;
        var phases = new JsonArray();
        var options = new LoggingOptions { FilePath = path, FileFormat = LogOutputFormat.JsonLines, ConsoleLevel = CoreLogLevel.None, MaxEntryBytes = 16384, FileMaxBytes = 65536, FileRetainedFiles = mode == "retention-full" ? 1 : 10 };
        options.Prepare(root);
        try
        {
            for (int phase = 0; phase < (mode == "repeat" ? 2 : 1); phase++)
            {
                runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock());
                LogOutputDiagnostics held = runtime.GetDiagnostics().Outputs.Single();
                recovered = held.RecoveredPartialSegment;
                LogWriteResult result = runtime.CreateLogger("Boundary").Write(CoreLogLevel.Information, new LogEventData { EventName = "boundary.append", Message = "\u03a9 \U0001f642", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } });
                outcome = result.Outcome.ToString();
                runtime.CloseAdmission();
                await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(15));
                LogOutputDiagnostics final = runtime.GetDiagnostics().Outputs.Single();
                written = final.Written;
                heldRecovered = held.RecoveredPartialSegment;
                phases.Add(new JsonObject
                {
                    ["recovered"] = final.RecoveredPartialSegment,
                    ["heldRecovered"] = held.RecoveredPartialSegment,
                    ["heldAccepted"] = held.Accepted,
                    ["heldFailed"] = held.Failed,
                    ["heldFault"] = held.LastFaultCode,
                    ["state"] = final.State.ToString(),
                    ["accepted"] = final.Accepted,
                    ["written"] = final.Written,
                    ["failed"] = final.Failed,
                    ["abandoned"] = final.Abandoned,
                    ["pending"] = final.Pending,
                    ["fault"] = final.LastFaultCode,
                    ["faultAt"] = final.LastFaultAtUtc
                });
                if (mode == "repeat" && phase == 0)
                {
                    byte[] first = File.ReadAllBytes(path);
                    firstActive = Convert.ToBase64String(first);
                    byte[] interrupted = [.. first, .. Encoding.UTF8.GetBytes("{\"again\":\""), 0xf0, 0x9f];
                    File.WriteAllBytes(path, interrupted);
                    repeatBefore = Convert.ToBase64String(interrupted);
                }
            }
            var restarted = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock());
            try { restartRecovered = restarted.GetDiagnostics().Outputs.Single().RecoveredPartialSegment; }
            finally { restarted.CloseAdmission(); await restarted.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(15)); }
        }
        catch (CoreLogInitializationException error) { startupError = error.ErrorCode; }
        finally { if (runtime is not null) { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(15)); } }
        return new JsonObject
        {
            ["mode"] = mode,
            ["path"] = path,
            ["pid"] = Environment.ProcessId,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
            ["fixtureSha256"] = Hash(typeof(JsonlBoundaryFixture).Assembly.Location),
            ["inspected"] = inspected,
            ["status"] = status,
            ["length"] = length,
            ["originalPosition"] = originalPosition,
            ["restoredPosition"] = restoredPosition,
            ["inspectionError"] = inspectionError,
            ["inspectionReleased"] = inspectionReleased,
            ["startupError"] = startupError,
            ["outcome"] = outcome,
            ["written"] = written,
            ["recovered"] = recovered,
            ["heldRecovered"] = heldRecovered,
            ["restartRecovered"] = restartRecovered,
            ["firstActive"] = firstActive,
            ["repeatBefore"] = repeatBefore,
            ["phases"] = phases,
            ["preimage"] = preimage,
            ["filesystem"] = Snapshot(Path.GetDirectoryName(path)!),
            ["finalReleased"] = Released(path) && Released(path + ".cis.lock"),
            ["before"] = Convert.ToBase64String(seed),
            ["after"] = Convert.ToBase64String(File.ReadAllBytes(path)),
            ["inventoryBefore"] = inventoryBefore is null ? null : Convert.ToBase64String(inventoryBefore),
            ["inventoryAfter"] = File.Exists(metadata) ? Convert.ToBase64String(File.ReadAllBytes(metadata)) : null,
            ["neighbor"] = Convert.ToBase64String(File.ReadAllBytes(foreign)),
            ["names"] = new JsonArray(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFileName).Order(StringComparer.Ordinal).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())
        };
    }
    private static JsonObject Snapshot(string root)
    {
        var result = new JsonObject();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        { result[Path.GetRelativePath(root, file).Replace('\\', '/')] = Convert.ToBase64String(File.ReadAllBytes(file)); }
        return result;
    }
    private static bool Released(string path) { try { using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
