using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggerOutcomeTests
{
    private static readonly Lazy<(string Catalog, string[] Ids, Dictionary<string, string> Rows)> Inputs = new(ReadInputs);

    public static IEnumerable<object[]> Rows => Inputs.Value.Ids.Select(id => new object[] { id });
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
        ValidateCatalog(Catalog());
    }

    private static void ValidateCatalog(JsonObject catalog)
    {
        JsonArray modes = catalog["modes"]!.AsArray(), rows = catalog["cases"]!.AsArray();
        Assert.AreEqual(125, rows.Count);
        Assert.AreEqual(125, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        string[] expectedModes = ["disabled", "filtered", "filtered-faulted", "accepted", "full", "faulted", "admission-fault"];
        CollectionAssert.AreEqual(expectedModes, modes.Select(mode => mode!.GetValue<string>()).ToArray(), "Outcome modes differ.");
        HashSet<string> expectedIds = (from console in expectedModes
                                       from file in expectedModes
                                       from method in new[] { "direct", "lazy" }
                                       select $"OUTCOME-TABLE-{console.ToUpperInvariant()}-{file.ToUpperInvariant()}-{method.ToUpperInvariant()}").ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(expectedIds.SetEquals(rows.Select(row => row!["id"]!.GetValue<string>()).Where(id => id.StartsWith("OUTCOME-TABLE-", StringComparison.Ordinal))), "Ordered outcome pair IDs differ.");
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
    [TestMethod]
    [DataRow("missing-mode")]
    [DataRow("renamed-mode")]
    [DataRow("missing-pair")]
    public void CatalogCannotRedefineTheExpectedMatrix(string fault)
    {
        JsonObject catalog = Catalog();
        ValidateCatalog(catalog);
        switch (fault)
        {
            case "missing-mode": catalog["modes"]!.AsArray().RemoveAt(6); break;
            case "renamed-mode": catalog["modes"]![6] = "invented"; break;
            case "missing-pair": catalog["cases"]![0]!["id"] = "OTHER-CASE"; break;
        }
        Assert.ThrowsExactly<AssertFailedException>(() => ValidateCatalog(catalog));
    }
    private static JsonObject Row(string id) => JsonNode.Parse(Inputs.Value.Rows[id])!.AsObject();
    private static JsonObject Catalog() => JsonNode.Parse(Inputs.Value.Catalog)!.AsObject();

    private static (string Catalog, string[] Ids, Dictionary<string, string> Rows) ReadInputs()
    {
        string text = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "logger-outcome-cases.json"));
        JsonObject[] rows = JsonNode.Parse(text)!["cases"]!.AsArray().Select(row => row!.AsObject()).ToArray();
        // Captures still run per case; only static input text is shared.
        return (text, rows.Select(row => row["id"]!.GetValue<string>()).ToArray(),
            rows.ToDictionary(row => row["id"]!.GetValue<string>(), row => row.ToJsonString(), StringComparer.Ordinal));
    }
}
