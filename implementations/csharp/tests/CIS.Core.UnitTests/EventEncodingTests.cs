using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Exceptions;
using CIS.Logging.Scopes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed partial class EventEncodingTests
{
    private const int Minimum = 16384;

    [TestMethod]
    public void AllTypedValuesAndOptionalFactsRoundTripAsOneOwnedRecord()
    {
        var attributes = new Dictionary<string, LogValue>
        {
            ["null"] = LogValue.Null(),
            ["true"] = LogValue.Boolean(true),
            ["false"] = LogValue.Boolean(false),
            ["text"] = LogValue.String("\"\\\n\r\t\0 café 😀"),
            ["integer"] = LogValue.Integer(9007199254740991),
            ["integerString"] = LogValue.IntegerString("90071992547409920000"),
            ["decimal"] = LogValue.DecimalString("12.3400"),
            ["number"] = LogValue.Number(1.25),
            ["uuid"] = LogValue.Uuid(Guid.Empty),
            ["time"] = LogValue.Timestamp(Instant),
            ["array"] = LogValue.Array([LogValue.Null(), LogValue.Integer(-7)]),
            ["map"] = LogValue.Map(new Dictionary<string, LogValue> { ["case"] = LogValue.String("lower"), ["Case"] = LogValue.String("upper") })
        };
        EncodedEvent encoded = EventEncoder.Encode(Capture(new LogEventData
        {
            EventName = "unicode.event",
            Message = "original message",
            MessageTemplate = "",
            OperationId = " operation ",
            Attributes = attributes,
            Exception = new InvalidOperationException("failed", new Exception("inner"))
        }), 65536);
        using JsonDocument document = Record(encoded);
        JsonElement actual = document.RootElement, values = actual.GetProperty("attributes");
        Assert.AreEqual(1, actual.GetProperty("schema_version").GetInt32());
        Assert.AreEqual("2026-10-12T10:11:12.1234567Z", actual.GetProperty("timestamp").GetString());
        Assert.AreEqual("info", actual.GetProperty("status").GetString());
        Assert.AreEqual("operation", actual.GetProperty("operation_id").GetString());
        Assert.AreEqual("", actual.GetProperty("message_template").GetString());
        Assert.AreEqual(JsonValueKind.Null, values.GetProperty("null").ValueKind);
        Assert.AreEqual("\"\\\n\r\t\0 café 😀", values.GetProperty("text").GetString());
        Assert.AreEqual(9007199254740991L, values.GetProperty("integer").GetInt64());
        Assert.AreEqual("90071992547409920000", values.GetProperty("integerString").GetString());
        Assert.AreEqual("12.3400", values.GetProperty("decimal").GetString());
        Assert.AreEqual(1.25, values.GetProperty("number").GetDouble());
        Assert.IsTrue(values.GetProperty("true").GetBoolean()); Assert.IsFalse(values.GetProperty("false").GetBoolean());
        Assert.AreEqual("00000000-0000-0000-0000-000000000000", values.GetProperty("uuid").GetString());
        Assert.AreEqual("2026-10-12T10:11:12.1234567Z", values.GetProperty("time").GetString());
        Assert.AreEqual("upper", values.GetProperty("map").GetProperty("Case").GetString());
        Assert.AreEqual("lower", values.GetProperty("map").GetProperty("case").GetString());
        Assert.AreEqual(-7, values.GetProperty("array")[1].GetInt32());
        Assert.AreEqual("System.InvalidOperationException", actual.GetProperty("exception").GetProperty("type").GetString());
        Assert.AreEqual("inner", actual.GetProperty("exception").GetProperty("inner_exceptions")[0].GetProperty("message").GetString());
        Assert.AreEqual(14, actual.GetProperty("resource").EnumerateObject().Count());
        Assert.IsFalse(actual.TryGetProperty("truncated", out _)); Assert.IsFalse(encoded.IsTruncated);
        Assert.IsFalse(actual.TryGetProperty("trace_id", out _)); Assert.IsFalse(actual.TryGetProperty("span_id", out _));
        attributes.Clear(); Assert.AreEqual(12, document.RootElement.GetProperty("attributes").EnumerateObject().Count());
    }

    [TestMethod]
    [DataRow("\"\\\b\f\n\r\t\0\u001b\u007f")]
    [DataRow("café 中文 😀 \u2028\u2029 <script> &")]
    [DataRow("x\uFEFFy\u200Dz\uFFFF")]
    public void JsonEscapingRoundTripsControlsAndUnicodeWithoutRawLines(string message)
    {
        using JsonDocument record = Record(EventEncoder.Encode(Capture(new LogEventData { EventName = "escape", Message = message }), 65536));
        Assert.AreEqual(message, record.RootElement.GetProperty("message").GetString());
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-0.0)]
    [DataRow(double.Epsilon)]
    [DataRow(double.MaxValue)]
    [DataRow(double.MinValue)]
    [DataRow(1e20)]
    [DataRow(1e-7)]
    [DataRow(1.2345678901234567)]
    public void NumberFormattingPreservesFiniteDoubleAndActualMeasuredBytes(double value)
    {
        using JsonDocument record = Record(EventEncoder.Encode(Capture(Attributes("value", LogValue.Number(value))), 65536));
        Assert.AreEqual(value, record.RootElement.GetProperty("attributes").GetProperty("value").GetDouble());
        Assert.AreEqual(JsonValueKind.Number, record.RootElement.GetProperty("attributes").GetProperty("value").ValueKind);
    }

    [TestMethod]
    [DataRow(16384)]
    [DataRow(65536)]
    [DataRow(131072)]
    public void ExactlyAtCapFitsAndOneExtraByteTriggersMarkedTruncationIncludingLf(int cap)
    {
        var metadata = new FixtureEnvironmentValues();
        int empty = EventEncoder.Encode(Capture(Attributes("payload", LogValue.String("")), metadata), cap).JsonLine.Length;
        EncodedEvent exact = EventEncoder.Encode(Capture(Attributes("payload", LogValue.String(new string('x', cap - empty))), metadata), cap);
        Assert.AreEqual(cap, exact.JsonLine.Length); Assert.IsFalse(exact.IsTruncated);
        EncodedEvent over = EventEncoder.Encode(Capture(Attributes("payload", LogValue.String(new string('x', cap - empty + 1))), metadata), cap);
        Assert.AreEqual(cap, over.JsonLine.Length); Assert.IsTrue(over.IsTruncated);
        using JsonDocument record = Record(over);
        Assert.IsTrue(record.RootElement.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(cap - empty - 17, record.RootElement.GetProperty("attributes").GetProperty("payload").GetString()!.Length);
    }

    [TestMethod]
    [DataRow("\u0001")]
    [DataRow("\\")]
    [DataRow("é")]
    [DataRow("中")]
    [DataRow("😀")]
    public void EscapedByteBoundaryNeverSplitsAScalar(string scalar)
    {
        string text = string.Concat(Enumerable.Repeat(scalar, 20000));
        EncodedEvent encoded = EventEncoder.Encode(Capture(Attributes("payload", LogValue.String(text))), Minimum);
        Assert.IsTrue(encoded.IsTruncated); Assert.IsTrue(encoded.JsonLine.Length <= Minimum);
        using JsonDocument record = Record(encoded);
        string retained = record.RootElement.GetProperty("attributes").GetProperty("payload").GetString()!;
        Assert.IsTrue(text.StartsWith(retained, StringComparison.Ordinal));
        Assert.AreEqual(0, retained.Length % scalar.Length); Assert.IsTrue(retained.Length > 0);
    }

    [TestMethod]
    public void MessageThenOrdinalAttributesWinBeforeExceptionTemplateAndDescriptions()
    {
        CapturedEventFacts source = Capture(new LogEventData
        {
            EventName = "priority",
            Message = new string('m', 2000),
            MessageTemplate = "lower priority template",
            Attributes = new Dictionary<string, LogValue> { ["z"] = LogValue.Integer(42), ["a"] = LogValue.String(new string('a', 30000)) },
            Exception = new Exception("lower priority exception"),
            OperationId = "protected operation"
        });
        using JsonDocument record = Record(EventEncoder.Encode(source, Minimum)); JsonElement actual = record.RootElement;
        Assert.AreEqual(source.Message, actual.GetProperty("message").GetString());
        Assert.IsTrue(actual.GetProperty("attributes").GetProperty("a").GetString()!.Length > 0);
        Assert.IsFalse(actual.GetProperty("attributes").TryGetProperty("z", out _));
        Assert.IsFalse(actual.TryGetProperty("exception", out _)); Assert.IsFalse(actual.TryGetProperty("message_template", out _));
        Assert.IsFalse(actual.GetProperty("resource").TryGetProperty("os_description", out _));
        Assert.IsFalse(actual.GetProperty("resource").TryGetProperty("runtime_description", out _));
        Assert.AreEqual("protected operation", actual.GetProperty("operation_id").GetString());
        foreach (string name in new[] { "build_version", "instance_id", "initialized_at", "process_started_at", "environment_kind", "os_family", "host_name", "os_architecture", "process_architecture", "process_id", "commit_id", "build_id" })
        { Assert.IsTrue(actual.GetProperty("resource").TryGetProperty(name, out _), name); }
    }

    [TestMethod]
    public void SortedMapBudgetIsIndependentOfInsertionOrderAndKeepsArrayOrder()
    {
        LogValue nested = LogValue.Array([LogValue.Integer(2), LogValue.Integer(1)]);
        CapturedEventFacts first = Capture(new LogEventData { EventName = "order", Message = "", Attributes = new Dictionary<string, LogValue> { ["z"] = nested, ["a"] = LogValue.String(new string('x', 20000)) } });
        CapturedEventFacts second = Capture(new LogEventData { EventName = "order", Message = "", Attributes = new Dictionary<string, LogValue> { ["a"] = LogValue.String(new string('x', 20000)), ["z"] = nested } });
        CollectionAssert.AreEqual(EventEncoder.Encode(first, Minimum).JsonLine.ToArray(), EventEncoder.Encode(second, Minimum).JsonLine.ToArray());
        using JsonDocument full = Record(EventEncoder.Encode(first, 65536));
        Assert.AreEqual(2, full.RootElement.GetProperty("attributes").GetProperty("z")[0].GetInt32());
        Assert.AreEqual(1, full.RootElement.GetProperty("attributes").GetProperty("z")[1].GetInt32());
    }

    [TestMethod]
    [DataRow("integer")]
    [DataRow("decimal")]
    [DataRow("uuid")]
    [DataRow("timestamp")]
    public void IndivisibleStringRepresentationsAreOmittedWholeWhenOnlyQuotesFit(string kind)
    {
        LogValue value = kind switch
        {
            "integer" => LogValue.IntegerString(new string('1', 20000)),
            "decimal" => LogValue.DecimalString("1." + new string('0', 20000)),
            "uuid" => LogValue.Uuid(Guid.Empty),
            _ => LogValue.Timestamp(Instant)
        };
        var metadata = new FixtureEnvironmentValues();
        int empty = EventEncoder.Encode(Capture(new LogEventData { EventName = "atomic", Message = "", Attributes = new Dictionary<string, LogValue> { ["a"] = LogValue.String(""), ["b"] = value } }, metadata), 131072).JsonLine.Length;
        int valueSize = JsonSerializer.Serialize(value.Scalar.Text).Length;
        int fill = Minimum - empty + valueSize - 2 - 17;
        using JsonDocument record = Record(EventEncoder.Encode(Capture(new LogEventData { EventName = "atomic", Message = "", Attributes = new Dictionary<string, LogValue> { ["a"] = LogValue.String(new string('x', fill)), ["b"] = value } }, metadata), Minimum));
        Assert.IsTrue(record.RootElement.GetProperty("truncated").GetBoolean());
        Assert.IsFalse(record.RootElement.GetProperty("attributes").TryGetProperty("b", out _));
    }

    [TestMethod]
    public void OmittedAtomicAttributeDoesNotPreventLaterSmallValuesFromFitting()
    {
        using JsonDocument record = Record(EventEncoder.Encode(Capture(new LogEventData
        {
            EventName = "skip",
            Message = "",
            Attributes = new Dictionary<string, LogValue>
            { ["a"] = LogValue.IntegerString(new string('1', 30000)), ["b"] = LogValue.Integer(0), ["c"] = LogValue.Boolean(true) }
        }), Minimum));
        JsonElement values = record.RootElement.GetProperty("attributes");
        Assert.IsFalse(values.TryGetProperty("a", out _)); Assert.AreEqual(0, values.GetProperty("b").GetInt32()); Assert.IsTrue(values.GetProperty("c").GetBoolean());
    }

    [TestMethod]
    public void NestedCollectionsRetainTheirShapesAndExceptionRetainsLocalMarker()
    {
        var attributes = new Dictionary<string, LogValue> { ["nested"] = LogValue.Map(new Dictionary<string, LogValue> { ["array"] = LogValue.Array([LogValue.String(new string('x', 30000))]) }) };
        using JsonDocument values = Record(EventEncoder.Encode(Capture(new LogEventData { EventName = "nested", Message = "", Attributes = attributes }), Minimum));
        Assert.AreEqual(JsonValueKind.Array, values.RootElement.GetProperty("attributes").GetProperty("nested").GetProperty("array").ValueKind);
        using JsonDocument error = Record(EventEncoder.Encode(Capture(new LogEventData { EventName = "error", Message = "", Exception = new StackException() }), Minimum));
        Assert.IsTrue(error.RootElement.GetProperty("exception").GetProperty("truncated").GetBoolean());
        Assert.IsFalse(error.RootElement.GetProperty("exception").TryGetProperty("inner_exceptions", out _));
        Assert.AreEqual("message", error.RootElement.GetProperty("exception").GetProperty("message").GetString());
    }

    [TestMethod]
    public void EarlierScalarProjectionLossRemainsMarkedEvenWhenEncodedEntryFits()
    {
        EncodedEvent encoded = EventEncoder.Encode(Capture(new LogEventData { EventName = "loss", Message = new string('x', 8193) }), 65536);
        using JsonDocument record = Record(encoded);
        Assert.IsTrue(encoded.IsTruncated); Assert.IsTrue(record.RootElement.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(8192, record.RootElement.GetProperty("message").GetString()!.Length);
    }

    [TestMethod]
    public void OversizedProtectedEnvelopeHasSpecificFailureAndDoesNotAlterFacts()
    {
        string identity = string.Concat(Enumerable.Repeat("😀", 512));
        var metadata = new FixtureEnvironmentValues { BuildVersion = identity, ServiceVersion = identity, CommitId = identity, BuildId = identity };
        CapturedEventFacts facts = Capture(Attributes("payload", LogValue.String("small")), metadata);
        Assert.ThrowsExactly<EventEnvelopeTooLargeException>(() => EventEncoder.Encode(facts, Minimum));
        Assert.AreEqual(identity, facts.Version); Assert.IsFalse(facts.IsTruncated);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16383)]
    [DataRow(16777217)]
    [DataRow(int.MaxValue)]
    public void EncoderRejectsInvalidConfiguredLimits(int cap) => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EventEncoder.Encode(Capture(Attributes("value", LogValue.Null())), cap));

    [TestMethod]
    public void EncoderRequiresOwnedFacts() => Assert.ThrowsExactly<ArgumentNullException>(() => EventEncoder.Encode(null!, Minimum));

    [TestMethod]
    [DataRow("")]
    [DataRow("😀")]
    public void BudgetDoesNotInventEmptyOptionalStringsButKeepsSuppliedEmptyValues(string value)
    {
        var metadata = new FixtureEnvironmentValues();
        var inputs = new Dictionary<string, LogValue> { ["a"] = LogValue.String(""), ["b"] = LogValue.String("") };
        var data = new LogEventData { EventName = "empty", Message = "", Attributes = inputs };
        int baseline = EventEncoder.Encode(Capture(data, metadata), Minimum).JsonLine.Length;
        inputs["a"] = LogValue.String(new string('x', Minimum - baseline - 17));
        inputs["b"] = LogValue.String(value); inputs["z"] = LogValue.IntegerString(new string('1', 30000));
        using JsonDocument record = Record(EventEncoder.Encode(Capture(data, metadata), Minimum));
        JsonElement attributes = record.RootElement.GetProperty("attributes");
        Assert.AreEqual(value.Length == 0, attributes.TryGetProperty("b", out JsonElement retained));
        if (value.Length == 0) { Assert.AreEqual("", retained.GetString()); }
        Assert.IsTrue(record.RootElement.GetProperty("truncated").GetBoolean());
    }

    private sealed class StackException : Exception
    {
        public override string Message => "message";
        public override string StackTrace => new string('x', 16384);
        internal StackException() : base("unused", new Exception("inner")) { }
    }

    private static DateTimeOffset Instant => DateTimeOffset.Parse("2026-10-12T12:11:12.1234567+02:00", CultureInfo.InvariantCulture);
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Instant; }
    private static LogEventData Attributes(string key, LogValue value) => new() { EventName = "payload", Message = "", Attributes = new Dictionary<string, LogValue> { [key] = value } };
    private static CapturedEventFacts Capture(LogEventData data, FixtureEnvironmentValues? metadata = null)
    {
        metadata ??= new FixtureEnvironmentValues
        {
            ApplicationName = "Test.App",
            EnvironmentName = "tests",
            BuildVersion = "build",
            ServiceVersion = "service",
            EnvironmentKind = EnvironmentKind.Console,
            CommitId = "commit",
            BuildId = "build-id",
            HostName = "test-node",
            OperatingSystem = "linux",
            OperatingSystemDescription = "description",
            OSArchitecture = "x64",
            ProcessArchitecture = "x64",
            RuntimeDescription = "runtime",
            ProcessId = 123,
            ProcessStartedAtUtc = Instant.AddMinutes(-1),
            InitializedAtUtc = Instant
        };
        var store = new OperationScopeStore();
        return EventProjection.Capture(metadata.CaptureMetadata(), store.Capture(), "component", CoreLogLevel.Information, data, new FixedClock())!;
    }
    private static JsonDocument Record(EncodedEvent encoded)
    {
        byte[] bytes = encoded.JsonLine.ToArray();
        Assert.AreEqual((byte)'\n', bytes[^1]); Assert.AreNotEqual((byte)0xEF, bytes[0]);
        Assert.AreEqual(1, bytes.Count(value => value == (byte)'\n')); Assert.AreEqual(0, bytes.Count(value => value == (byte)'\r'));
        _ = new UTF8Encoding(false, true).GetString(bytes);
        return JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
    }
}
