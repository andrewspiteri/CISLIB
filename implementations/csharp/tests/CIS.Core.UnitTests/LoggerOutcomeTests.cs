using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggerOutcomeTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void CompleteOutcomeTableMatchesRealLoggerInterestAndAdmission(string id)
    {
        JsonObject row = Row(id);
        LoggerOutcomeExpectations.Validate(row, LoggerOutcomeFixture.Execute(row));
    }

    [TestMethod]
    public void CatalogIncludesEachOrderedOutputPairForDirectAndLazyCalls()
    {
        JsonObject catalog = Catalog();
        JsonArray modes = catalog["modes"]!.AsArray(), rows = catalog["cases"]!.AsArray();
        Assert.AreEqual(125, rows.Count);
        Assert.AreEqual(125, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        foreach (string method in new[] { "direct", "lazy" })
        {
            for (int console = 0; console < modes.Count; console++)
            {
                for (int file = 0; file < modes.Count; file++)
                {
                    string id = $"OUTCOME-TABLE-{modes[console]!.GetValue<string>().ToUpperInvariant()}-{modes[file]!.GetValue<string>().ToUpperInvariant()}-{method.ToUpperInvariant()}";
                    JsonObject row = rows.Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
                    Assert.AreEqual(catalog["aggregateTable"]![console]![file]!.GetValue<string>(), row["expected"]!["results"]![0]!["outcome"]!.GetValue<string>());
                }
            }
        }
    }

    [TestMethod]
    [DataRow("outcome")]
    [DataRow("factory")]
    [DataRow("ticket")]
    [DataRow("filtered")]
    [DataRow("sticky")]
    [DataRow("capacity")]
    [DataRow("exception")]
    [DataRow("utf8")]
    [DataRow("missing")]
    public void ComparatorRejectsAlteredOutcomeAdmissionAndWireObservations(string fault)
    {
        string id = fault switch
        {
            "sticky" => "OUTCOME-STICKY-ACCEPTED-LAZY",
            "capacity" => "OUTCOME-CAPACITY-LAZY",
            "exception" => "OUTCOME-THROW-ACCEPTED-ACCEPTED",
            _ => "OUTCOME-TABLE-ACCEPTED-FILTERED-FAULTED-LAZY"
        };
        JsonObject row = Row(id), actual = LoggerOutcomeFixture.Execute(row);
        LoggerOutcomeExpectations.Validate(row, actual);
        switch (fault)
        {
            case "outcome": actual["results"]![0]!["outcome"] = "PartiallyAccepted"; break;
            case "factory": actual["factoryInvocations"]![0] = 2; break;
            case "ticket": actual["results"]![0]!["outputs"]![1]!["sequence"] = 101; break;
            case "filtered": actual["unavailable"]![1] = 1; break;
            case "sticky": actual["tryCalls"]![0] = 2; break;
            case "capacity": actual["enabledAfterFirst"] = false; break;
            case "exception": actual["exceptionSame"]![0] = false; break;
            case "missing": row["expected"]!.AsObject().Remove("factoryInvocations"); break;
            case "utf8":
                byte[] bytes = Convert.FromBase64String(actual["wires"]![0]![0]!.GetValue<string>());
                bytes[0] = 0xFF; actual["wires"]![0]![0] = Convert.ToBase64String(bytes); break;
        }
        if (fault == "utf8") { Assert.ThrowsExactly<System.Text.DecoderFallbackException>(() => LoggerOutcomeExpectations.Validate(row, actual)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => LoggerOutcomeExpectations.Validate(row, actual)); }
    }
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "logger-outcome-cases.json")))!.AsObject();
}
