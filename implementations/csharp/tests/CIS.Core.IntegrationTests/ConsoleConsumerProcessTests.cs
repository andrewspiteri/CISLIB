using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConsoleConsumerProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => ConsoleProcessChecks.Cases("consumer", 38);
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SameCompiledConsumerUsesEnvironmentAndDrainsBothStreams(string id) => await Run(id);
    [TestMethod]
    [DataRow("settings", "Environment-selected settings differ.")]
    [DataRow("outcome", "Consumer outcomes/tickets differ.")]
    [DataRow("factory", "Consumer factory calls differ.")]
    [DataRow("clock", "Consumer clock calls differ.")]
    [DataRow("final", "Consumer final accounting differs.")]
    [DataRow("checkpoint", "Independent file progress differs.")]
    [DataRow("dispose", "Borrowed console disposal differs.")]
    [DataRow("application", "Borrowed application bytes differ.")]
    [DataRow("route", "Unselected consumer stream has records.")]
    [DataRow("identity", "Consumer JSONL facts differ.")]
    [DataRow("resource", "Consumer JSONL facts differ.")]
    [DataRow("bom", "Consumer entry framing/cap differs.")]
    [DataRow("records", "Consumer record count differs.")]
    public async Task ConsumerComparerRejectsSpecificCorruption(string fault, string reason)
    {
        (JsonObject catalog, JsonObject row, JsonObject actual) = await Run("CONSUMER-BLOCK-JSONL-STDERR");
        ConsoleConsumerExpectations.Damage(actual, fault);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true));
        Assert.AreEqual(reason, error.Message);
    }
    [TestMethod]
    public async Task IndependentConsumersHaveUniqueInstanceIdentities()
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (object[] row in Rows)
        {
            (_, _, JsonObject actual) = await Run((string)row[0]);
            Assert.IsTrue(identities.Add((string)actual["resource"]!["instance_id"]!), "A fresh consumer reused its instance identity.");
        }
        Assert.AreEqual(38, identities.Count);
    }

    private async Task<(JsonObject Catalog, JsonObject Row, JsonObject Actual)> Run(string id)
    {
        JsonObject catalog = LogValueProcessTests.Read("console-consumer-cases.json"), row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await ConsoleProcessChecks.Capture("consumer", row, TestContext, ConsoleConsumerExpectations.EnvironmentFor(row));
        ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true);
        JsonNode observed = actual["observed"]!;
        Assert.AreEqual((string?)observed["operatingSystem"], (string?)actual["resource"]!["os_family"]);
        Assert.AreEqual(observed["processId"]!.GetValue<int>(), actual["resource"]!["process_id"]!.GetValue<int>());
        return (catalog, row, actual);
    }
}
