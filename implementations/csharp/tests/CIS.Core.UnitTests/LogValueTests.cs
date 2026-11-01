using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LogValueTests
{
    public static IEnumerable<object[]> PortableRows => Read()["cases"]!.AsArray()
        .Where(row => LogValueFixtureAdapter.Kinds.Contains(row!["input"]!["kind"]!.GetValue<string>(), StringComparer.Ordinal))
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(PortableRows))]
    public void ActualValueFactoriesMatchEveryApplicablePortableVector(string id)
    {
        JsonObject row = Read()["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LogValueFixtureAdapter.Execute(row);
        LogValueFixtureAdapter.Validate(row, actual);
        bool adapterOnly = id is "VALUE-INVALID-BOOL" or "VALUE-INVALID-INTEGER-TYPE";
        Assert.AreEqual(adapterOnly, actual["factoryCalls"]!.GetValue<int>() == 0, id);
        if (!row["expectedValid"]!.GetValue<bool>() && !adapterOnly)
        {
            Assert.AreEqual(id == "VALUE-INVALID-MAP-KEY" ? "properties" : "value", actual["parameter"]!.GetValue<string>());
        }
    }

    [TestMethod]
    [DataRow(long.MinValue)]
    [DataRow(long.MaxValue)]
    [DataRow(-9007199254740992L)]
    [DataRow(9007199254740992L)]
    public void UnsafeNativeIntegersNeverChangeToString(long value)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LogValue.Integer(value));
        Assert.AreEqual("value", error.ParamName);
        Assert.IsNull(error.ActualValue);
        LogValue explicitText = LogValue.IntegerString(value.ToString(CultureInfo.InvariantCulture));
        Assert.AreEqual(LogValueKind.IntegerString, explicitText.Kind);
        Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture), explicitText.Scalar.Text);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void NonfiniteNativeNumbersFailWithAStableArgumentCategory(double value)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LogValue.Number(value));
        Assert.AreEqual("value", error.ParamName);
        Assert.IsNull(error.ActualValue);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-0.0)]
    [DataRow(double.Epsilon)]
    [DataRow(-double.Epsilon)]
    [DataRow(double.MaxValue)]
    [DataRow(-double.MaxValue)]
    [DataRow(9007199254740992.0)]
    [DataRow(1.0)]
    public void FiniteNumbersRetainTheirExplicitKindAndIeeeValue(double value)
    {
        LogValue result = LogValue.Number(value);
        Assert.AreEqual(LogValueKind.Number, result.Kind);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(result.Scalar.Number));
        using JsonDocument json = JsonDocument.Parse(LogValueFixtureAdapter.Project(result)!.ToJsonString());
        Assert.AreEqual(JsonValueKind.Number, json.RootElement.ValueKind);
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("fr-FR")]
    [DataRow("ar-EG")]
    public void ExplicitNumericRepresentationsRemainInvariantAndPreservePrecision(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            foreach (string text in new[] { "0.0", "-0.0000", "12.3400", "100000000000000000000000.012345678901234567890" })
            {
                LogValue result = LogValue.DecimalString(text);
                Assert.AreEqual(LogValueKind.DecimalString, result.Kind);
                Assert.AreEqual(text, result.Scalar.Text);
            }
            Assert.AreEqual("9223372036854775808123456789", LogValue.IntegerString("9223372036854775808123456789").Scalar.Text);
            Assert.ThrowsExactly<ArgumentException>(() => LogValue.DecimalString("1,25"));
            Assert.ThrowsExactly<ArgumentException>(() => LogValue.IntegerString("\u0661"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    [DataRow("String")]
    [DataRow("IntegerString")]
    [DataRow("DecimalString")]
    [DataRow("Array")]
    [DataRow("Map")]
    public void NullRequiredArgumentsKeepTheirDeclaredParameterName(string method)
    {
        Action call = method switch
        {
            "String" => () => LogValue.String(null!),
            "IntegerString" => () => LogValue.IntegerString(null!),
            "DecimalString" => () => LogValue.DecimalString(null!),
            "Array" => () => LogValue.Array(null!),
            _ => () => LogValue.Map(null!)
        };
        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(call);
        Assert.AreEqual(method == "Array" ? "items" : method == "Map" ? "properties" : "value", error.ParamName);
    }

    [TestMethod]
    [DataRow("high")]
    [DataRow("low")]
    [DataRow("reversed")]
    [DataRow("interrupted")]
    public void StringsRejectMalformedUtf16WithoutChangingValidText(string scenario)
    {
        string text = scenario switch
        {
            "high" => "x" + (char)0xd800,
            "low" => "x" + (char)0xdc00,
            "reversed" => new string([(char)0xdc00, (char)0xd800]),
            _ => new string([(char)0xd800, 'x', (char)0xdc00])
        };
        Assert.ThrowsExactly<ArgumentException>(() => LogValue.String(text));
        const string valid = " e\u0301\U0001f600\n\t\0 ";
        Assert.AreEqual(valid, LogValue.String(valid).Scalar.Text);
    }

    [TestMethod]
    public void OwnedNestedCollectionsSurviveEveryCallerMutationAndRemainReadOnly()
    {
        var leaf = new Dictionary<string, LogValue> { ["amount"] = LogValue.DecimalString("12.3400") };
        var items = new List<LogValue> { LogValue.Map(leaf), LogValue.Null() };
        var source = new Dictionary<string, LogValue> { ["items"] = LogValue.Array(items) };
        LogValue result = LogValue.Map(source);
        string before = LogValueFixtureAdapter.Project(result)!.ToJsonString();
        leaf["amount"] = LogValue.String("changed");
        items.Clear();
        source.Clear();
        Assert.AreEqual(before, LogValueFixtureAdapter.Project(result)!.ToJsonString());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, LogValue>)result.MapProperties).Clear());
        LogValue array = result.MapProperties["items"];
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<LogValue>)array.ArrayItems)[0] = LogValue.Boolean(false));
        Assert.AreEqual(LogValueKind.DecimalString, array.ArrayItems[0].MapProperties["amount"].Kind);
        Assert.AreEqual(LogValueKind.Null, array.ArrayItems[1].Kind);
    }

    [TestMethod]
    public void MapKeysUseOrdinalEqualityAndPreserveWhitespaceAndUnicodeSequences()
    {
        var source = new Dictionary<string, LogValue>
        {
            ["a"] = LogValue.Integer(1),
            ["A"] = LogValue.Integer(2),
            ["\u00e9"] = LogValue.Integer(3),
            ["e\u0301"] = LogValue.Integer(4),
            [" "] = LogValue.Integer(5)
        };
        LogValue value = LogValue.Map(source);
        Assert.AreEqual(5, value.MapProperties.Count);
        foreach ((string key, LogValue child) in source)
        {
            Assert.AreSame(child, value.MapProperties[key]);
        }
        Assert.IsFalse(value.MapProperties.ContainsKey("E\u0301"));
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("control")]
    [DataRow("malformed")]
    [DataRow("over-limit")]
    public void InvalidMapKeysAreRejectedRatherThanAltered(string scenario)
    {
        string key = scenario switch
        {
            "empty" => "",
            "control" => "bad\n",
            "malformed" => new string((char)0xd800, 1),
            _ => string.Concat(Enumerable.Repeat("\U0001f600", 256))
        };
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => LogValue.Map(new Dictionary<string, LogValue> { [key] = LogValue.Null() }));
        Assert.AreEqual("properties", error.ParamName);
        string maximum = string.Concat(Enumerable.Repeat("\U0001f600", 255));
        Assert.IsTrue(LogValue.Map(new Dictionary<string, LogValue> { [maximum] = LogValue.Null() }).MapProperties.ContainsKey(maximum));
    }

    [TestMethod]
    [DataRow("array")]
    [DataRow("map")]
    public void NullReferencesInsideCollectionsRequireExplicitNullValues(string kind)
    {
        ArgumentException error = kind == "array"
            ? Assert.ThrowsExactly<ArgumentException>(() => LogValue.Array([null!]))
            : Assert.ThrowsExactly<ArgumentException>(() => LogValue.Map(new Dictionary<string, LogValue> { ["a"] = null! }));
        Assert.AreEqual(kind == "array" ? "items" : "properties", error.ParamName);
    }

    [TestMethod]
    public void ArrayInputIsEnumeratedOnceAndDisposedWhenValidationFails()
    {
        var source = new ObservedSequence([LogValue.Integer(1), null!]);
        Assert.ThrowsExactly<ArgumentException>(() => LogValue.Array(source));
        Assert.AreEqual(1, source.Enumerations);
        Assert.IsTrue(source.Disposed);
    }

    [TestMethod]
    public void EmptyCollectionsAndExplicitNullKeepDifferentRepresentations()
    {
        LogValue emptyArray = LogValue.Array([]);
        LogValue emptyMap = LogValue.Map(new Dictionary<string, LogValue>());
        Assert.AreEqual(LogValueKind.Array, emptyArray.Kind);
        Assert.AreEqual(LogValueKind.Map, emptyMap.Kind);
        Assert.AreEqual(0, emptyArray.ArrayItems.Count);
        Assert.AreEqual(0, emptyMap.MapProperties.Count);
        Assert.IsNull(LogValueFixtureAdapter.Project(LogValue.Null()));
        Assert.AreEqual("[]", LogValueFixtureAdapter.Project(emptyArray)!.ToJsonString());
        Assert.AreEqual("{}", LogValueFixtureAdapter.Project(emptyMap)!.ToJsonString());
    }

    [TestMethod]
    public void MapRejectsDuplicateKeysFromAReadOnlyInterface()
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => LogValue.Map(new DuplicateMap()));
        Assert.AreEqual("properties", error.ParamName);
    }

    [TestMethod]
    [DataRow("VALUE-INTEGER-0", "Number")]
    [DataRow("VALUE-DECIMAL-13", "String")]
    public void TaggedComparatorRejectsIdenticalWireValuesWithChangedKinds(string id, string replacement)
    {
        JsonObject row = Read()["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LogValueFixtureAdapter.Execute(row);
        actual["kind"] = replacement;
        Assert.ThrowsExactly<InvalidDataException>(() => LogValueFixtureAdapter.Validate(row, actual));
    }

    private sealed class DuplicateMap : IReadOnlyDictionary<string, LogValue>
    {
        public int Count => 2;
        public IEnumerable<string> Keys => ["a", "a"];
        public IEnumerable<LogValue> Values => [LogValue.Null(), LogValue.Null()];
        public LogValue this[string key] => LogValue.Null();
        public bool ContainsKey(string key) => key == "a";
        public bool TryGetValue(string key, out LogValue value)
        {
            value = LogValue.Null();
            return ContainsKey(key);
        }
        public IEnumerator<KeyValuePair<string, LogValue>> GetEnumerator()
        {
            yield return new("a", LogValue.Null());
            yield return new("a", LogValue.Null());
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ObservedSequence(LogValue[] values) : IEnumerable<LogValue>
    {
        public int Enumerations { get; private set; }
        public bool Disposed { get; private set; }
        public IEnumerator<LogValue> GetEnumerator()
        {
            Enumerations++;
            try
            {
                foreach (LogValue value in values)
                {
                    yield return value;
                }
            }
            finally
            {
                Disposed = true;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static JsonObject Read(string name = "event-value-cases.json") => QualificationFixtureInputs.ReadLogging(name);
}
