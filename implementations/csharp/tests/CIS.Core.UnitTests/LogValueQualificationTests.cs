using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LogValueQualificationTests
{
    public static IEnumerable<object[]> Rows => LogValueTests.Read("value-qualification-cases.json")["cases"]!.AsArray()
        .SelectMany(row => new[] { "en-US", "fr-FR", "ar-SA" }.Select(culture => new object[] { row!["id"]!.GetValue<string>(), culture }));

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void EveryPortableQualificationRowExecutesAgainstCoreAcrossCultures(string id, string culture)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo beforeUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            JsonObject row = LogValueTests.Read("value-qualification-cases.json")["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
            JsonObject actual = LogValueQualificationFixtureAdapter.Execute(row);
            LogValueQualificationFixtureAdapter.Validate(row, actual);
            if (!actual["valid"]!.GetValue<bool>())
            {
                Assert.AreEqual(1, actual["factoryCalls"]!.GetValue<int>());
                Assert.IsNotNull(actual["nativeException"]);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
            CultureInfo.CurrentUICulture = beforeUi;
        }
    }

    [TestMethod]
    [DataRow("kind", "QUALIFY-INTEGER-MAX")]
    [DataRow("error", "QUALIFY-INTEGER-ABOVE")]
    [DataRow("parameter", "QUALIFY-DECIMAL-TEXT-NO-ECHO")]
    [DataRow("numberBits", "QUALIFY-NUMBER-NEGATIVE-ZERO")]
    [DataRow("ownedGraphUnchanged", "QUALIFY-MUTATION-CLEAR-NESTED")]
    [DataRow("json", "QUALIFY-DECIMAL-TEXT-PRECISION")]
    public void QualificationComparatorRejectsChangedMeaningAndErrorObservations(string field, string id)
    {
        JsonObject row = LogValueTests.Read("value-qualification-cases.json")["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LogValueQualificationFixtureAdapter.Execute(row);
        switch (field)
        {
            case "ownedGraphUnchanged":
                actual[field] = false;
                break;
            case "json":
                actual[field] = 1;
                break;
            default:
                actual[field] = "changed";
                break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => LogValueQualificationFixtureAdapter.Validate(row, actual));
    }
}
