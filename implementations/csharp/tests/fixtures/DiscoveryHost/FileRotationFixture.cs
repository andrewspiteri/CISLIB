using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileRotationFixture
{
    internal static readonly string[] Modes = ["jsonl-exact", "jsonl-over", "human-exact", "human-over"];
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    private sealed class Borrowed : MemoryStream
    {
        internal int Disposes;
        protected override void Dispose(bool disposing) { if (disposing) { Disposes++; } base.Dispose(disposing); }
    }
    internal static int Run(string mode, string root) { Console.WriteLine(Execute(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    private static async Task<JsonObject> Execute(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown rotation mode.", nameof(mode)); }
        bool human = mode.StartsWith("human", StringComparison.Ordinal), over = mode.EndsWith("over", StringComparison.Ordinal);
        string path = Path.Combine(root, "logs", "\u03a9-\U0001f642.log"), reference = Path.Combine(root, "reference.log");
        LoggingOptions Options(string name) => new() { FilePath = name, FileFormat = human ? LogOutputFormat.Human : LogOutputFormat.JsonLines, ConsoleFormat = LogOutputFormat.JsonLines, HumanDetail = HumanOutputDetail.Verbose, MaxEntryBytes = 16384, FileMaxBytes = 65536, FileRetainedFiles = 10 };
        LogEventData Data() => new() { EventName = "rotation.event", Message = "\u03a9 \U0001f642 e\u0301" };
        CoreLogRuntime? runtime = null;
        using var borrowed = new Borrowed();
        try
        {
            var referenceOptions = Options(reference); referenceOptions.ConsoleLevel = CoreLogLevel.None; referenceOptions.Prepare(root);
            runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), referenceOptions, new Clock()); runtime.CreateLogger("Rotation").Write(CoreLogLevel.Information, Data());
            runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); runtime = null;
            byte[] record = File.ReadAllBytes(reference);
            string prefix = human ? "seed=\"" : "{\"seed\":\"", suffix = human ? "\"\n" : "\"}\n";
            int seedLength = 65536 - record.Length + (over ? 1 : 0); byte[] seed = Encoding.UTF8.GetBytes(prefix + new string('a', seedLength - prefix.Length - suffix.Length) + suffix);
            using (FileOutputOwner.Initialize(path, human ? LogOutputFormat.Human : LogOutputFormat.JsonLines)) { }
            File.WriteAllBytes(path, seed); byte[] oldInventory = File.ReadAllBytes(path + ".cis.inventory.json");
            var options = Options(path); options.Prepare(root); runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock(), _ => borrowed);
            LogWriteResult result = runtime.CreateLogger("Rotation").Write(CoreLogLevel.Information, Data()); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit);
            CoreLogDiagnostics diagnostics = runtime.GetDiagnostics();
            return new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["mode"] = mode,
                ["path"] = path,
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
                ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
                ["fixtureSha256"] = Hash(typeof(FileRotationFixture).Assembly.Location),
                ["result"] = result.Outcome.ToString(),
                ["truncated"] = result.IsTruncated,
                ["tickets"] = new JsonArray(result.Outputs.Select(x => (JsonNode)new JsonObject { ["name"] = x.OutputName, ["status"] = x.Status.ToString(), ["sequence"] = x.AcceptedSequence }).ToArray()),
                ["state"] = diagnostics.State.ToString(),
                ["borrowedDisposes"] = borrowed.Disposes,
                ["outputs"] = new JsonArray(diagnostics.Outputs.Select(x => (JsonNode)new JsonObject { ["name"] = x.OutputName, ["state"] = x.State.ToString(), ["accepted"] = x.Accepted, ["written"] = x.Written, ["failed"] = x.Failed, ["pending"] = x.Pending, ["abandoned"] = x.Abandoned }).ToArray()),
                ["recordBytes"] = Convert.ToBase64String(record),
                ["seedBytes"] = Convert.ToBase64String(seed),
                ["activeBytes"] = Convert.ToBase64String(File.ReadAllBytes(path)),
                ["consoleBytes"] = Convert.ToBase64String(borrowed.ToArray()),
                ["oldInventory"] = JsonNode.Parse(oldInventory),
                ["inventory"] = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json")),
                ["closed"] = new JsonArray(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.closed").Order(StringComparer.Ordinal).Select(x => (JsonNode)new JsonObject { ["name"] = Path.GetFileName(x), ["bytes"] = Convert.ToBase64String(File.ReadAllBytes(x)) }).ToArray())
            };
        }
        finally { if (runtime is not null) { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
