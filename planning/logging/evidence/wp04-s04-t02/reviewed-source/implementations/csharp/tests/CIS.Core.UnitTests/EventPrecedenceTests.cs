using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class EventPrecedenceTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void LiteralSharedPrecedenceUsesActualOwnedCoreFacts(string id)
    {
        JsonObject catalog = Catalog();
        JsonObject row = catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), EventPrecedenceFixture.Execute(row));
    }

    [TestMethod]
    [DataRow("ascii-max")]
    [DataRow("astral-max")]
    [DataRow("padded")]
    [DataRow("ascii-over")]
    [DataRow("astral-over")]
    [DataRow("high")]
    [DataRow("low")]
    [DataRow("nul")]
    [DataRow("c1")]
    public void IdentifierScalarBoundsApplyAfterTrimWithoutClippingOrStateMutation(string mode)
    {
        string ascii = new('x', 255); string astral = string.Concat(Enumerable.Repeat("\U0001f600", 255));
        string identifier = mode switch
        {
            "ascii-max" => ascii,
            "astral-max" => astral,
            "padded" => new string(' ', 300) + astral + "\u00a0\u2003",
            "ascii-over" => ascii + "x",
            "astral-over" => astral + "\U0001f600",
            "high" => "\ud800",
            "low" => "\udc00",
            "nul" => "PRIVATE_MARKER\0",
            "c1" => "PRIVATE_MARKER\u0085tail",
            _ => throw new InvalidDataException("Unknown identifier row.")
        };
        var store = new OperationScopeStore();
        using IDisposable owner = store.BeginScope(new OperationContextData { OperationId = "scope" });
        OperationScopeFrame? head = store.CurrentFrame;
        var data = new LogEventData { EventName = "event", Message = "", OperationId = identifier };
        if (mode is "ascii-max" or "astral-max" or "padded")
        {
            CapturedEventFacts facts = EventProjection.Capture(Snapshot(), store.Capture(), "Component", CoreLogLevel.Information, data)!;
            Assert.AreEqual(mode == "ascii-max" ? ascii : astral, facts.OperationId);
            Assert.AreEqual(255, facts.OperationId!.EnumerateRunes().Count());
            Assert.IsFalse(facts.IsTruncated);
        }
        else
        {
            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
                EventProjection.Capture(Snapshot(), store.Capture(), "Component", CoreLogLevel.Information, data));
            Assert.AreEqual("data", error.ParamName);
            Assert.IsFalse(error.Message.Contains("PRIVATE_MARKER", StringComparison.Ordinal));
        }
        Assert.AreSame(head, store.CurrentFrame); Assert.AreEqual("scope", store.Capture().OperationId);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("boolean")]
    [DataRow("string")]
    [DataRow("integer")]
    [DataRow("double")]
    [DataRow("integer-string")]
    [DataRow("decimal-string")]
    [DataRow("uuid")]
    [DataRow("timestamp")]
    [DataRow("array")]
    [DataRow("map")]
    public void ExplicitWinnerKeepsItsTypedKindAndReplacesTheWholeScopeValue(string kind)
    {
        LogValue expected = kind switch
        {
            "null" => LogValue.Null(),
            "boolean" => LogValue.Boolean(false),
            "string" => LogValue.String("event"),
            "integer" => LogValue.Integer(17),
            "double" => LogValue.Number(1.0),
            "integer-string" => LogValue.IntegerString("12345678901234567890"),
            "decimal-string" => LogValue.DecimalString("1.2300"),
            "uuid" => LogValue.Uuid(Guid.Parse("00000000-0000-4000-8000-000000000111")),
            "timestamp" => LogValue.Timestamp(new DateTimeOffset(2026, 10, 11, 6, 0, 0, TimeSpan.Zero)),
            "array" => LogValue.Array([LogValue.Null(), LogValue.Integer(2)]),
            "map" => LogValue.Map(new Dictionary<string, LogValue> { ["new"] = LogValue.Integer(2) }),
            _ => throw new InvalidDataException("Unknown typed winner.")
        };
        var store = new OperationScopeStore();
        using IDisposable owner = store.BeginScope(new OperationContextData
        {
            Attributes = new Dictionary<string, LogValue> { ["winner"] = LogValue.Map(new Dictionary<string, LogValue> { ["old"] = LogValue.String("scope") }) }
        });
        CapturedEventFacts facts = EventProjection.Capture(Snapshot(), store.Capture(), "Component", CoreLogLevel.Information,
            new LogEventData { EventName = "event", Message = "", Attributes = new Dictionary<string, LogValue> { ["winner"] = expected } })!;
        LogValue winner = facts.Attributes.MapProperties["winner"];
        Assert.AreEqual(expected.Kind, winner.Kind);
        Assert.IsTrue(JsonNode.DeepEquals(LogValueFixtureAdapter.Project(expected), LogValueFixtureAdapter.Project(winner)));
        Assert.AreEqual(1, facts.Attributes.MapProperties.Count);
        Assert.IsFalse(facts.IsTruncated);
    }

    [TestMethod]
    [DataRow("CAPTURE-NESTED-OVERRIDES")]
    [DataRow("CAPTURE-BLANK-IDS")]
    public void OriginalPrecedenceVectorsExecuteAgainstActualScopeAndEventFacts(string id)
    {
        JsonObject row = LogValueTests.Read("event-capture-expectations.json")["cases"]!.AsArray()
            .Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject input = row["inputs"]!.AsObject();
        var store = new OperationScopeStore(); var owners = new List<IDisposable>();
        try
        {
            foreach (JsonNode? node in input["scopes"]!.AsArray())
            {
                JsonObject scope = node!.AsObject();
                owners.Add(store.BeginScope(new OperationContextData
                {
                    OperationId = scope["operationId"]?.GetValue<string>(),
                    Attributes = Strings(scope["attributes"]!.AsObject())
                }));
            }
            JsonObject eventInput = input["event"]!.AsObject();
            CapturedEventFacts facts = EventProjection.Capture(Snapshot(), store.Capture(), "Component", CoreLogLevel.Information,
                new LogEventData
                {
                    EventName = "event",
                    Message = "",
                    OperationId = eventInput["operationId"]?.GetValue<string>(),
                    Attributes = Strings(eventInput["attributes"]!.AsObject())
                })!;
            Assert.AreEqual(row["expected"]!["operation_id"]!.GetValue<string>(), facts.OperationId);
            Assert.IsTrue(JsonNode.DeepEquals(row["expected"]!["attributes"], LogValueFixtureAdapter.Project(facts.Attributes)));
            Assert.IsFalse(facts.IsTruncated);
        }
        finally { for (int index = owners.Count - 1; index >= 0; index--) { owners[index].Dispose(); } }
    }

    [TestMethod]
    [DataRow("winner", "PRECEDENCE-NESTED-OVERRIDES")]
    [DataRow("deep-merge", "PRECEDENCE-MAP-WHOLE")]
    [DataRow("null-omitted", "PRECEDENCE-NULL-WINS")]
    [DataRow("case-fold", "PRECEDENCE-ORDINAL-CASE")]
    [DataRow("operation", "PRECEDENCE-NESTED-OVERRIDES")]
    [DataRow("type", "PRECEDENCE-ARRAY-TO-SCALAR")]
    [DataRow("identity", "PRECEDENCE-PROTECTED-EVENT")]
    [DataRow("trace-promotion", "PRECEDENCE-FREE-ID-ABSENT")]
    public void PrecedenceComparatorRejectsWrongWinnersAndFreeFormPromotion(string mutation, string id)
    {
        JsonObject catalog = Catalog();
        JsonObject row = catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = EventProjectionExpectations.Expected(row, catalog["nativeBindings"]!.AsObject());
        JsonObject projection = actual["projection"]!.AsObject(); JsonObject attributes = projection["attributes"]!.AsObject();
        switch (mutation)
        {
            case "winner": attributes["shared"] = "inner"; break;
            case "deep-merge": attributes["shared"]!["old"] = 1; break;
            case "null-omitted": attributes.Remove("shared"); break;
            case "case-fold": attributes.Remove("Tenant"); break;
            case "operation": projection["operation_id"] = "inner"; break;
            case "type": attributes["shared"] = "7"; break;
            case "identity": projection["service"] = "fake"; break;
            case "trace-promotion": projection["trace_id"] = attributes["trace_id"]!.DeepClone(); break;
            default: throw new InvalidDataException("Unknown comparator mutation.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual));
    }

    [TestMethod]
    [DataRow("popped")]
    [DataRow("replacement")]
    public void LaterScopesAndEventMutationsCannotChangeEarlierOwnedWinners(string mode)
    {
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer", Attributes = Strings(new JsonObject { ["shared"] = "outer" }) });
        using IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "inner", Attributes = Strings(new JsonObject { ["shared"] = "inner" }) });
        var source = new Dictionary<string, LogValue> { ["shared"] = LogValue.String("event-first") };
        var data = new LogEventData { EventName = "event", Message = "", OperationId = " event-first ", Attributes = source };
        EnvironmentSnapshot snapshot = Snapshot();
        CapturedEventFacts first = EventProjection.Capture(snapshot, store.Capture(), "Component", CoreLogLevel.Information, data)!;
        source.Clear(); data.OperationId = null;
        if (mode == "popped")
        {
            inner.Dispose(); data.Attributes = null;
            CapturedEventFacts later = EventProjection.Capture(snapshot, store.Capture(), "Component", CoreLogLevel.Information, data)!;
            Assert.AreEqual("outer", later.OperationId);
            Assert.IsTrue(JsonNode.DeepEquals(JsonValue.Create("outer"), LogValueFixtureAdapter.Project(later.Attributes.MapProperties["shared"])));
        }
        else
        {
            using IDisposable replacement = store.BeginScope(new OperationContextData { OperationId = "replacement" });
            source["shared"] = LogValue.String("event-next");
            CapturedEventFacts later = EventProjection.Capture(snapshot, store.Capture(), "Component", CoreLogLevel.Information, data)!;
            Assert.AreEqual("replacement", later.OperationId);
            Assert.IsTrue(JsonNode.DeepEquals(JsonValue.Create("event-next"), LogValueFixtureAdapter.Project(later.Attributes.MapProperties["shared"])));
        }
        Assert.AreEqual("event-first", first.OperationId);
        Assert.IsTrue(JsonNode.DeepEquals(JsonValue.Create("event-first"), LogValueFixtureAdapter.Project(first.Attributes.MapProperties["shared"])));
        Assert.AreEqual("Projection.App", first.Service); Assert.AreEqual("qa", first.Environment);
    }

    private static Dictionary<string, LogValue> Strings(JsonObject values) => values.ToDictionary(pair => pair.Key,
        pair => LogValue.String(pair.Value!.GetValue<string>()), StringComparer.Ordinal);
    private static JsonObject Catalog() => LogValueTests.Read("event-precedence-cases.json");
    private static EnvironmentSnapshot Snapshot() => EventProjectionFixtureSources.Create(Catalog()["cases"]![0]!["input"]!["metadata"]!.DeepClone().AsObject(), out _, out _);
}
