using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerOutcomeFixture
{
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.AsObject();
        string consoleMode = input["console"]!.GetValue<string>(), fileMode = input["file"]!.GetValue<string>();
        string scenario = input["scenario"]!.GetValue<string>(), payload = input["payload"]!.GetValue<string>();
        CoreLogLevel level = (CoreLogLevel)input["level"]!.GetValue<int>();
        var options = new LoggingOptions
        {
            ConsoleLevel = (CoreLogLevel)input["consoleThreshold"]!.GetValue<int>(),
            FileLevel = (CoreLogLevel)input["fileThreshold"]!.GetValue<int>(),
            FilePath = Path.Combine(Directory.GetCurrentDirectory(), "cis-no-output-created.jsonl"),
            MaxEntryBytes = 16384
        };
        var console = new LoggerOutcomeSources.Output(consoleMode, 10, input["consoleCapacity"]?.GetValue<int>() ?? int.MaxValue);
        var file = new LoggerOutcomeSources.Output(fileMode, 100, input["fileCapacity"]?.GetValue<int>() ?? int.MaxValue);
        var clock = new LoggerFoundationFixture.Clock();
        var runtime = new CoreLogRuntime(EventQualificationFixture.Metadata().CaptureMetadata(), options,
            options.ConsoleEnabled ? console : null, options.FileEnabled ? file : null, clock);
        if (input["closed"]!.GetValue<bool>()) { runtime.CloseAdmission(); }
        CoreLogger logger = runtime.CreateLogger("Outcomes");
        var actual = new JsonObject { ["caseId"] = row["id"]!.DeepClone(), ["enabledBefore"] = logger.IsEnabled(level) };
        var trace = new List<string>();
        var results = new JsonArray(); var invocations = new JsonArray();
        var same = new JsonArray(); var exceptions = new JsonArray();
        // Repeated hints cannot touch payload, count attempts or reserve capacity.
        logger.IsEnabled(level); logger.IsEnabled(level);
        if (console.Attempts != 0 || file.Attempts != 0 || console.Filtered != 0 || file.Filtered != 0 || clock.Calls != 0)
        {
            throw new InvalidDataException("IsEnabled changed admission observations.");
        }
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            int count = scenario is "capacity" or "sticky" ? 2 : 1;
            for (int index = 0; index < count; index++)
            {
                int calls = 0;
                var data = new LogEventData
                {
                    EventName = "outcome.event",
                    Message = "original",
                    Attributes = new LoggerDecisionSources.Attributes(trace, () => { }, payload == "poison"),
                    Exception = new LoggerDecisionSources.ObservedException(trace, payload == "poison", () => { })
                };
                var supplied = new IOException("CIS_FACTORY_PRIVATE_IO");
                LogEventData Factory() { calls++; if (payload == "throw") { throw supplied; } return data; }
                try
                {
                    LogWriteResult result = input["method"]!.GetValue<string>() == "lazy"
                        ? logger.WriteLazy(level, Factory) : logger.Write(level, data);
                    results.Add(LoggerFoundationFixture.Observe(result)); same.Add(false); exceptions.Add((JsonNode?)null);
                }
                catch (Exception error)
                {
                    results.Add((JsonNode?)null); same.Add(ReferenceEquals(error, supplied)); exceptions.Add(error.GetType().Name);
                }
                invocations.Add(calls);
                data.Message = "CIS_CALLER_CHANGED_AFTER_CAPTURE";
                if (count == 2 && index == 0) { actual["enabledAfterFirst"] = logger.IsEnabled(level); }
            }
        }
        finally { Activity.Current = previous; }
        actual["enabledAfter"] = logger.IsEnabled(level);
        actual["results"] = results; actual["factoryInvocations"] = invocations;
        actual["exceptionSame"] = same; actual["exceptions"] = exceptions;
        actual["clockCalls"] = clock.Calls;
        actual["payloadTrace"] = new JsonArray(trace.Select(item => (JsonNode)JsonValue.Create(item)!).ToArray());
        actual["invalidCount"] = runtime.InvalidEventCount; actual["truncatedCount"] = runtime.TruncatedEventCount;
        actual["attempts"] = new JsonArray(console.Attempts, file.Attempts);
        actual["tryCalls"] = new JsonArray(console.TryCalls, file.TryCalls);
        actual["filtered"] = new JsonArray(console.Filtered, file.Filtered);
        actual["unavailable"] = new JsonArray(console.Unavailable, file.Unavailable);
        actual["accepted"] = new JsonArray(console.Events.Count, file.Events.Count);
        actual["queueFull"] = new JsonArray(console.QueueFull, file.QueueFull);
        actual["wires"] = new JsonArray(Bytes(console), Bytes(file));
        actual["sharedCaptures"] = file.Events.Count(item => console.Events.Any(other => ReferenceEquals(other, item)));
        return actual;
    }
    private static JsonArray Bytes(LoggerOutcomeSources.Output output) => new(output.Events.Select(item => (JsonNode)JsonValue.Create(Convert.ToBase64String(item.JsonLine.Span))!).ToArray());
}
