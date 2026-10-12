using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerCaptureFixture
{
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.AsObject();
        string mode = input["mode"]!.GetValue<string>();
        string payload = input["payload"]!.GetValue<string>();
        var console = new LoggerCaptureSources.Output(Plan(input["consolePlan"]!), input["consoleSeed"]!.GetValue<long>());
        var file = new LoggerCaptureSources.Output(Plan(input["filePlan"]!), input["fileSeed"]!.GetValue<long>());
        var clock = new LoggerFoundationFixture.Clock();
        var options = new LoggingOptions { FilePath = Path.Combine(Directory.GetCurrentDirectory(), "cis-no-output-created.jsonl"), MaxEntryBytes = 16384 };
        var runtime = new CoreLogRuntime(EventQualificationFixture.Metadata().CaptureMetadata(), options, console, file, clock);
        CoreLogger logger = runtime.CreateLogger("Capture");
        int count = mode is "invalid" or "sequence" ? 3 : 1;
        int calls = 0;
        var results = new JsonArray();
        var invocations = new JsonArray();
        var same = new JsonArray();
        var exceptions = new JsonArray();
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            for (int index = 0; index < count; index++)
            {
                int callsBefore = calls;
                bool invalid = mode == "invalid" && index == 1;
                LogEventData data = LoggerCaptureSources.Data(invalid || mode == "truncate" ? payload : "valid");
                Exception? supplied = invalid && payload == "throw-io" ? new IOException("CIS_FACTORY_PRIVATE_IO")
                    : invalid && payload == "throw-cancel" ? new OperationCanceledException("CIS_FACTORY_PRIVATE_CANCELLATION") : null;
                LogEventData Factory()
                {
                    calls++;
                    if (supplied is not null) { throw supplied; }
                    return invalid && payload == "null-result" ? null! : data;
                }
                try
                {
                    LogWriteResult result = input["method"]!.GetValue<string>() == "lazy"
                        ? logger.WriteLazy(CoreLogLevel.Information, Factory) : logger.Write(CoreLogLevel.Information, data);
                    results.Add(LoggerFoundationFixture.Observe(result)); same.Add(false); exceptions.Add((JsonNode?)null);
                }
                catch (Exception error)
                {
                    results.Add((JsonNode?)null); same.Add(ReferenceEquals(error, supplied)); exceptions.Add(error.GetType().Name);
                }
                invocations.Add(calls - callsBefore);
                data.Message = "CIS_CALLER_CHANGED_AFTER_CAPTURE";
            }
        }
        finally { Activity.Current = previous; }
        return new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["results"] = results,
            ["exceptionSame"] = same,
            ["exceptions"] = exceptions,
            ["factoryCalls"] = calls,
            ["factoryInvocations"] = invocations,
            ["clockCalls"] = clock.Calls,
            ["invalidCount"] = runtime.InvalidEventCount,
            ["truncatedCount"] = runtime.TruncatedEventCount,
            ["attempts"] = new JsonArray(console.Attempts, file.Attempts),
            ["accepted"] = new JsonArray(console.Events.Count, file.Events.Count),
            ["queueFull"] = new JsonArray(console.QueueFull, file.QueueFull),
            ["unavailable"] = new JsonArray(console.Unavailable, file.Unavailable),
            ["wires"] = new JsonArray(Bytes(console), Bytes(file)),
            ["sharedCaptures"] = file.Events.Where(item => console.Events.Any(other => ReferenceEquals(other, item))).Count()
        };
    }
    private static LogOutputWriteStatus[] Plan(JsonNode input) => input.AsArray().Select(item => Enum.Parse<LogOutputWriteStatus>(item!.GetValue<string>())).ToArray();
    private static JsonArray Bytes(LoggerCaptureSources.Output output) => new(output.Events.Select(item => (JsonNode)JsonValue.Create(Convert.ToBase64String(item.JsonLine.Span))!).ToArray());
}
