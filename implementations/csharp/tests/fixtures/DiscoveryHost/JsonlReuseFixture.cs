using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Actual Core produces the bytes. Joining them here is a framing probe outside admission, never an output worker.
internal static class JsonlReuseFixture
{
    private sealed class FixedException : Exception
    {
        internal FixedException() : base("failure\nquote \" and slash \\", new Exception("inner\ntext")) { Data["private"] = "CIS_EXCLUDED_EXCEPTION_DATA"; }
        public override string? StackTrace => "frame1\nframe2\r\n\t\u001b[31m";
    }
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.AsObject();
        string mode = input["mode"]!.GetValue<string>();
        int cap = input["maxEntryBytes"]!.GetValue<int>();
        var metadata = EventQualificationFixture.Metadata();
        if (mode is "absent" or "empty")
        {
            metadata.ProcessStartedAtUtc = null; metadata.HostName = null; metadata.OperatingSystemDescription = null;
            metadata.OSArchitecture = null; metadata.ProcessArchitecture = null; metadata.RuntimeDescription = null;
            metadata.ProcessId = null; metadata.CommitId = null; metadata.BuildId = null;
        }
        var console = new LoggerFoundationFixture.Output(); var file = new LoggerFoundationFixture.Output();
        var options = new LoggingOptions { Level = CoreLogLevel.Trace, FilePath = Path.Combine(Directory.GetCurrentDirectory(), "cis-no-output-created.jsonl"), MaxEntryBytes = cap };
        var clock = new LoggerFoundationFixture.Clock();
        var runtime = new CoreLogRuntime(metadata.CaptureMetadata(), options, console, file, clock);
        CoreLogger logger = runtime.CreateLogger("Jsonl");
        var results = new JsonArray(); int factoryCalls = 0;
        Activity? previous = Activity.Current; Activity.Current = null;
        try
        {
            foreach (JsonNode? item in input["messages"]!.AsArray())
            {
                var data = EventQualificationFixture.Data();
                data.EventName = "jsonl.event"; data.Message = item!.GetValue<string>();
                data.MessageTemplate = data.Message; data.Exception = new FixedException();
                var attributes = data.Attributes!.ToDictionary(pair => pair.Key, pair => pair.Value);
                attributes["text"] = LogValue.String(data.Message);
                if (mode == "cap") { attributes["zPayload"] = LogValue.String(new string('\u0001', 30000)); }
                data.Attributes = attributes;
                if (mode is "absent" or "empty")
                {
                    data.Attributes = mode == "empty" ? new Dictionary<string, LogValue> { ["null"] = LogValue.Null() } : null;
                    data.MessageTemplate = mode == "empty" ? "" : null; data.Exception = null; data.OperationId = null;
                }
                results.Add(LoggerFoundationFixture.Observe(logger.WriteLazy((CoreLogLevel)input["level"]!.GetValue<int>(), () => { factoryCalls++; return data; })));
                // Later caller/configuration changes cannot change retained bytes or the next event's runtime identity/cap.
                data.Message = "CIS_CHANGED_CALLER"; data.Exception = null; attributes.Clear();
                metadata.ApplicationName = "changed"; options.MaxEntryBytes = 16384;
            }
        }
        finally { Activity.Current = previous; }
        return new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["results"] = results,
            ["factoryCalls"] = factoryCalls,
            ["clockCalls"] = clock.Calls,
            ["invalidCount"] = runtime.InvalidEventCount,
            ["truncatedCount"] = runtime.TruncatedEventCount,
            ["sharedCaptures"] = file.Events.Count(item => console.Events.Any(other => ReferenceEquals(other, item))),
            ["reusedMemory"] = console.Events.All(item => item.JsonLine.Equals(item.JsonLine)),
            ["wires"] = new JsonArray(Bytes(console), Bytes(file)),
            ["stream"] = Convert.ToBase64String(console.Events.SelectMany(item => item.JsonLine.ToArray()).ToArray())
        };
    }
    private static JsonArray Bytes(LoggerFoundationFixture.Output output) => new(output.Events.Select(item => (JsonNode)JsonValue.Create(Convert.ToBase64String(item.JsonLine.Span))!).ToArray());
}
