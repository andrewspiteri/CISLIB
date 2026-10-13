using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

// The parent supplies real environment variables before this fresh process first accesses the static container.
internal static class ConsoleConsumerFixture
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class Clock : TimeProvider
    {
        internal int Calls;
        public override DateTimeOffset GetUtcNow() { Calls++; return new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    }
    private sealed class HeldStream(bool fails) : MemoryStream
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly IOException Error = new("consumer-private-path-and-payload");
        internal int Disposes;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            if (fails) { throw Error; }
            await base.WriteAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing) { if (disposing) { Disposes++; } base.Dispose(disposing); }
    }
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        string mode = row["mode"]!.GetValue<string>(); bool physical = row["physical"]!.GetValue<bool>();
        // No case values assign presentation/stream/threshold settings here: the real constructor reads the child's environment.
        EnvironmentOptions.EnvironmentKind = EnvironmentKind.Console;
        LoggingOptions options = EnvironmentOptions.Logging; options.Prepare(Directory.GetCurrentDirectory());
        var metadata = new EventMetadata(); var clock = new Clock(); using var file = new MemoryStream(); using var seam = new HeldStream(mode == "fail");
        ConsoleOutputStream? selectedStream = null;
        Stream Select(ConsoleOutputStream selected) { selectedStream = selected; return seam; }
        CoreLogRuntime runtime = CoreLogRuntime.CreateConsoleQueued(metadata, options, options.FileEnabled ? item => file.WriteAsync(item.Event.JsonLine) : null,
            clock, acquireConsole: physical ? null : Select);
        CoreLogger logger = runtime.CreateLogger("Consumer");
        var results = new JsonArray(); var factories = new JsonArray(); var checkpoints = new JsonArray(); int calls = 0;
        LogWriteResult Write(string label, CoreLogLevel level, int id)
        {
            int before = calls;
            LogWriteResult result = logger.WriteLazy(level, () =>
            {
                calls++; return new LogEventData
                {
                    EventName = "consumer.event",
                    Message = Message(id) + (mode == "pressure" ? new string('p', 2048) : ""),
                    MessageTemplate = "consumer {n}",
                    OperationId = "consumer-op",
                    Attributes = new Dictionary<string, LogValue> { ["n"] = LogValue.Integer(id) }
                };
            });
            factories.Add(calls - before); results.Add(Result(label, result)); return result;
        }
        async Task FileWritten(int count)
        {
            using var deadline = new CancellationTokenSource(Limit);
            while (runtime.QueuedOutputs!.File!.CaptureAccounting().Written < count) { await Task.Delay(1, deadline.Token); }
        }
        async Task Join(Task completion)
        {
            try { await completion.WaitAsync(Limit); }
            catch (IOException error) when (mode == "fail" && ReferenceEquals(error, seam.Error)) { }
        }
        try
        {
            if (physical)
            {
                if (mode == "pressure") { for (int id = 1; id <= 64; id++) { Write("pressure-" + id.ToString(CultureInfo.InvariantCulture), CoreLogLevel.Information, id); } }
                else { Write("debug", CoreLogLevel.Debug, 1); Write("info", CoreLogLevel.Information, 2); Write("error", CoreLogLevel.Error, 3); }
            }
            else
            {
                Write("first", CoreLogLevel.Information, 1); await seam.Started.Task.WaitAsync(Limit);
                Write("second", CoreLogLevel.Information, 2); await FileWritten(2);
                JsonObject held = Project(runtime.GetDiagnostics()); held["phase"] = "file-progress-while-console-held";
                held["consoleInFlight"] = Number(runtime.QueuedOutputs!.Console!.CaptureAccounting().InFlight);
                held["consoleWorkerCompleted"] = runtime.QueuedOutputs.Console.Completion.IsCompleted; checkpoints.Add(held);
                seam.Release.TrySetResult();
                if (mode == "fail")
                {
                    await Join(runtime.QueuedOutputs.Console.Completion); Write("after-fault", CoreLogLevel.Information, 3); await FileWritten(3);
                }
            }
            runtime.CloseAdmission(); await Join(runtime.QueuedOutputs!.Completion);
            Write("closed", CoreLogLevel.Information, 4);
            byte[] console = physical ? [] : seam.ToArray(); string after = "";
            if (!physical) { seam.Write(Encoding.UTF8.GetBytes("application-still-open")); after = Encoding.UTF8.GetString(seam.ToArray().AsSpan(console.Length)); }
            return new JsonObject
            {
                ["ok"] = true,
                ["caseId"] = row["id"]!.DeepClone(),
                ["settings"] = new JsonObject
                {
                    ["format"] = options.ConsoleFormat.ToString(),
                    ["detail"] = options.HumanDetail.ToString(),
                    ["stream"] = options.ConsoleStream.ToString(),
                    ["consoleEnabled"] = options.ConsoleEnabled,
                    ["fileEnabled"] = options.FileEnabled,
                    ["consoleLevel"] = options.EffectiveConsoleLevel.ToString(),
                    ["fileLevel"] = options.EffectiveFileLevel.ToString(),
                    ["queueCapacity"] = options.QueueCapacity,
                    ["maxEntryBytes"] = options.MaxEntryBytes
                },
                ["results"] = results,
                ["factoryCalls"] = factories,
                ["clockCalls"] = clock.Calls,
                ["final"] = Project(runtime.GetDiagnostics()),
                ["checkpoints"] = checkpoints,
                ["selectedStreamArgument"] = selectedStream?.ToString(),
                ["borrowedDisposes"] = seam.Disposes,
                ["afterApplication"] = after,
                ["stdout"] = !physical && selectedStream == ConsoleOutputStream.StandardOutput ? Convert.ToBase64String(console) : "",
                ["stderr"] = !physical && selectedStream == ConsoleOutputStream.StandardError ? Convert.ToBase64String(console) : "",
                ["file"] = Convert.ToBase64String(file.ToArray()),
                ["resource"] = Resource(metadata)
            };
        }
        finally
        {
            seam.Release.TrySetResult(); runtime.CloseAdmission(); await Join(runtime.QueuedOutputs!.Completion);
        }
    }
    internal static string Message(int id) => "consumer-" + id.ToString(CultureInfo.InvariantCulture) + " caf\u00e9 \U0001F642\nnext";
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonObject Result(string label, LogWriteResult value) => new()
    {
        ["label"] = label,
        ["outcome"] = value.Outcome.ToString(),
        ["truncated"] = value.IsTruncated,
        ["outputs"] = new JsonArray(value.Outputs.Select(item => (JsonNode)new JsonObject
        { ["name"] = item.OutputName, ["status"] = item.Status.ToString(), ["ticket"] = item.AcceptedSequence?.ToString(CultureInfo.InvariantCulture) }).ToArray())
    };
    private static JsonObject Project(CoreLogDiagnostics value) => new()
    {
        ["state"] = value.State.ToString(),
        ["closed"] = value.AdmissionClosed,
        ["invalid"] = Number(value.InvalidEventCount),
        ["truncated"] = Number(value.TruncatedEventCount),
        ["outputs"] = new JsonArray(value.Outputs.Select(item => (JsonNode)new JsonObject
        {
            ["name"] = item.OutputName,
            ["state"] = item.State.ToString(),
            ["sequence"] = Number(item.AcceptedSequence),
            ["accepted"] = Number(item.Accepted),
            ["written"] = Number(item.Written),
            ["filtered"] = Number(item.Filtered),
            ["failed"] = Number(item.Failed),
            ["abandoned"] = Number(item.Abandoned),
            ["pending"] = Number(item.Pending),
            ["unavailable"] = Number(item.Unavailable),
            ["fault"] = item.LastFaultCode
        }).ToArray())
    };
    // Independently projected from public readonly metadata, not from captured/rendered bytes.
    private static JsonObject Resource(EventMetadata metadata)
    {
        string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var facts = new JsonObject
        {
            ["build_version"] = metadata.BuildVersion,
            ["instance_id"] = metadata.InstanceId,
            ["initialized_at"] = Timestamp(metadata.InitializedAtUtc),
            ["environment_kind"] = "console",
            ["os_family"] = metadata.OperatingSystem
        };
        if (metadata.ProcessStartedAtUtc is { } start) { facts["process_started_at"] = Timestamp(start); }
        if (metadata.ProcessId is { } pid) { facts["process_id"] = pid; }
        Add("host_name", metadata.HostName); Add("os_description", metadata.OperatingSystemDescription); Add("os_architecture", metadata.OSArchitecture);
        Add("process_architecture", metadata.ProcessArchitecture); Add("runtime_description", metadata.RuntimeDescription);
        return facts;
        void Add(string key, string? value) { if (value is not null) { facts[key] = value; } }
    }
    internal static int Run(string path, string id)
    {
        JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject(), row = catalog["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); ConsoleConsumerExpectations.Validate(catalog, row, actual);
        actual["observed"] = new JsonObject
        {
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ConsoleConsumerFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
            ["inputsSha256"] = Hash(path),
            ["processId"] = System.Environment.ProcessId
        };
        // With default stderr this one JSON value is the complete stdout command result.
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
