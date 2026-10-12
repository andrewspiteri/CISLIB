using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

// Complete real capture-to-wire probes. Holding facts across a barrier is not a logger queue.
internal static class EventQualificationFixture
{
    internal static readonly DateTimeOffset Instant = DateTimeOffset.Parse("2026-10-12T12:11:12.1234567+02:00", CultureInfo.InvariantCulture);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Instant; }
    private sealed class CountingClock : TimeProvider
    {
        internal int Calls { get; private set; }
        public override DateTimeOffset GetUtcNow() { Calls++; throw new InvalidOperationException("None must not read the clock."); }
    }
    private sealed class FixedAggregateException(IEnumerable<Exception> children) : AggregateException(children)
    {
        public override string Message => "root";
        public override string? StackTrace => null;
    }
    // Its real runtime name costs more bytes than the later System.Exception sibling.
    private sealed class ExceptionWithAnIntentionallyLongRuntimeTypeNameToExerciseOmissionOfAnIndivisibleTypeWhileRetainingALaterSmallSiblingWithoutShorteningAnyRetainedType : Exception;

    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        string mode = row["input"]!["mode"]!.GetValue<string>();
        int cap = row["input"]!["maxEntryBytes"]!.GetValue<int>();
        var result = new JsonObject { ["caseId"] = row["id"]!.DeepClone(), ["records"] = new JsonArray() };
        if (mode is "concurrent" or "stores")
        {
            await EventQualificationConcurrency.ExecuteAsync(row, result).ConfigureAwait(false);
            return result;
        }
        Activity? previous = Activity.Current;
        Activity.Current = null;
        var store = new OperationScopeStore();
        try
        {
            if (mode == "none")
            {
                var clock = new CountingClock();
                CapturedEventFacts? none = EventProjection.Capture(null!, null!, null!, CoreLogLevel.None, null!, clock);
                result["noneSkipped"] = none is null;
                result["clockCalls"] = clock.Calls;
                return result;
            }
            var metadata = Metadata();
            if (row["input"]!["environmentKind"] is JsonNode kind) { metadata.EnvironmentKind = Enum.Parse<EnvironmentKind>(kind.GetValue<string>()); }
            var data = Data();
            result["originalStack"] = data.Exception!.StackTrace;
            if (mode is "absent" or "empty")
            {
                metadata.ProcessStartedAtUtc = null; metadata.HostName = null; metadata.OperatingSystemDescription = null;
                metadata.OSArchitecture = null; metadata.ProcessArchitecture = null; metadata.RuntimeDescription = null;
                metadata.ProcessId = null; metadata.CommitId = null; metadata.BuildId = null;
                data = new LogEventData { EventName = "qualification.event", Message = "", MessageTemplate = mode == "empty" ? "" : null };
                Add(result, Capture(metadata, store, data), cap, "minimal", null);
            }
            else if (mode == "child") { Child(result, metadata, store, data, cap); }
            else if (mode == "aggregate") { Aggregate(result, metadata, store, cap); }
            else if (mode == "sibling") { Sibling(result, metadata, store, cap); }
            else if (mode is "fields" or "delayed")
            {
                using var activity = StartActivity("capture");
                JsonObject sampled = Sample(activity);
                var attrs = data.Attributes!.ToDictionary(pair => pair.Key, pair => pair.Value);
                data.Attributes = attrs;
                CapturedEventFacts facts;
                OperationContextData innerContext = Context("inner", "inner");
                if (mode == "delayed")
                {
                    data.OperationId = " ";
                    innerContext.Attributes = new Dictionary<string, LogValue> { ["shared"] = LogValue.String("inner"), ["scopeOnly"] = LogValue.String("original-scope") };
                }
                using (store.BeginScope(Context("outer", "outer")))
                using (store.BeginScope(innerContext))
                {
                    facts = Capture(metadata, store, data, Enum.Parse<CoreLogLevel>(row["input"]!["severity"]!.GetValue<string>()));
                }
                activity.Stop();
                if (mode == "delayed")
                {
                    metadata.ApplicationName = "changed"; metadata.CommitId = "changed"; metadata.Logging.MaxEntryBytes = 16384;
                    data.EventName = "changed"; data.Message = "changed"; data.MessageTemplate = null; data.OperationId = "changed";
                    data.Exception!.Data["later"] = "CIS_CHANGED_EXCEPTION_DATA";
                    bool exceptionDataChanged = (string?)data.Exception.Data["later"] == "CIS_CHANGED_EXCEPTION_DATA"; data.Exception = null;
                    attrs.Clear(); attrs["changed"] = LogValue.String("changed"); data.Attributes = null;
                    using var replacement = new Activity("replacement").SetIdFormat(ActivityIdFormat.W3C).Start();
                    using (store.BeginScope(new OperationContextData { OperationId = "replacement", Attributes = new Dictionary<string, LogValue> { ["scopeOnly"] = LogValue.String("replacement-scope") } }))
                    {
                        result["laterState"] = new JsonObject { ["operationId"] = store.Capture().OperationId, ["scopeOnly"] = store.Capture().Attributes.MapProperties["scopeOnly"].Scalar.Text, ["exceptionDataChanged"] = exceptionDataChanged, ["traceId"] = replacement.TraceId.ToHexString(), ["spanId"] = replacement.SpanId.ToHexString(), ["callerChanged"] = metadata.ApplicationName == "changed" && attrs.ContainsKey("changed") && data.Message == "changed" };
                        await Task.Yield();
                        Add(result, facts, cap, "captured", sampled);
                    }
                }
                else { Add(result, facts, cap, "captured", sampled); }
                result["storeEmpty"] = store.CurrentFrame is null;
            }
            else { throw new InvalidDataException("Unknown event qualification scenario."); }
            return result;
        }
        finally { Activity.Current = previous; }
    }

    private static void Child(JsonObject result, FixtureEnvironmentValues metadata, OperationScopeStore store, LogEventData data, int cap)
    {
        data.OperationId = null;
        using var parent = StartActivity("parent");
        JsonObject parentSample = Sample(parent);
        CapturedEventFacts before, child, after;
        JsonObject childSample;
        using (store.BeginScope(Context("outer", "outer")))
        {
            before = Capture(metadata, store, data);
            using (var nested = new Activity("child").SetIdFormat(ActivityIdFormat.W3C).Start())
            using (store.BeginScope(Context("child", "child")))
            {
                childSample = Sample(nested); child = Capture(metadata, store, data);
            }
            after = Capture(metadata, store, data);
        }
        parent.Stop();
        Add(result, before, cap, "parent-before", parentSample);
        Add(result, child, cap, "child", childSample);
        Add(result, after, cap, "parent-after", parentSample);
        result["storeEmpty"] = store.CurrentFrame is null;
    }

    private static void Aggregate(JsonObject result, FixtureEnvironmentValues metadata, OperationScopeStore store, int cap)
    {
        var data = Data();
        data.Attributes = new Dictionary<string, LogValue>
        {
            ["a"] = LogValue.String(string.Concat(Enumerable.Repeat("\U0001f600\u0001", cap))),
            ["z"] = LogValue.DecimalString("1.0000")
        };
        using var activity = StartActivity("aggregate");
        CapturedEventFacts facts = Capture(metadata, store, data);
        Add(result, facts, cap, "bounded", Sample(activity));
        // Re-encode the same owned input with a larger cap: byte fitting must not mutate it.
        AddUnboundedObservation(result, facts);
    }

    private static void AddUnboundedObservation(JsonObject result, CapturedEventFacts facts)
    {
        EncodedEvent large = EventEncoder.Encode(facts, 16777216);
        result["largerWire"] = Encoding.UTF8.GetString(large.JsonLine.Span);
        result["largerTruncated"] = large.IsTruncated;
    }

    private static void Sibling(JsonObject result, FixtureEnvironmentValues metadata, OperationScopeStore store, int cap)
    {
        var data = new LogEventData { EventName = "qualification.event", Message = "", Attributes = new Dictionary<string, LogValue> { ["fill"] = LogValue.String("") }, Exception = new FixedAggregateException([]) };
        metadata.OperatingSystemDescription = null; metadata.RuntimeDescription = null;
        // Derive filler from actual complete bytes; leave a literal 128 bytes for child structure.
        int baseline = EventEncoder.Encode(Capture(metadata, store, data), cap).JsonLine.Length;
        data.Attributes = new Dictionary<string, LogValue> { ["fill"] = LogValue.String(new string('x', cap - baseline - 128)) };
        data.Exception = new FixedAggregateException([new ExceptionWithAnIntentionallyLongRuntimeTypeNameToExerciseOmissionOfAnIndivisibleTypeWhileRetainingALaterSmallSiblingWithoutShorteningAnyRetainedType(), new Exception("small")]);
        Add(result, Capture(metadata, store, data), cap, "sibling", null);
    }

    internal static FixtureEnvironmentValues Metadata() => new()
    {
        ApplicationName = "Qualification.App",
        EnvironmentName = "qa",
        BuildVersion = "build+AbC",
        ServiceVersion = "release-4",
        EnvironmentKind = EnvironmentKind.Service,
        InitializedAtUtc = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero),
        ProcessStartedAtUtc = new(2026, 10, 12, 9, 59, 0, TimeSpan.Zero),
        ProcessId = 123,
        HostName = "node-1",
        OperatingSystem = "linux",
        OperatingSystemDescription = "OS description",
        OSArchitecture = "x64",
        ProcessArchitecture = "arm64",
        RuntimeDescription = "runtime description",
        CommitId = "commit-1",
        BuildId = "build-1"
    };

    internal static LogEventData Data() => new()
    {
        EventName = "qualification.event",
        Message = "quote \" slash \\ newline\n caf\u00e9 \U0001f600",
        MessageTemplate = "template {value}",
        OperationId = " event ",
        Exception = Failure(),
        Attributes = new Dictionary<string, LogValue>
        {
            ["null"] = LogValue.Null(),
            ["true"] = LogValue.Boolean(true),
            ["false"] = LogValue.Boolean(false),
            ["text"] = LogValue.String("quote \" slash \\ newline\n caf\u00e9 \U0001f600"),
            ["integer"] = LogValue.Integer(9007199254740991),
            ["number"] = LogValue.Number(1.25),
            ["integerString"] = LogValue.IntegerString("90071992547409920000"),
            ["decimal"] = LogValue.DecimalString("12.3400"),
            ["uuid"] = LogValue.Uuid(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
            ["instant"] = LogValue.Timestamp(Instant),
            ["array"] = LogValue.Array([LogValue.Integer(2), LogValue.Integer(1), LogValue.Null()]),
            ["map"] = LogValue.Map(new Dictionary<string, LogValue> { ["z"] = LogValue.String("last"), ["a"] = LogValue.Boolean(true) }),
            ["service"] = LogValue.String("nested service"),
            ["trace_id"] = LogValue.String("nested trace"),
            ["span_id"] = LogValue.String("nested span"),
            ["operation_id"] = LogValue.String("nested operation"),
            ["truncated"] = LogValue.Boolean(false),
            ["shared"] = LogValue.String("event")
        }
    };

    private static Exception Failure()
    {
        try { throw new InvalidOperationException("captured failure", new ArgumentException("inner")); }
        catch (InvalidOperationException error) { error.Data["secret"] = "CIS_EXCLUDED_EXCEPTION_DATA"; return error; }
    }
    internal static OperationContextData Context(string id, string shared) => new()
    {
        OperationId = id,
        Attributes = new Dictionary<string, LogValue> { ["shared"] = LogValue.String(shared), ["scopeOnly"] = LogValue.String("outer") }
    };
    internal static Activity StartActivity(string name) => new Activity(name).SetIdFormat(ActivityIdFormat.W3C)
        .SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start();
    internal static JsonObject Sample(Activity activity) => new() { ["traceId"] = activity.TraceId.ToHexString(), ["spanId"] = activity.SpanId.ToHexString() };
    internal static CapturedEventFacts Capture(FixtureEnvironmentValues metadata, OperationScopeStore store, LogEventData data, CoreLogLevel level = CoreLogLevel.Information) =>
        EventProjection.Capture(metadata.CaptureMetadata(), store.Capture(), "component", level, data, new Clock())!;
    internal static void Add(JsonObject result, CapturedEventFacts facts, int cap, string label, JsonObject? sampled)
    {
        EncodedEvent encoded = EventEncoder.Encode(facts, cap);
        result["records"]!.AsArray().Add(new JsonObject { ["label"] = label, ["wire"] = Encoding.UTF8.GetString(encoded.JsonLine.Span), ["utf8Bytes"] = encoded.JsonLine.Length, ["truncated"] = encoded.IsTruncated, ["sampled"] = sampled?.DeepClone() });
    }
}
