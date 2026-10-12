using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggerDecisionTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void SharedDecisionCasesObserveArgumentsThenStateThenCaptureThenAdmission(string id)
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        LoggerDecisionExpectations.Validate(row, LoggerDecisionFixture.Execute(row));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void IsEnabledValidatesUndefinedLevelsBeforeClosedOrFaultedState(bool closed, bool faulted)
    {
        var output = new LoggerDecisionSources.Output("console", []) { Faulted = faulted };
        var runtime = new CoreLogRuntime(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions(), output, null);
        if (closed) { runtime.CloseAdmission(); }
        CoreLogger logger = runtime.CreateLogger("Component");
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.IsEnabled((CoreLogLevel)999));
        Assert.AreEqual("level", error.ParamName);
        Assert.AreEqual(0, output.FaultReads);
        Assert.AreEqual(0, output.Attempts);
    }

    [TestMethod]
    [DataRow("trace")]
    [DataRow("attempt")]
    [DataRow("ticket")]
    [DataRow("utf8")]
    public void ComparatorRejectsAlteredExecutionObservations(string fault)
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "DECISION-CAPTURE-FANOUT-DIRECT")!.AsObject();
        JsonObject actual = LoggerDecisionFixture.Execute(row);
        switch (fault)
        {
            case "trace": actual["trace"]!.AsArray().Insert(0, "factory"); break;
            case "attempt": actual["attempts"]![0] = 0; break;
            case "ticket": actual["result"]!["outputs"]![0]!["sequence"] = 2; break;
            case "utf8":
                byte[] bytes = Convert.FromBase64String(actual["wires"]![0]![0]!.GetValue<string>());
                bytes[0] = 0xFF;
                actual["wires"]![0]![0] = Convert.ToBase64String(bytes);
                break;
        }
        if (fault == "utf8") { Assert.ThrowsExactly<System.Text.DecoderFallbackException>(() => LoggerDecisionExpectations.Validate(row, actual)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => LoggerDecisionExpectations.Validate(row, actual)); }
    }

    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "logger-decision-cases.json")))!.AsObject();
}
