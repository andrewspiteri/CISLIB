using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class EventCorrelationTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void LiteralClockAndActualActivityObserveOwnedFacts(string id)
    {
        JsonObject row = Row(id);
        EventCorrelationExpectations.Validate(row, EventCorrelationFixture.Execute(row));
    }
    [TestMethod]
    public void SystemClockIsSampledInsideCaptureAsUtc()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        CapturedEventFacts facts = Capture();
        DateTimeOffset after = DateTimeOffset.UtcNow;
        Assert.AreEqual(TimeSpan.Zero, facts.TimestampUtc.Offset);
        Assert.IsTrue(facts.TimestampUtc >= before && facts.TimestampUtc <= after);
    }
    [TestMethod]
    [DataRow("none")]
    [DataRow("negative")]
    [DataRow("above")]
    public void DisabledOrInvalidSeverityNeverReadsClock(string mode)
    {
        var clock = new EventCorrelationFixture.MutableClock(DateTimeOffset.MinValue, () => throw new InvalidOperationException("Clock must not be read"));
        CoreLogLevel severity = mode == "none" ? CoreLogLevel.None : (CoreLogLevel)(mode == "negative" ? -1 : 99);
        if (mode == "none") { Assert.IsNull(EventProjection.Capture(null!, null!, null!, severity, null!, clock)); }
        else { Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EventProjection.Capture(null!, null!, null!, severity, null!, clock)); }
        Assert.AreEqual(0, clock.Reads);
    }
    [TestMethod]
    [DataRow("clock")]
    [DataRow("exception")]
    public void CallerCallbacksCannotReplaceAlreadySampledTimeIdsOrNamedInputReferences(string mode)
    {
        Activity? previous = Activity.Current; Activity? replacement = null;
        Activity.Current = null;
        using var activity = new Activity("original").SetIdFormat(ActivityIdFormat.W3C).Start();
        string trace = activity.TraceId.ToHexString(), span = activity.SpanId.ToHexString();
        var data = new LogEventData
        {
            EventName = "original",
            Message = "original",
            OperationId = "original",
            Attributes = new Dictionary<string, LogValue> { ["original"] = LogValue.Integer(1) }
        };
        var clock = new EventCorrelationFixture.MutableClock(DateTimeOffset.Parse("2026-10-11T10:11:12.1234567Z", CultureInfo.InvariantCulture));
        void Mutate()
        {
            data.EventName = "changed"; data.Message = "changed"; data.OperationId = "changed"; data.Attributes = null;
            activity.Stop(); Activity.Current = null; replacement = new Activity("replacement").SetIdFormat(ActivityIdFormat.W3C).Start();
            if (mode == "exception") { clock.Value = DateTimeOffset.MaxValue; }
        }
        TimeProvider selected = clock;
        if (mode == "clock") { selected = new EventCorrelationFixture.MutableClock(clock.Value, Mutate); }
        else { data.Exception = new CallbackException(Mutate); }
        try
        {
            CapturedEventFacts facts = Capture(data, selected);
            Assert.AreEqual(trace, facts.TraceId); Assert.AreEqual(span, facts.SpanId);
            Assert.AreEqual("2026-10-11T10:11:12.1234567Z", facts.TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            Assert.AreEqual("original", facts.EventName); Assert.AreEqual("original", facts.Message); Assert.AreEqual("original", facts.OperationId);
            Assert.IsTrue(facts.Attributes.MapProperties.ContainsKey("original")); Assert.AreEqual("changed", data.EventName);
            Assert.AreEqual(1, ((EventCorrelationFixture.MutableClock)selected).Reads);
        }
        finally { replacement?.Dispose(); activity.Stop(); Activity.Current = previous; }
    }
    private sealed class CallbackException(Action callback) : Exception
    {
        public override string Message { get { callback(); return "owned"; } }
    }
    [TestMethod]
    [DataRow("trace-zero")]
    [DataRow("span-zero")]
    [DataRow("trace-short")]
    [DataRow("span-short")]
    [DataRow("trace-upper")]
    [DataRow("span-upper")]
    [DataRow("trace-nonhex")]
    [DataRow("span-nonhex")]
    public void NativeClrFaultInjectionOmitsTheWholeInvalidPair(string mode)
    {
        // Test-only private CLR field injection reaches invalid states public Activity cannot construct.
        Activity? previous = Activity.Current; Activity.Current = null;
        using var activity = new Activity("fault").SetIdFormat(ActivityIdFormat.W3C).Start();
        bool trace = mode.StartsWith("trace", StringComparison.Ordinal); int length = trace ? 32 : 16;
        string value = mode.EndsWith("zero", StringComparison.Ordinal) ? new string('0', length)
            : mode.EndsWith("short", StringComparison.Ordinal) ? new string('1', length - 1)
            : new string(mode.EndsWith("upper", StringComparison.Ordinal) ? 'A' : 'g', length);
        FieldInfo field = typeof(Activity).GetField(trace ? "_traceId" : "_spanId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.IsNotNull(field, "Pinned CLR capability is unavailable"); field.SetValue(activity, value);
        try
        {
            Assert.AreSame(activity, Activity.Current);
            Assert.AreEqual(value, trace ? activity.TraceId.ToHexString() : activity.SpanId.ToHexString());
            CapturedEventFacts facts = Capture(new LogEventData { EventName = "event", Message = "", OperationId = "independent" });
            Assert.IsNull(facts.TraceId); Assert.IsNull(facts.SpanId); Assert.AreEqual("independent", facts.OperationId);
        }
        finally { activity.Stop(); Activity.Current = previous; }
    }
    [TestMethod]
    [DataRow("timestamp")]
    [DataRow("precision")]
    [DataRow("trace")]
    [DataRow("span")]
    [DataRow("partial")]
    [DataRow("zero")]
    [DataRow("operation")]
    [DataRow("extra")]
    public void CorrelationComparatorRejectsWrongTimeIdsAndPartialPairs(string mode)
    {
        JsonObject row = Row("ACTIVITY-W3C-PARENT"); JsonObject actual = EventCorrelationFixture.Execute(row);
        JsonObject projection = actual["projection"]!.AsObject();
        switch (mode)
        {
            case "timestamp": projection["timestamp"] = "2026-10-11T13:11:12.1234567Z"; break;
            case "precision": projection["timestamp"] = "2026-10-11T10:11:12.123Z"; break;
            case "trace": projection["trace_id"] = new string('3', 32); break;
            case "span": projection["span_id"] = new string('2', 16); break;
            case "partial": projection.Remove("span_id"); break;
            case "zero": projection["span_id"] = new string('0', 16); break;
            case "operation": projection["operation_id"] = "scope-op"; break;
            case "extra": projection["baggage"] = "private"; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => EventCorrelationExpectations.Validate(row, actual));
    }
    [TestMethod]
    public void OwnedFactsDoNotKeepActivityOrClockAlive()
    {
        (CapturedEventFacts facts, WeakReference activity, WeakReference clock) = CaptureCollectible();
        for (int attempt = 0; attempt < 10 && (activity.IsAlive || clock.IsAlive); attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.IsFalse(activity.IsAlive); Assert.IsFalse(clock.IsAlive); Assert.IsNotNull(facts.TraceId); Assert.IsNotNull(facts.SpanId);
        GC.KeepAlive(facts);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CapturedEventFacts, WeakReference, WeakReference) CaptureCollectible()
    {
        Activity? previous = Activity.Current; Activity.Current = null;
        using var activity = new Activity("collectible").SetIdFormat(ActivityIdFormat.W3C).Start();
        var clock = new EventCorrelationFixture.MutableClock(DateTimeOffset.MinValue);
        try { return (Capture(clock: clock), new WeakReference(activity), new WeakReference(clock)); }
        finally { activity.Stop(); Activity.Current = previous; }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualInheritedOrSuppressedChildCapturesItsOwnCurrentPair(bool suppress)
    {
        Activity? previous = Activity.Current; Activity.Current = null;
        using var parent = new Activity("parent").SetIdFormat(ActivityIdFormat.W3C).Start();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(CapturedEventFacts Facts, string Trace, string Span)> worker;
        async Task<(CapturedEventFacts, string, string)> Work()
        {
            using var child = new Activity("child").SetIdFormat(ActivityIdFormat.W3C).Start();
            string trace = child.TraceId.ToHexString(), span = child.SpanId.ToHexString();
            ready.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return (Capture(), trace, span);
        }
        try
        {
            if (suppress) { using (ExecutionContext.SuppressFlow()) { worker = Task.Run(Work); } }
            else { worker = Task.Run(Work); }
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { release.TrySetResult(); await worker.WaitAsync(TimeSpan.FromSeconds(10)); }
            (CapturedEventFacts facts, string trace, string span) = await worker.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(trace, facts.TraceId); Assert.AreEqual(span, facts.SpanId); Assert.AreNotEqual(parent.SpanId.ToHexString(), span);
            if (suppress) { Assert.AreNotEqual(parent.TraceId.ToHexString(), trace); } else { Assert.AreEqual(parent.TraceId.ToHexString(), trace); }
            Assert.AreSame(parent, Activity.Current);
        }
        finally { release.TrySetResult(); parent.Stop(); Activity.Current = previous; }
    }
    [TestMethod]
    public void OriginalReservedCorrelationVectorUsesAnExplicitCurrentSpanBinding()
    {
        JsonObject original = LogValueTests.Read("event-capture-expectations.json")["cases"]!.AsArray()
            .Single(row => row!["id"]!.GetValue<string>() == "CAPTURE-RESERVED-CORRELATION")!.AsObject();
        JsonObject row = Row("ACTIVITY-PROTECTED-FREEFORM"); JsonObject input = row["input"]!.AsObject();
        input["metadata"]!["service"] = original["expected"]!["service"]!.DeepClone();
        input["event"]!["operationId"] = original["inputs"]!["event"]!["operationId"]!.DeepClone();
        input["event"]!["attributes"] = new JsonObject(original["inputs"]!["event"]!["attributes"]!.AsObject()
            .Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, new JsonObject { ["kind"] = "string", ["value"] = pair.Value!.DeepClone() })));
        JsonObject actual = EventCorrelationFixture.Execute(row); JsonNode facts = actual["projection"]!;
        Assert.AreEqual(original["expected"]!["service"]!.GetValue<string>(), facts["service"]!.GetValue<string>());
        Assert.AreEqual(original["expected"]!["operation_id"]!.GetValue<string>(), facts["operation_id"]!.GetValue<string>());
        Assert.AreEqual(original["expected"]!["trace_id"]!.GetValue<string>(), facts["trace_id"]!.GetValue<string>());
        Assert.AreEqual(original["expected"]!["span_id"]!.GetValue<string>(), actual["sourceActivity"]!["parentSpanId"]!.GetValue<string>());
        Assert.AreEqual(actual["sourceActivity"]!["spanId"]!.GetValue<string>(), facts["span_id"]!.GetValue<string>());
        foreach (var pair in original["expected"]!["attributes"]!.AsObject()) { Assert.IsTrue(JsonNode.DeepEquals(pair.Value, facts["attributes"]![pair.Key])); }
        // Original supplied span is a parent-span input in the public CLR binding; full wire vector is T05.
    }
    private static CapturedEventFacts Capture(LogEventData? data = null, TimeProvider? clock = null) => EventProjection.Capture(
        EventProjectionFixtureSources.Create(Catalog()["cases"]![0]!["input"]!["metadata"]!.AsObject(), out _, out _),
        new OperationScopeStore().Capture(), "Component", CoreLogLevel.Information, data ?? new LogEventData { EventName = "event", Message = "" }, clock)!;
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.DeepClone().AsObject();
    private static JsonObject Catalog() => LogValueTests.Read("event-correlation-cases.json");
}
