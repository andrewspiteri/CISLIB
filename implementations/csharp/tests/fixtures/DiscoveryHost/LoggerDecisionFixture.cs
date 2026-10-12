using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerDecisionFixture
{
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.AsObject();
        string state = input["state"]!.GetValue<string>();
        string action = input["action"]!.GetValue<string>();
        string payload = input["payload"]!.GetValue<string>();
        var trace = new List<string>();
        var console = new LoggerDecisionSources.Output("console", trace);
        var file = new LoggerDecisionSources.Output("file", trace);
        LoggingOptions options = Options(state);
        ConfigureOutputs(state, action, console, file);
        CoreLogRuntime? runtime = null;
        var closes = new List<Task>();
        bool closeTimedOut = false;
        void Close()
        {
            trace.Add("close");
            Task close = Task.Run(runtime!.CloseAdmission);
            closes.Add(close);
            if (!close.Wait(TimeSpan.FromSeconds(5)))
            {
                closeTimedOut = true;
                throw new TimeoutException("Caller work held the admission gate.");
            }
        }
        void AttributesAction()
        {
            if (action == "close-attributes") { Close(); }
            if (action == "fault-attributes") { trace.Add("fault"); console.Faulted = true; file.Faulted = true; }
        }
        var clock = new LoggerDecisionSources.Clock(trace, () => { if (action == "close-clock") { Close(); } });
        runtime = new CoreLogRuntime(EventQualificationFixture.Metadata().CaptureMetadata(), options,
            options.ConsoleEnabled ? console : null, options.FileEnabled ? file : null, clock);
        CoreLogger logger = runtime.CreateLogger("Decisions");
        if (state.StartsWith("closed", StringComparison.Ordinal)) { runtime.CloseAdmission(); }
        var data = new LogEventData
        {
            EventName = payload == "bad-name" ? "" : "decision.event",
            Message = payload == "long" ? new string('x', 9000) : "original",
            Attributes = new LoggerDecisionSources.Attributes(trace, AttributesAction, payload == "poison"),
            Exception = new LoggerDecisionSources.ObservedException(trace, payload == "poison", () => { if (action == "close-exception") { Close(); } })
        };
        LogEventData Factory()
        {
            trace.Add("factory");
            if (action == "close-factory") { Close(); }
            return data;
        }
        LogWriteResult? result = null;
        Exception? error = null;
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            CoreLogLevel level = (CoreLogLevel)input["level"]!.GetValue<int>();
            bool missing = input["argument"]!.GetValue<string>() == "null";
            try
            {
                result = input["method"]!.GetValue<string>() == "lazy"
                    ? logger.WriteLazy(level, missing ? null! : Factory)
                    : logger.Write(level, missing ? null! : data);
            }
            catch (Exception caught) { error = caught; }
        }
        finally
        {
            Activity.Current = previous;
            // Join every launched close even on failed capture; no detached background probes.
            if (!Task.WaitAll(closes.ToArray(), TimeSpan.FromSeconds(5))) { throw new TimeoutException("Close probe did not finish."); }
        }
        JsonObject observed = Observe(row, trace, result, error, runtime, console, file);
        observed["closeTimedOut"] = closeTimedOut;
        return observed;
    }

    private static LoggingOptions Options(string state) => new()
    {
        Level = CoreLogLevel.Information,
        ConsoleLevel = state.EndsWith("disabled", StringComparison.Ordinal) ? CoreLogLevel.None : state is "below-faulted" or "file-faulted" or "console-faulted" ? CoreLogLevel.Warning : CoreLogLevel.Information,
        FileLevel = state.EndsWith("disabled", StringComparison.Ordinal) ? CoreLogLevel.None : state == "below-faulted" ? CoreLogLevel.Warning : state is "file-faulted" or "console-faulted" ? CoreLogLevel.Debug : CoreLogLevel.Information,
        FilePath = Path.Combine(Directory.GetCurrentDirectory(), "cis-no-output-created.jsonl")
    };

    private static void ConfigureOutputs(string state, string action, LoggerDecisionSources.Output console, LoggerDecisionSources.Output file)
    {
        console.Faulted = state is "faulted" or "closed-faulted" or "below-faulted" or "console-faulted" or "console-only-faulted";
        file.Faulted = state is "faulted" or "closed-faulted" or "below-faulted" or "file-faulted" or "file-only-faulted";
        console.Full = file.Full = action == "full";
    }

    private static JsonObject Observe(JsonObject row, List<string> trace, LogWriteResult? result, Exception? error,
        CoreLogRuntime runtime, LoggerDecisionSources.Output console, LoggerDecisionSources.Output file) => new()
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["trace"] = new JsonArray(trace.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
            ["result"] = result is null ? null : LoggerFoundationFixture.Observe(result),
            ["exception"] = error?.GetType().Name,
            ["parameter"] = (error as ArgumentException)?.ParamName,
            ["invalidCount"] = runtime.InvalidEventCount,
            ["truncatedCount"] = runtime.TruncatedEventCount,
            ["readsFaultState"] = console.FaultReads + file.FaultReads > 0,
            ["attempts"] = new JsonArray(console.Attempts, file.Attempts),
            ["filtered"] = new JsonArray(console.Filtered, file.Filtered),
            ["unavailable"] = new JsonArray(console.Unavailable, file.Unavailable),
            ["wires"] = new JsonArray(new JsonArray(console.Events.Select(item => (JsonNode)JsonValue.Create(Convert.ToBase64String(item.JsonLine.Span))!).ToArray()),
                new JsonArray(file.Events.Select(item => (JsonNode)JsonValue.Create(Convert.ToBase64String(item.JsonLine.Span))!).ToArray())),
            ["sameCapture"] = console.Events.Count == 1 && file.Events.Count == 1 ? ReferenceEquals(console.Events[0], file.Events[0]) : null
        };
}
