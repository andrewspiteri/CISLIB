using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

// Observes owned facts with a real CLR Activity and a controlled clock; not a wire encoder.
internal static class EventCorrelationFixture
{
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.DeepClone().AsObject();
        EnvironmentSnapshot snapshot = EventProjectionFixtureSources.Create(input["metadata"]!.AsObject(), out EnvironmentSetup setup, out _);
        LogEventData data = EventProjectionFixture.Input(input["event"]!.AsObject());
        JsonObject scope = input["scope"]!.AsObject();
        var store = new OperationScopeStore();
        var clock = new MutableClock(DateTimeOffset.Parse(input["clock"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        Activity? previous = Activity.Current;
        Activity? candidate = null;
        Activity? replacement = null;
        JsonObject result;
        try
        {
            Activity.Current = null;
            candidate = CreateActivity(input["activity"]!["mode"]!.GetValue<string>());
            JsonObject? source = Observe(Activity.Current); // Independent source observation before Core capture.
            CapturedEventFacts facts;
            JsonObject before;
            bool headUnchanged;
            using (IDisposable owner = store.BeginScope(new OperationContextData
            {
                OperationId = scope["operationId"]?.GetValue<string>(),
                Attributes = scope["attributes"]!.AsObject().ToDictionary(pair => pair.Key,
                    pair => LogValueFixtureAdapter.Construct(pair.Value!.AsObject(), () => { }), StringComparer.Ordinal)
            }))
            {
                OperationScopeFrame? head = store.CurrentFrame;
                facts = EventProjection.Capture(snapshot, store.Capture(), input["component"]!.GetValue<string>(),
                    Enum.Parse<CoreLogLevel>(input["severity"]!.GetValue<string>()), data, clock)!;
                headUnchanged = ReferenceEquals(head, store.CurrentFrame);
                before = Project(facts);
            }
            clock.Value = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            candidate?.SetTag("CIS_NEVER_CAPTURE_ACTIVITY", "private");
            candidate?.AddBaggage("CIS_NEVER_CAPTURE_ACTIVITY", "private");
            candidate?.Stop();
            Activity.Current = null;
            replacement = new Activity("replacement").SetIdFormat(ActivityIdFormat.W3C).Start();
            data.EventName = "changed"; data.Message = "changed"; data.MessageTemplate = "changed"; data.OperationId = "changed";
            if (data.Attributes is Dictionary<string, LogValue> attributes) { attributes.Clear(); attributes["changed"] = LogValue.Null(); }
            data.Attributes = null; setup.ApplicationName = "Changed.App";
            JsonObject after = Project(facts);
            result = new JsonObject
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["projection"] = after,
                ["sourceActivity"] = source,
                ["clockReads"] = clock.Reads,
                ["scopeHeadUnchanged"] = headUnchanged,
                ["stableAfterMutation"] = JsonNode.DeepEquals(before, after),
                ["callerMutationApplied"] = data.EventName == "changed" && setup.ApplicationName == "Changed.App"
                    && clock.Value.Year == 2027 && ReferenceEquals(Activity.Current, replacement)
            };
        }
        finally
        {
            replacement?.Dispose(); candidate?.Dispose(); Activity.Current = previous;
        }
        result["currentRestored"] = ReferenceEquals(previous, Activity.Current);
        return result;
    }

    internal static Activity? CreateActivity(string mode)
    {
        if (mode == "absent") { return null; }
        if (mode is not ("hierarchical" or "parent" or "zero-trace" or "zero-parent-span" or "root" or "unstarted" or "stopped")) { throw new InvalidDataException("Unknown activity mode."); }
        var activity = new Activity("CIS_NEVER_CAPTURE_ACTIVITY");
        activity.SetIdFormat(mode == "hierarchical" ? ActivityIdFormat.Hierarchical : ActivityIdFormat.W3C);
        if (mode is "parent" or "zero-trace" or "zero-parent-span")
        {
            activity.SetParentId(mode == "zero-trace" ? default : ActivityTraceId.CreateFromString("11111111111111111111111111111111"),
                mode == "zero-parent-span" ? default : ActivitySpanId.CreateFromString("2222222222222222"), ActivityTraceFlags.Recorded);
        }
        if (mode == "unstarted") { Activity.Current = activity; }
        else
        {
            activity.Start();
            if (mode == "stopped") { activity.Stop(); Activity.Current = activity; }
        }
        activity.SetTag("CIS_NEVER_CAPTURE_ACTIVITY", "private"); activity.AddBaggage("CIS_NEVER_CAPTURE_ACTIVITY", "private");
        return activity;
    }
    internal static JsonObject? Observe(Activity? activity) => activity is null ? null : new JsonObject
    {
        ["format"] = activity.IdFormat.ToString(),
        ["traceId"] = activity.TraceId.ToHexString(),
        ["spanId"] = activity.SpanId.ToHexString(),
        ["parentSpanId"] = activity.ParentSpanId.ToHexString()
    };
    internal static JsonObject Project(CapturedEventFacts facts)
    {
        JsonObject result = EventProjectionFixture.Project(facts)!;
        result["timestamp"] = facts.TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        if (facts.TraceId is not null) { result["trace_id"] = facts.TraceId; }
        if (facts.SpanId is not null) { result["span_id"] = facts.SpanId; }
        return result;
    }
    internal sealed class MutableClock(DateTimeOffset value, Action? onRead = null) : TimeProvider
    {
        internal DateTimeOffset Value { get; set; } = value;
        internal int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; onRead?.Invoke(); return Value; }
    }
}
