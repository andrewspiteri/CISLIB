using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConsoleCompositionFixture
{
    private sealed class Clock : TimeProvider
    {
        internal int Calls;
        public override DateTimeOffset GetUtcNow() { Calls++; return new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    }
    internal static EventMetadata Metadata() => new("Console.App", "build-1", "1.2.3", "test", EnvironmentKind.Console,
        "11111111-1111-1111-1111-111111111111", null, new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero),
        "unknown", null, null, null, null, null, null, null, null);
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        string profile = row["profile"]!.GetValue<string>(); bool physical = row["physical"]!.GetValue<bool>(); int selectionCalls = 0;
        using var stderr = new MemoryStream(); using var stdout = new MemoryStream(); using var file = new MemoryStream(); var clock = new Clock();
        var options = new LoggingOptions
        {
            ConsoleLevel = profile == "both" ? CoreLogLevel.Warning : profile is "file" or "disabled" ? CoreLogLevel.None : CoreLogLevel.Information,
            FilePath = profile is "both" or "file" ? "controlled.jsonl" : null,
            FileLevel = CoreLogLevel.Debug,
            MaxEntryBytes = row["maxEntryBytes"]!.GetValue<int>(),
            QueueCapacity = 16
        };
        if (row["format"]!.GetValue<int>() == 1) { options.ConsoleFormat = LogOutputFormat.JsonLines; }
        if (row["detail"]!.GetValue<int>() == 1) { options.HumanDetail = HumanOutputDetail.Verbose; }
        if (row["stream"]!.GetValue<int>() == 1) { options.ConsoleStream = ConsoleOutputStream.StandardOutput; }
        Stream Select(ConsoleOutputStream selected)
        {
            selectionCalls++; options.ConsoleFormat = (LogOutputFormat)999; options.HumanDetail = (HumanOutputDetail)999; options.ConsoleStream = (ConsoleOutputStream)999;
            options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.None; options.FilePath = null; options.QueueCapacity = 0; options.MaxEntryBytes = 1;
            return selected == ConsoleOutputStream.StandardError ? stderr : stdout;
        }
        bool fileEnabled = options.FileEnabled;
        var runtime = CoreLogRuntime.CreateConsoleQueued(Metadata(), options,
            fileEnabled ? item => file.WriteAsync(item.Event.JsonLine) : null, clock, physical ? null : Select);
        options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.None; options.FilePath = null;
        options.ConsoleFormat = (LogOutputFormat)999; options.HumanDetail = (HumanOutputDetail)999; options.ConsoleStream = (ConsoleOutputStream)999;
        options.QueueCapacity = 0; options.MaxEntryBytes = 1;
        Activity? previous = Activity.Current; Activity.Current = null;
        try
        {
            var results = new JsonArray(); CoreLogger logger = runtime.CreateLogger("Console");
            int[] levels = row["mode"]!.GetValue<string>() == "cap" ? [3] : [1, 2, 3, 4];
            foreach (int level in levels)
            {
                bool cap = row["mode"]!.GetValue<string>() == "cap";
                var data = new LogEventData
                {
                    EventName = "console.event",
                    Message = cap ? new string('\u200d', 8192) : Message(level),
                    MessageTemplate = "cmd {count}",
                    OperationId = "op-1",
                    Attributes = cap ?
                    new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('\u200d', 8192)) } :
                    new Dictionary<string, LogValue> { ["count"] = LogValue.Integer(2), ["text"] = LogValue.String("value") }
                };
                LogWriteResult value = logger.Write((CoreLogLevel)level, data);
                results.Add(Result(level, value));
                data.Message = "CIS_MUTATED_AFTER_CAPTURE"; data.Attributes = null;
            }
            runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); CoreLogDiagnostics final = runtime.GetDiagnostics();
            return new JsonObject
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["results"] = results,
                ["selectionCalls"] = selectionCalls,
                ["clockCalls"] = clock.Calls,
                ["stderr"] = Convert.ToBase64String(stderr.ToArray()),
                ["stdout"] = Convert.ToBase64String(stdout.ToArray()),
                ["file"] = Convert.ToBase64String(file.ToArray()),
                ["invalid"] = final.InvalidEventCount.ToString(CultureInfo.InvariantCulture),
                ["truncated"] = final.TruncatedEventCount.ToString(CultureInfo.InvariantCulture),
                ["state"] = final.State.ToString(),
                ["outputs"] = new JsonArray(final.Outputs.Select(output => (JsonNode)new JsonObject
                {
                    ["name"] = output.OutputName,
                    ["accepted"] = output.Accepted.ToString(CultureInfo.InvariantCulture),
                    ["written"] = output.Written.ToString(CultureInfo.InvariantCulture),
                    ["filtered"] = output.Filtered.ToString(CultureInfo.InvariantCulture),
                    ["pending"] = output.Pending.ToString(CultureInfo.InvariantCulture),
                    ["failed"] = output.Failed.ToString(CultureInfo.InvariantCulture),
                    ["abandoned"] = output.Abandoned.ToString(CultureInfo.InvariantCulture)
                }).ToArray())
            };
        }
        finally { Activity.Current = previous; runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    internal static string Message(int level) => "message-" + level + " caf\u00e9 \U0001F642\nnext \u202e";
    private static JsonObject Result(int level, LogWriteResult value) => new()
    {
        ["level"] = level,
        ["outcome"] = value.Outcome.ToString(),
        ["truncated"] = value.IsTruncated,
        ["outputs"] = new JsonArray(value.Outputs.Select(output => (JsonNode)new JsonObject
        {
            ["name"] = output.OutputName,
            ["status"] = output.Status.ToString(),
            ["ticket"] = output.AcceptedSequence?.ToString(CultureInfo.InvariantCulture)
        }).ToArray())
    };
    internal static int Run(string path, string id)
    {
        JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject(), row = catalog["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); ConsoleCompositionExpectations.Validate(catalog, row, actual);
        actual["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ConsoleCompositionFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
            ["inputsSha256"] = Hash(path)
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
