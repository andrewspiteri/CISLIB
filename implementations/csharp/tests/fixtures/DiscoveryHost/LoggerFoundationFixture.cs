using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

// Admission double only: real Core logger/capture/encoder, no simulated writer or delivery.
internal static class LoggerFoundationFixture
{
    internal sealed class Output : ILogOutputAdmission
    {
        internal List<EncodedEvent> Events { get; } = [];
        internal int Capacity { get; set; } = int.MaxValue;
        internal int Attempts { get; private set; }
        internal int Filtered { get; private set; }
        internal int Unavailable { get; private set; }
        public bool IsFaulted { get; set; }
        public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence)
        {
            Attempts++;
            acceptedSequence = null;
            if (Events.Count >= Capacity) { return LogOutputWriteStatus.QueueFull; }
            Events.Add(captured);
            acceptedSequence = Events.Count;
            return LogOutputWriteStatus.Accepted;
        }
        public void RecordFiltered() => Filtered++;
        public void RecordUnavailable() { Unavailable++; Attempts++; }
    }
    internal sealed class Clock : TimeProvider
    {
        internal int Calls { get; private set; }
        public override DateTimeOffset GetUtcNow() { Calls++; return EventQualificationFixture.Instant; }
    }

    internal static JsonObject Execute(JsonObject row)
    {
        string mode = row["input"]!["mode"]!.GetValue<string>();
        CoreLogLevel level = Enum.Parse<CoreLogLevel>(row["input"]!["level"]!.GetValue<string>());
        var options = new LoggingOptions { Level = CoreLogLevel.Trace };
        var console = new Output();
        var file = new Output();
        var clock = new Clock();
        if (mode == "union")
        {
            options.Level = CoreLogLevel.Information;
            options.ConsoleLevel = CoreLogLevel.Warning;
            options.FileLevel = CoreLogLevel.Debug;
            options.FilePath = Path.Combine(Directory.GetCurrentDirectory(), "cis-no-output-created.jsonl");
        }
        else if (mode == "truncate") { options.MaxEntryBytes = 16384; }
        else if (mode == "disabled") { options.Level = CoreLogLevel.None; }
        else if (mode == "fanout") { options.FilePath = Path.Combine(Directory.GetCurrentDirectory(), "cis-no-output-created.jsonl"); }
        var metadata = EventQualificationFixture.Metadata();
        metadata.ApplicationName = "Logger.App";
        var runtime = new CoreLogRuntime(metadata.CaptureMetadata(), options, options.ConsoleEnabled ? console : null, options.FileEnabled ? file : null, clock);
        CoreLogger logger = runtime.CreateLogger(component: "Orders");
        var data = new LogEventData { EventName = "logger.event", Message = "original" };
        int factoryCalls = 0;
        LogEventData Factory() { factoryCalls++; return data; }
        var results = new List<LogWriteResult>();
        var actual = new JsonObject { ["caseId"] = row["id"]!.DeepClone(), ["enabledBefore"] = logger.IsEnabled(level) };
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            if (mode == "full")
            {
                console.Capacity = 1;
                results.Add(logger.Write(level, data));
                actual["enabledWhenFull"] = logger.IsEnabled(level);
                results.Add(logger.Write(level, data));
            }
            else if (mode == "settings")
            {
                options.Level = CoreLogLevel.None;
                options.ConsoleLevel = CoreLogLevel.None;
                options.MaxEntryBytes = 16384;
                data.Attributes = new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('x', 30000)) };
                results.Add(logger.Write(level, data));
            }
            else if (mode == "fanout")
            {
                using (var activity = EventQualificationFixture.StartActivity("logger"))
                using (runtime.Scopes.BeginScope(new OperationContextData { OperationId = "scope-op", Attributes = new Dictionary<string, LogValue> { ["tenant"] = LogValue.String("original") } }))
                {
                    results.Add(logger.Write(level, data));
                    actual["sampled"] = EventQualificationFixture.Sample(activity);
                }
                data.Message = "changed";
                metadata.ApplicationName = "changed";
                actual["sameCapture"] = ReferenceEquals(console.Events[0], file.Events[0]);
                actual["storeEmpty"] = runtime.Scopes.CurrentFrame is null;
            }
            else if (mode == "truncate")
            {
                data.Attributes = new Dictionary<string, LogValue> { ["attachment"] = LogValue.String(new string('x', 50000)) };
                results.Add(logger.Write(level, data));
            }
            else if (mode == "invalid") { data.EventName = ""; results.Add(logger.Write(level, data)); }
            else if (mode == "null-result") { results.Add(logger.WriteLazy(level, () => { factoryCalls++; return null!; })); }
            else if (mode == "caller-throws")
            {
                var expected = new IOException("CIS_CALLER_EXCEPTION_SENTINEL");
                try { results.Add(logger.WriteLazy(level, () => { factoryCalls++; throw expected; })); }
                catch (IOException error) { actual["sameCallerException"] = ReferenceEquals(error, expected); }
            }
            else if (mode == "stopped")
            {
                runtime.CloseAdmission();
                actual["enabledAfterClose"] = logger.IsEnabled(level);
                results.Add(logger.WriteLazy(level, Factory));
                actual["loggerCreatedAfterClose"] = !runtime.CreateLogger("After").IsEnabled(level);
            }
            else if (mode == "isolation")
            {
                var otherOutput = new Output();
                var other = new CoreLogRuntime(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions(), otherOutput, null);
                using (runtime.Scopes.BeginScope(new OperationContextData { OperationId = "first" }))
                using (other.Scopes.BeginScope(new OperationContextData { OperationId = "second" }))
                {
                    results.Add(logger.Write(level, data));
                    other.CreateLogger("Other").Write(level, data);
                }
                actual["otherWire"] = Encoding.UTF8.GetString(otherOutput.Events[0].JsonLine.Span);
            }
            else if (mode is "lazy" or "union" or "disabled") { results.Add(logger.WriteLazy(level, Factory)); }
            else { results.Add(logger.Write(level, data)); }
        }
        finally { Activity.Current = previous; }
        actual["results"] = new JsonArray(results.Select(result => (JsonNode)Observe(result)).ToArray());
        actual["console"] = Observe(console);
        actual["file"] = Observe(file);
        actual["clockCalls"] = clock.Calls;
        actual["factoryCalls"] = factoryCalls;
        actual["invalidCount"] = runtime.InvalidEventCount;
        actual["truncatedCount"] = runtime.TruncatedEventCount;
        return actual;
    }

    internal static JsonObject Observe(LogWriteResult result) => new()
    {
        ["outcome"] = result.Outcome.ToString(),
        ["errorCode"] = result.ErrorCode,
        ["truncated"] = result.IsTruncated,
        ["outputs"] = new JsonArray(result.Outputs.Select(output => (JsonNode)new JsonObject
        {
            ["name"] = output.OutputName,
            ["status"] = output.Status.ToString(),
            ["sequence"] = output.AcceptedSequence
        }).ToArray())
    };
    private static JsonObject Observe(Output output) => new()
    {
        ["attempts"] = output.Attempts,
        ["filtered"] = output.Filtered,
        ["unavailable"] = output.Unavailable,
        ["wireBase64"] = new JsonArray(output.Events.Select(item => (JsonNode)JsonValue.Create(Convert.ToBase64String(item.JsonLine.Span))!).ToArray()),
        ["wires"] = new JsonArray(output.Events.Select(item => (JsonNode)JsonValue.Create(new UTF8Encoding(false, true).GetString(item.JsonLine.Span))!).ToArray())
    };
}
