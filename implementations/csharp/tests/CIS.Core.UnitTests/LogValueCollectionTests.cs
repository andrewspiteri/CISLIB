using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LogValueCollectionTests
{
    public static IEnumerable<object[]> Rows => LogValueTests.Read("value-collection-cases.json")["cases"]!.AsArray()
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    public static IEnumerable<object[]> InvalidRows => new[] { 0, 1023 }.SelectMany(index =>
        new[] { ("array", "null"), ("map", "null"), ("map", "empty"), ("map", "control"), ("map", "malformed"), ("map", "over-limit"), ("map", "duplicate") }
            .Select(pair => new object[] { pair.Item1, pair.Item2, index }));

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void ActualCollectionFactoriesMatchIndependentRootAndBoundaryVectors(string id)
    {
        JsonObject row = LogValueTests.Read("value-collection-cases.json")["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LogValueCollectionFixtureAdapter.Execute(row);
        LogValueCollectionFixtureAdapter.Validate(row, actual);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidRows))]
    public void ObservedInvalidChildrenAndKeysFailEvenAtTheFirstExcessEntry(string kind, string problem, int index)
    {
        LogValue[] values = Enumerable.Repeat(LogValue.Null(), index + 1).ToArray();
        if (problem == "null")
        {
            values[^1] = null!;
        }
        string? invalidKey = problem switch
        {
            "empty" => "",
            "control" => "CIS_NO_KEY_ECHO\n",
            "malformed" => new string((char)0xd800, 1),
            "over-limit" => string.Concat(Enumerable.Repeat("\U0001f600", 256)),
            _ => null
        };
        var input = new LogValueObservedInputs { Values = values, InvalidLastKey = invalidKey, DuplicateLastKey = problem == "duplicate" };
        if (problem == "duplicate" && index == 0)
        {
            // Two entries are necessary to observe a duplicate.
            input = new LogValueObservedInputs { Values = [LogValue.Null(), LogValue.Null()], DuplicateLastKey = true };
        }
        ArgumentException error = kind == "array"
            ? Assert.ThrowsExactly<ArgumentException>(() => LogValue.Array(input.Sequence()))
            : Assert.ThrowsExactly<ArgumentException>(() => LogValue.Map(input.Map()));
        Assert.AreEqual(kind == "array" ? "items" : "properties", error.ParamName);
        Assert.IsFalse(error.Message.Contains("CIS_NO_KEY_ECHO", StringComparison.Ordinal));
        Assert.AreEqual(1, input.Enumerations);
        Assert.IsTrue(input.Disposed);
        Assert.AreEqual(problem == "duplicate" && index == 0 ? 2 : index + 1, input.Produced);
    }

    [TestMethod]
    [DataRow("nodes")]
    [DataRow("depth")]
    [DataRow("isTruncated")]
    public void BoundaryComparatorRejectsWrongRootCountingAndFalseLossMarkers(string field)
    {
        JsonObject row = LogValueTests.Read("value-collection-cases.json")["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == "COLLECTION-MAP-CHILDREN-1023")!.AsObject();
        JsonObject actual = LogValueCollectionFixtureAdapter.Execute(row);
        if (field == "isTruncated")
        {
            actual[field] = true;
        }
        else
        {
            actual[field] = actual[field]!.GetValue<int>() - 1;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => LogValueCollectionFixtureAdapter.Validate(row, actual));
    }

    [TestMethod]
    public void MapStructuralProjectionKeepsABoundedEnumerationPrefixWithOrdinalKeys()
    {
        var source = new Dictionary<string, LogValue>(StringComparer.Ordinal);
        for (int index = 1023; index >= 0; index--)
        {
            source.Add("k" + index.ToString("D4", CultureInfo.InvariantCulture), LogValue.Integer(index));
        }
        LogValue result = LogValue.Map(source);
        Assert.AreEqual(1023, result.MapProperties.Count);
        Assert.IsTrue(result.IsTruncated);
        Assert.IsFalse(result.MapProperties.ContainsKey("k0000"));
        Assert.IsTrue(result.MapProperties.ContainsKey("k1023"));
        Assert.IsTrue(result.MapProperties.ContainsKey("k0001"));
        Assert.IsFalse(result.MapProperties.ContainsKey("K0001"));
        Assert.AreEqual(1024, source.Count);
        source.Clear();
        Assert.AreEqual(1L, result.MapProperties["k0001"].Scalar.Integer);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("boolean")]
    [DataRow("string")]
    [DataRow("number")]
    [DataRow("integer")]
    [DataRow("integer-string")]
    [DataRow("decimal-string")]
    [DataRow("uuid")]
    [DataRow("timestamp")]
    public void StructuralProjectionNeverChangesRetainedScalarKinds(string kind)
    {
        LogValue scalar = kind switch
        {
            "null" => LogValue.Null(),
            "boolean" => LogValue.Boolean(true),
            "string" => LogValue.String("\U0001f600\n"),
            "number" => LogValue.Number(9007199254740992.0),
            "integer" => LogValue.Integer(9007199254740991),
            "integer-string" => LogValue.IntegerString("9007199254740992000000000000000000000000"),
            "decimal-string" => LogValue.DecimalString("12345678901234567890.00000000000000000001"),
            "uuid" => LogValue.Uuid(Guid.Empty),
            _ => LogValue.Timestamp(DateTimeOffset.MinValue)
        };
        LogValue result = LogValue.Array(Enumerable.Repeat(LogValue.Null(), 1022).Append(scalar).Append(LogValue.Null()));
        Assert.IsTrue(result.IsTruncated);
        Assert.AreEqual(1024, result.NodeCount);
        Assert.AreSame(scalar, result.ArrayItems[^1]);
        Assert.AreEqual(scalar.Kind, result.ArrayItems[^1].Kind);
        Assert.IsTrue(JsonNode.DeepEquals(LogValueFixtureAdapter.Project(scalar), LogValueFixtureAdapter.Project(result.ArrayItems[^1])));
    }

    [TestMethod]
    [DataRow("array")]
    [DataRow("map")]
    public void PartialDescendantProjectionOwnsShapesAndDoesNotMutateSharedInputs(string kind)
    {
        var leafSource = Enumerable.Repeat(LogValue.Integer(42), 31).ToList();
        LogValue leaf = LogValue.Array(leafSource);
        LogValue[] values = Enumerable.Repeat(leaf, 33).ToArray();
        var mapSource = values.Select((child, index) => new KeyValuePair<string, LogValue>(index.ToString(CultureInfo.InvariantCulture), child)).ToDictionary();
        LogValue result = kind == "array" ? LogValue.Array(values) : LogValue.Map(mapSource);
        string before = LogValueFixtureAdapter.Project(result)!.ToJsonString();
        LogValue partial = kind == "array" ? result.ArrayItems.Last() : result.MapProperties.Values.Last();
        Assert.IsTrue(result.IsTruncated && partial.IsTruncated);
        Assert.AreEqual(30, partial.ArrayItems.Count);
        Assert.IsFalse(leaf.IsTruncated);
        Assert.AreEqual(31, leaf.ArrayItems.Count);
        Assert.AreNotSame(leaf, partial);
        leafSource.Clear();
        System.Array.Fill(values, LogValue.Null());
        mapSource.Clear();
        Assert.AreEqual(before, LogValueFixtureAdapter.Project(result)!.ToJsonString());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<LogValue>)partial.ArrayItems).Clear());
    }

    [TestMethod]
    [DataRow("array")]
    [DataRow("map")]
    public void PriorTruncationSurvivesReuseWithoutAnyAdditionalOmission(string kind)
    {
        LogValue value = LogValue.Null();
        for (int index = 0; index < 8; index++)
        {
            value = LogValue.Array([value]);
        }
        while (value.ArrayItems.Count != 0)
        {
            value = value.ArrayItems[0];
        }
        Assert.IsTrue(value.IsTruncated);
        Assert.AreEqual(1, value.NodeCount);
        LogValue parent = kind == "array" ? LogValue.Array([value]) : LogValue.Map(new Dictionary<string, LogValue> { ["kept"] = value });
        Assert.AreEqual(2, parent.NodeCount);
        Assert.AreEqual(2, parent.Depth);
        Assert.IsTrue(parent.IsTruncated);
        Assert.AreSame(value, kind == "array" ? parent.ArrayItems[0] : parent.MapProperties["kept"]);
        Assert.IsFalse(LogValue.Array([]).IsTruncated);
        Assert.IsFalse(LogValue.Map(new Dictionary<string, LogValue>()).IsTruncated);
    }

    [TestMethod]
    [DataRow("array")]
    [DataRow("map")]
    public void ParallelRootsUseIndependentBudgetsAndImmutableSharedChildren(string kind)
    {
        LogValue child = LogValue.Null();
        var results = new LogValue[16];
        Parallel.For(0, results.Length, index =>
        {
            LogValue[] values = Enumerable.Repeat(child, index % 2 == 0 ? 1 : 1024).ToArray();
            results[index] = kind == "array" ? LogValue.Array(values) : LogValue.Map(values.Select((value, key) => new KeyValuePair<string, LogValue>(key.ToString(CultureInfo.InvariantCulture), value)).ToDictionary());
        });
        for (int index = 0; index < results.Length; index++)
        {
            Assert.AreEqual(index % 2 == 0 ? 2 : 1024, results[index].NodeCount);
            Assert.AreEqual(index % 2 != 0, results[index].IsTruncated);
        }
        Assert.AreEqual(1, child.NodeCount);
        Assert.IsFalse(child.IsTruncated);
    }
}
