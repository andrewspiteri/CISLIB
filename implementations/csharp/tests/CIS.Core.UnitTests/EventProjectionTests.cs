using System.Collections;
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
[DoNotParallelize]
public sealed class EventProjectionTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void EverySharedProjectionExecutesAgainstOwnedCoreFacts(string id)
    {
        JsonObject catalog = Catalog();
        JsonObject row = catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), EventProjectionFixture.Execute(row));
    }

    [TestMethod]
    public void PublicNamedInputMatchesThePreservedReferenceShape()
    {
        Type type = typeof(LogEventData);
        Assert.AreEqual("CIS.Logging.LogEventData", type.FullName);
        Assert.IsTrue(type.IsSealed && type.IsPublic);
        Assert.IsNotNull(type.GetConstructor(Type.EmptyTypes));
        var names = new[] { "EventName", "Message", "MessageTemplate", "OperationId", "Attributes", "Exception" };
        CollectionAssert.AreEquivalent(names, type.GetProperties().Select(property => property.Name).ToArray());
        foreach (string name in names) { Assert.IsNotNull(type.GetProperty(name)!.SetMethod); }
        Assert.AreEqual(typeof(IReadOnlyDictionary<string, LogValue>), type.GetProperty("Attributes")!.PropertyType);
        Assert.AreEqual(typeof(Exception), type.GetProperty("Exception")!.PropertyType);
        foreach (string name in names)
        {
            Assert.AreEqual(name is "EventName" or "Message" ? NullabilityState.NotNull : NullabilityState.Nullable,
                new NullabilityInfoContext().Create(type.GetProperty(name)!).ReadState);
        }
        var input = new LogEventData();
        Assert.AreEqual(string.Empty, input.Message);
        Assert.IsNull(type.GetProperty("Severity"));
        Assert.IsNull(type.GetProperty("Level"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CapturedFactsSurviveDtoDictionaryExceptionAndScopeReplacement(bool wrapper)
    {
        var store = new OperationScopeStore();
        var source = new Dictionary<string, LogValue> { ["explicit"] = LogValue.Null() };
        var error = new MutableException { Text = "original" };
        var data = new LogEventData
        {
            EventName = "event",
            Message = "message",
            OperationId = " event-id ",
            Exception = error,
            Attributes = wrapper ? new System.Collections.ObjectModel.ReadOnlyDictionary<string, LogValue>(source) : source
        };
        CapturedEventFacts captured;
        using (store.BeginScope(new OperationContextData { OperationId = "scope", Attributes = new Dictionary<string, LogValue> { ["scope"] = LogValue.Integer(1) } }))
        {
            captured = EventProjection.Capture(Snapshot(), store.Capture(), "Component", CoreLogLevel.Warning, data)!;
        }
        JsonObject before = EventProjectionFixture.Project(captured)!;
        data.EventName = "changed"; data.Message = "changed"; data.OperationId = "changed";
        data.Exception = new Exception("replacement"); error.Text = "changed";
        source.Clear(); source["changed"] = LogValue.String("changed");
        using IDisposable other = store.BeginScope(new OperationContextData { OperationId = "replacement" });
        Assert.IsTrue(JsonNode.DeepEquals(before, EventProjectionFixture.Project(captured)));
        Assert.AreEqual("original", captured.Exception!.Message);
        Assert.AreEqual("event-id", captured.OperationId);
        Assert.AreEqual(LogValueKind.Null, captured.Attributes.MapProperties["explicit"].Kind);
    }

    [TestMethod]
    public void EventFactsDoNotRetainStartupInputsConfigurationCallerExceptionOrScopeStore()
    {
        (CapturedEventFacts facts, WeakReference[] sources) = CreateCollectibleInputs();
        for (int pass = 0; pass < 8 && sources.Any(source => source.IsAlive); pass++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.IsTrue(sources.All(source => !source.IsAlive));
        Assert.AreEqual("message", facts.Message);
        Assert.AreEqual("boom", facts.Exception!.Message);
        GC.KeepAlive(facts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CapturedEventFacts, WeakReference[]) CreateCollectibleInputs()
    {
        EventMetadata snapshot = EventProjectionFixtureSources.Create(Metadata(), out JsonObject source);
        var attributes = new Dictionary<string, LogValue> { ["value"] = LogValue.Null() };
        var exception = new Exception("boom");
        var attachment = new object(); exception.Data["attachment"] = attachment;
        var data = new LogEventData { EventName = "event", Message = "message", Attributes = attributes, Exception = exception };
        var store = new OperationScopeStore();
        CapturedEventFacts facts;
        using (store.BeginScope(new OperationContextData { OperationId = "scope" }))
        {
            facts = EventProjection.Capture(snapshot, store.Capture(), "Component", CoreLogLevel.Information, data)!;
        }
        WeakReference[] references = new object[] { snapshot, EnvironmentStoreTestData.Values.Logging, source, data, attributes, exception, attachment, store }
            .Select(value => new WeakReference(value)).ToArray();
        // Caller replaces its store reference; the captured event must not retain the old settings.
        EnvironmentStoreTestData.Values.Logging = new LoggingOptions();
        return (facts, references);
    }

    [TestMethod]
    [DataRow("message", "ascii", 8192)]
    [DataRow("message", "ascii", 8193)]
    [DataRow("message", "astral", 8192)]
    [DataRow("message", "astral", 8193)]
    [DataRow("template", "ascii", 8192)]
    [DataRow("template", "ascii", 8193)]
    [DataRow("template", "astral", 8192)]
    [DataRow("template", "astral", 8193)]
    public void PayloadScalarBoundsPreserveTextAndMarkOnlyActualClipping(string field, string pattern, int count)
    {
        string unit = pattern == "astral" ? "\U0001F600" : "x";
        string text = string.Concat(Enumerable.Repeat(unit, count));
        var data = new LogEventData { EventName = "event", Message = field == "message" ? text : "", MessageTemplate = field == "template" ? text : null };
        CapturedEventFacts facts = Capture(data);
        Assert.AreEqual(string.Concat(Enumerable.Repeat(unit, Math.Min(count, 8192))), field == "message" ? facts.Message : facts.MessageTemplate);
        Assert.AreEqual(count > 8192, facts.IsTruncated);
    }

    [TestMethod]
    [DataRow("component-empty")]
    [DataRow("component-null")]
    [DataRow("component-control")]
    [DataRow("component-long")]
    [DataRow("component-malformed")]
    [DataRow("event-empty")]
    [DataRow("event-null")]
    [DataRow("event-control")]
    [DataRow("event-long")]
    [DataRow("event-malformed")]
    [DataRow("message-null")]
    [DataRow("message-high")]
    [DataRow("message-low")]
    [DataRow("message-suffix")]
    [DataRow("template-high")]
    [DataRow("template-low")]
    [DataRow("template-suffix")]
    [DataRow("operation-control")]
    [DataRow("operation-long")]
    [DataRow("operation-malformed")]
    public void InvalidNamesIdentifiersAndMalformedPayloadsFailWithoutPublishingFacts(string kind)
    {
        var data = new LogEventData { EventName = "event", Message = "message" };
        string component = "Component";
        string high = new((char)0xD800, 1), low = new((char)0xDC00, 1);
        switch (kind)
        {
            case "component-empty": component = ""; break;
            case "component-null": component = null!; break;
            case "component-control": component = "PRIVATE_MARKER\0"; break;
            case "component-long": component = new string('x', 256); break;
            case "component-malformed": component = high; break;
            case "event-empty": data.EventName = ""; break;
            case "event-null": data.EventName = null!; break;
            case "event-control": data.EventName = "PRIVATE_MARKER\n"; break;
            case "event-long": data.EventName = new string('x', 256); break;
            case "event-malformed": data.EventName = low; break;
            case "message-null": data.Message = null!; break;
            case "message-high": data.Message = high; break;
            case "message-low": data.Message = low; break;
            case "message-suffix": data.Message = new string('x', 8192) + high; break;
            case "template-high": data.MessageTemplate = high; break;
            case "template-low": data.MessageTemplate = low; break;
            case "template-suffix": data.MessageTemplate = new string('x', 8192) + low; break;
            case "operation-control": data.OperationId = "PRIVATE_MARKER\0"; break;
            case "operation-long": data.OperationId = new string('x', 256); break;
            case "operation-malformed": data.OperationId = low; break;
            default: throw new InvalidDataException("Unknown native invalid input.");
        }
        var store = new OperationScopeStore();
        using IDisposable owner = store.BeginScope(new OperationContextData { OperationId = "unchanged" });
        OperationScopeFrame? head = store.CurrentFrame;
        ArgumentException error = kind.EndsWith("-null", StringComparison.Ordinal)
            ? Assert.ThrowsExactly<ArgumentNullException>(() => EventProjection.Capture(Snapshot(), store.Capture(), component, CoreLogLevel.Information, data))
            : Assert.ThrowsExactly<ArgumentException>(() => EventProjection.Capture(Snapshot(), store.Capture(), component, CoreLogLevel.Information, data));
        Assert.AreEqual(kind.StartsWith("component", StringComparison.Ordinal) ? "component" : "data", error.ParamName);
        Assert.IsFalse(error.Message.Contains("PRIVATE_MARKER", StringComparison.Ordinal));
        Assert.AreSame(head, store.CurrentFrame);
    }

    [TestMethod]
    [DataRow("metadata")]
    [DataRow("scope")]
    [DataRow("data")]
    public void MissingCaptureInputsFailBeforeCooperativePayloadAccess(string missing)
    {
        var callback = new CallbackMap(() => throw new InvalidOperationException("Payload must not be read."));
        var data = new LogEventData { EventName = "event", Message = "message", Attributes = callback };
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => EventProjection.Capture(
            missing == "metadata" ? null! : Snapshot(), missing == "scope" ? null! : new OperationScopeStore().Capture(),
            "Component", CoreLogLevel.Information, missing == "data" ? null! : data));
        Assert.AreEqual(missing, error.ParamName);
        Assert.AreEqual(0, callback.Enumerations);
    }

    [TestMethod]
    public void NoneDoesNotCaptureOrReadAnyRequiredSource()
    {
        Assert.IsNull(EventProjection.Capture(null!, null!, null!, CoreLogLevel.None, null!));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(7)]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void UndefinedSeverityIsRejectedBeforeContentAccess(int value)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EventProjection.Capture(null!, null!, null!, (CoreLogLevel)value, null!));
        Assert.AreEqual("severity", error.ParamName);
        Assert.IsNull(error.ActualValue);
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("exception")]
    public void ProjectionCarriesActualScopeOrExceptionLossIntoTheEventFacts(string source)
    {
        var store = new OperationScopeStore();
        var scope = Enumerable.Range(0, 1024).ToDictionary(index => "key" + index, index => LogValue.Integer(index), StringComparer.Ordinal);
        using IDisposable owner = store.BeginScope(new OperationContextData { Attributes = source == "scope" ? scope : null });
        var data = new LogEventData { EventName = "event", Message = "message" };
        if (source == "scope") { data.Attributes = scope.Take(1023).ToDictionary(pair => pair.Key, _ => LogValue.Null(), StringComparer.Ordinal); }
        else { data.Exception = new AggregateException(Enumerable.Range(0, 20).Select(index => new Exception("child" + index))); }
        CapturedEventFacts facts = EventProjection.Capture(Snapshot(), store.Capture(), "Component", CoreLogLevel.Information, data)!;
        Assert.IsTrue(facts.IsTruncated);
        Assert.IsTrue(facts.Attributes.NodeCount <= 1024);
        if (source == "scope") { Assert.IsTrue(facts.Attributes.MapProperties.Values.All(value => value.Kind == LogValueKind.Null)); }
        else { Assert.AreEqual(16, facts.Exception!.NodeCount); Assert.IsTrue(facts.Exception.HasTruncatedContent); }
    }

    [TestMethod]
    [DataRow("candidates")]
    [DataRow("nodes")]
    [DataRow("scalars")]
    public void MergedKeyAndNestedNodeBoundsIndependentlyRecordNewLoss(string budget)
    {
        var store = new OperationScopeStore();
        var scope = budget == "candidates"
            ? Enumerable.Range(0, 1023).ToDictionary(index => "key" + index, index => LogValue.Integer(index), StringComparer.Ordinal)
            : budget == "scalars" ? Enumerable.Range(0, 600).ToDictionary(index => "scope" + index, index => LogValue.Integer(index), StringComparer.Ordinal)
            : new Dictionary<string, LogValue> { ["scope-array"] = LogValue.Array(Enumerable.Repeat(LogValue.Null(), 600)) };
        using IDisposable owner = store.BeginScope(new OperationContextData { OperationId = "scope", Attributes = scope });
        CapturedScopeContext capturedScope = store.Capture();
        Assert.IsFalse(capturedScope.IsTruncated);
        var explicitAttributes = budget == "candidates"
            ? new Dictionary<string, LogValue> { ["new"] = LogValue.String("omitted"), ["key1022"] = LogValue.String("winner") }
            : budget == "scalars" ? Enumerable.Range(0, 600).ToDictionary(index => "event" + index, index => LogValue.Integer(index), StringComparer.Ordinal)
            : new Dictionary<string, LogValue> { ["event-array"] = LogValue.Array(Enumerable.Repeat(LogValue.Null(), 600)) };
        Assert.IsFalse(LogValue.Map(explicitAttributes).IsTruncated);
        OperationScopeFrame? head = store.CurrentFrame;
        EventMetadata snapshot = Snapshot();
        var data = new LogEventData { EventName = "event", Message = "message", OperationId = "event", Attributes = explicitAttributes };
        CapturedEventFacts facts = EventProjection.Capture(snapshot, capturedScope, "Component", CoreLogLevel.Information, data)!;
        Assert.AreEqual(1024, facts.Attributes.NodeCount);
        Assert.IsTrue(facts.Attributes.IsTruncated && facts.IsTruncated);
        Assert.AreEqual("event", facts.OperationId); Assert.AreEqual(snapshot.ApplicationName, facts.Service);
        Assert.AreEqual(snapshot.ServiceVersion, facts.Version); Assert.AreEqual("Component", facts.Component);
        Assert.AreSame(head, store.CurrentFrame);
        if (budget == "candidates")
        {
            Assert.IsFalse(facts.Attributes.MapProperties.ContainsKey("new"));
            Assert.AreEqual(1023, facts.Attributes.MapProperties.Count);
            Assert.AreEqual("winner", facts.Attributes.MapProperties["key1022"].Scalar.Text);
        }
        else if (budget == "scalars")
        {
            Assert.AreEqual(1023, facts.Attributes.MapProperties.Count);
            Assert.AreEqual(600, facts.Attributes.MapProperties.Keys.Count(key => key.StartsWith("scope", StringComparison.Ordinal)));
            Assert.AreEqual(423, facts.Attributes.MapProperties.Keys.Count(key => key.StartsWith("event", StringComparison.Ordinal)));
        }
        else
        {
            Assert.AreEqual(600, facts.Attributes.MapProperties["scope-array"].ArrayItems.Count);
            Assert.AreEqual(421, facts.Attributes.MapProperties["event-array"].ArrayItems.Count);
            Assert.IsTrue(facts.Attributes.MapProperties["event-array"].IsTruncated);
            Assert.AreEqual(600, explicitAttributes["event-array"].ArrayItems.Count);
        }
    }

    [TestMethod]
    public void NamedInputReferencesAreCapturedBeforeCooperativeCollectionCallbacks()
    {
        var store = new OperationScopeStore();
        using IDisposable owner = store.BeginScope(new OperationContextData { OperationId = "scope" });
        var data = new LogEventData { EventName = "original", Message = "original", OperationId = "old", Exception = new Exception("original") };
        var attributes = new CallbackMap(() =>
        {
            data.EventName = "changed"; data.Message = "changed"; data.OperationId = "changed";
            data.Attributes = null; data.Exception = new Exception("changed");
            using IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "callback" });
        });
        data.Attributes = attributes;
        CapturedEventFacts facts = EventProjection.Capture(Snapshot(), store.Capture(), "Component", CoreLogLevel.Information, data)!;
        Assert.AreEqual(1, attributes.Enumerations);
        Assert.AreEqual("original", facts.EventName); Assert.AreEqual("original", facts.Message);
        Assert.AreEqual("old", facts.OperationId); Assert.AreEqual("original", facts.Exception!.Message);
        Assert.AreEqual(1L, facts.Attributes.MapProperties["key"].Scalar.Integer);
        Assert.AreEqual("scope", store.Capture().OperationId);
    }

    [TestMethod]
    public void AllTypedValuesRemainTheirExplicitKindsInOwnedAttributes()
    {
        var values = new[] { LogValue.Null(), LogValue.Boolean(true), LogValue.String("s"), LogValue.Number(1.0), LogValue.Integer(1),
            LogValue.IntegerString("9007199254740992"), LogValue.DecimalString("1.00"), LogValue.Uuid(Guid.Parse("00000000-0000-4000-8000-000000000111")),
            LogValue.Timestamp(DateTimeOffset.UnixEpoch), LogValue.Array([LogValue.Null()]), LogValue.Map(new Dictionary<string, LogValue> { ["null"] = LogValue.Null() }) };
        var data = new LogEventData { EventName = "event", Message = "message", Attributes = values.ToDictionary(value => value.Kind.ToString(), StringComparer.Ordinal) };
        CapturedEventFacts facts = Capture(data);
        Assert.AreEqual(11, facts.Attributes.MapProperties.Count);
        foreach (LogValue value in values) { Assert.AreEqual(value.Kind, facts.Attributes.MapProperties[value.Kind.ToString()].Kind); }
    }

    [TestMethod]
    [DataRow("status")]
    [DataRow("identity")]
    [DataRow("type")]
    [DataRow("missing")]
    [DataRow("extra")]
    [DataRow("name")]
    public void ProjectionComparatorRejectsWrongFieldsTypesAndAllowlistMembers(string mutation)
    {
        JsonObject catalog = Catalog(); JsonObject row = catalog["cases"]![0]!.AsObject();
        JsonObject actual = EventProjectionExpectations.Expected(row, catalog["nativeBindings"]!.AsObject());
        JsonObject projection = actual["projection"]!.AsObject(); JsonObject resource = projection["resource"]!.AsObject();
        switch (mutation)
        {
            case "status": projection["status"] = "wrong"; break;
            case "identity": projection["service"] = "wrong"; break;
            case "type": resource["process_id"] = "123"; break;
            case "missing": resource.Remove("build_version"); break;
            case "extra": resource["FilePath"] = "private"; break;
            case "name": resource["buildVersion"] = resource["build_version"]!.DeepClone(); resource.Remove("build_version"); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual));
    }

    private static JsonObject Catalog() => LogValueTests.Read("event-projection-cases.json");
    private static JsonObject Metadata() => Catalog()["cases"]![0]!["input"]!["metadata"]!.DeepClone().AsObject();
    private static EventMetadata Snapshot() => EventProjectionFixtureSources.Create(Metadata(), out _);
    private static CapturedEventFacts Capture(LogEventData data) => EventProjection.Capture(Snapshot(), new OperationScopeStore().Capture(), "Component", CoreLogLevel.Information, data)!;
    private sealed class MutableException : Exception { internal string Text { get; set; } = ""; public override string Message => Text; }
    private sealed class CallbackMap(Action callback) : IReadOnlyDictionary<string, LogValue>
    {
        internal int Enumerations { get; private set; }
        public IEnumerator<KeyValuePair<string, LogValue>> GetEnumerator()
        {
            Enumerations++; callback();
            return ((IEnumerable<KeyValuePair<string, LogValue>>)new[] { new KeyValuePair<string, LogValue>("key", LogValue.Integer(1)) }).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int Count => throw new InvalidOperationException("Do not inspect caller counts.");
        public IEnumerable<string> Keys => throw new InvalidOperationException();
        public IEnumerable<LogValue> Values => throw new InvalidOperationException();
        public LogValue this[string key] => throw new InvalidOperationException();
        public bool ContainsKey(string key) => throw new InvalidOperationException();
        public bool TryGetValue(string key, out LogValue value) => throw new InvalidOperationException();
    }
}
