using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LogValueNormalizationTests
{
    public static IEnumerable<object[]> BoundaryRows => LogValueTests.Read()["cases"]!.AsArray()
        .Where(row => !LogValueFixtureAdapter.Kinds.Contains(row!["input"]!["kind"]!.GetValue<string>(), StringComparer.Ordinal))
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    public static IEnumerable<object[]> NormalizationRows => LogValueTests.Read("value-normalization-cases.json")["cases"]!.AsArray()
        .SelectMany(row => new[] { "en-US", "fr-FR", "ar-SA" }.Select(culture => new object[] { row!["id"]!.GetValue<string>(), culture }));

    [TestMethod]
    [DynamicData(nameof(BoundaryRows))]
    public void RemainingOriginalScalarAndUnsupportedRowsDistinguishCoreFromTransport(string id)
    {
        JsonObject row = LogValueTests.Read()["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LogValueFixtureAdapter.Execute(row);
        LogValueFixtureAdapter.Validate(row, actual);
        bool valid = row["expectedValid"]!.GetValue<bool>();
        Assert.AreEqual(valid ? 1 : 0, actual["factoryCalls"]!.GetValue<int>());
        if (!valid)
        {
            Assert.AreEqual("InputAdapterTypeMismatch", actual["error"]!.GetValue<string>());
            Assert.IsNull(actual["parameter"]);
        }
    }

    [TestMethod]
    [DynamicData(nameof(NormalizationRows))]
    public void ActualScalarNormalizationPreservesInstantsAndPrecisionAcrossCultures(string id, string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            JsonObject row = LogValueTests.Read("value-normalization-cases.json")["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
            JsonObject actual = LogValueFixtureAdapter.Execute(row);
            LogValueFixtureAdapter.Validate(row, actual);
            Assert.AreEqual(1, actual["factoryCalls"]!.GetValue<int>());
            Assert.AreEqual(row["expectedJson"]!.GetValue<string>(), actual["json"]!.GetValue<string>());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [TestMethod]
    [DataRow("uuid")]
    [DataRow("timestamp")]
    public void IdenticalWireStringsCannotEraseExplicitNormalizedKinds(string kind)
    {
        JsonObject row = LogValueTests.Read("value-normalization-cases.json")["cases"]!.AsArray().First(value => value!["input"]!["kind"]!.GetValue<string>() == kind)!.AsObject();
        JsonObject actual = LogValueFixtureAdapter.Execute(row);
        LogValueFixtureAdapter.Validate(row, actual);
        actual["kind"] = "String";
        Assert.ThrowsExactly<InvalidDataException>(() => LogValueFixtureAdapter.Validate(row, actual));
        Assert.IsTrue(JsonNode.DeepEquals(row["expectedJson"], actual["json"]));
    }
}
