using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileRetentionFixture
{
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    internal static int Run(string mode, string phase, string root) { Console.WriteLine(Execute(mode, phase, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    private static async Task<JsonObject> Execute(string mode, string phase, string root)
    {
        if (mode is not ("jsonl-1" or "jsonl-3" or "human-1" or "human-3") || phase is not ("start" or "resume")) { throw new ArgumentException("Unknown retention phase."); }
        bool human = mode.StartsWith("human", StringComparison.Ordinal); int retained = mode.EndsWith('1') ? 1 : 3, first = phase == "start" ? 0 : 20, count = phase == "start" ? 20 : 12;
        string path = Path.Combine(root, "logs", "\u03a9-\U0001f642.log"), instance = Guid.NewGuid().ToString("D");
        JsonNode? before = File.Exists(path + ".cis.inventory.json") ? JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json")) : null;
        var options = new LoggingOptions { FilePath = path, FileFormat = human ? LogOutputFormat.Human : LogOutputFormat.JsonLines, FileMaxBytes = 65536, FileRetainedFiles = retained, ConsoleLevel = CoreLogLevel.None, HumanDetail = HumanOutputDetail.Verbose, QueueCapacity = 64, MaxEntryBytes = 16384 }; options.Prepare(root);
        string os = OperatingSystem.IsWindows() ? "windows" : "linux";
        var metadata = new EventMetadata("Retention.App", "build-1", "1.2.3", "test", EnvironmentKind.Console, instance, null, new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), os, Environment.ProcessId, null, null, null, null, null, null, null);
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(metadata, options, new Clock());
        // Caller mutations cannot change the captured count or next segment identity.
        options.FileRetainedFiles = 1000;
        try
        {
            var results = new JsonArray(); CoreLogger logger = runtime.CreateLogger("Retention");
            for (int index = first; index < first + count; index++)
            {
                LogWriteResult result = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "retention.event", Message = new string('x', 7000) + "\u03a9\U0001f642", Attributes = new Dictionary<string, LogValue> { ["entry_index"] = LogValue.Integer(index), ["payload"] = LogValue.String(new string('y', 7000)) } });
                results.Add(new JsonObject { ["outcome"] = result.Outcome.ToString(), ["truncated"] = result.IsTruncated, ["sequence"] = result.Outputs.Single().AcceptedSequence });
            }
            runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(15)); CoreLogDiagnostics diagnostics = runtime.GetDiagnostics(); LogOutputDiagnostics output = diagnostics.Outputs.Single();
            JsonObject inventory = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
            return new JsonObject
            {
                ["mode"] = mode,
                ["phase"] = phase,
                ["path"] = path,
                ["pid"] = Environment.ProcessId,
                ["instanceId"] = instance,
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = os,
                ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
                ["fixtureSha256"] = Hash(typeof(FileRetentionFixture).Assembly.Location),
                ["before"] = before,
                ["inventory"] = inventory,
                ["results"] = results,
                ["state"] = diagnostics.State.ToString(),
                ["output"] = new JsonObject { ["name"] = output.OutputName, ["state"] = output.State.ToString(), ["accepted"] = output.Accepted, ["written"] = output.Written, ["failed"] = output.Failed, ["pending"] = output.Pending, ["abandoned"] = output.Abandoned },
                ["files"] = new JsonArray(inventory["segments"]!.AsArray().Select(segment => { string name = segment!["fileName"]!.GetValue<string>(), member = Path.Combine(Path.GetDirectoryName(path)!, name); return (JsonNode)new JsonObject { ["name"] = name, ["bytes"] = new FileInfo(member).Length, ["sha256"] = Hash(member) }; }).ToArray())
            };
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(15)); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
