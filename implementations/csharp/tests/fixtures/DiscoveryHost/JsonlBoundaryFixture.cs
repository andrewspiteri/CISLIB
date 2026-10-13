using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class JsonlBoundaryFixture
{
    internal static readonly string[] Modes = ["empty", "object", "unicode", "two-records", "chunk-boundary", "missing-lf", "partial-json", "partial-utf8", "invalid-json", "invalid-utf8", "array", "blank", "crlf", "bom", "missing-inventory", "wrong-format", "wrong-encoding"];
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    internal static int Run(string mode, string root) { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown JSONL boundary mode.", nameof(mode)); }
        string path = Path.Combine(root, "logs", "\u03a9-\U0001f642.jsonl"), metadata = path + ".cis.inventory.json";
        using (FileOutputOwner.Initialize(path, mode == "wrong-format" ? LogOutputFormat.Human : LogOutputFormat.JsonLines)) { }
        byte[] seed = mode switch
        {
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
        byte[]? inventoryBefore = File.Exists(metadata) ? File.ReadAllBytes(metadata) : null; string foreign = Path.Combine(Path.GetDirectoryName(path)!, ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed"); File.WriteAllText(foreign, "untouched neighbor\n");
        int inspected = 0; long? originalPosition = null, restoredPosition = null, length = null; string? status = null, inspectionError = null;
        try
        {
            using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); owner.ActiveFile.Position = seed.Length == 0 ? 0 : 3; originalPosition = owner.ActiveFile.Position; inspected++;
            JsonlRecordBoundary boundary = JsonlRecordBoundary.Inspect(owner); restoredPosition = owner.ActiveFile.Position; length = boundary.Length; status = boundary.Status.ToString();
        }
        catch (CoreLogInitializationException error) { inspectionError = error.ErrorCode; }
        // These exclusive probes run in the producer before exit, before retry/runtime creation.
        bool inspectionReleased = Released(path) && Released(path + ".cis.lock"); string? startupError = null; string? outcome = null; long? written = null; CoreLogRuntime? runtime = null;
        try
        {
            var options = new LoggingOptions { FilePath = path, FileFormat = LogOutputFormat.JsonLines, ConsoleLevel = CoreLogLevel.None, MaxEntryBytes = 16384, FileMaxBytes = 65536, FileRetainedFiles = 10 }; options.Prepare(root);
            runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock());
            LogWriteResult result = runtime.CreateLogger("Boundary").Write(CoreLogLevel.Information, new LogEventData { EventName = "boundary.append", Message = "\u03a9 \U0001f642", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } });
            outcome = result.Outcome.ToString(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(15)); written = runtime.GetDiagnostics().Outputs.Single().Written;
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
            ["finalReleased"] = Released(path) && Released(path + ".cis.lock"),
            ["before"] = Convert.ToBase64String(seed),
            ["after"] = Convert.ToBase64String(File.ReadAllBytes(path)),
            ["inventoryBefore"] = inventoryBefore is null ? null : Convert.ToBase64String(inventoryBefore),
            ["inventoryAfter"] = File.Exists(metadata) ? Convert.ToBase64String(File.ReadAllBytes(metadata)) : null,
            ["neighbor"] = Convert.ToBase64String(File.ReadAllBytes(foreign)),
            ["names"] = new JsonArray(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFileName).Order(StringComparer.Ordinal).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())
        };
    }
    private static bool Released(string path) { try { using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
