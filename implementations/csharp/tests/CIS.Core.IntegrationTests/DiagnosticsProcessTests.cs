using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class DiagnosticsProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("diagnostics-cases.json", 23);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task PublicDiagnosticsRunInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("diagnostics-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        string sourceCatalog = (string)row["profile"]! == "delivery" ? "delivery-observation-cases.json" : "output-state-cases.json";
        Assert.AreEqual(sourceCatalog, (string?)row["sourceCatalog"]);
        Assert.AreEqual("DIAGNOSTICS-" + (string)row["sourceCaseId"]!, id);
        string sourcePath = FixtureProcessChecks.Find(sourceCatalog), sourceHash = Hash(sourcePath);
        JsonObject actual = await QueueProcessChecks.Capture("diagnostics", "diagnostics-cases.json", id, TestContext, 35);
        DiagnosticsExpectations.Validate(row, actual);
        Assert.AreEqual(sourceHash, (string?)actual["observed"]!["sourceInputsSha256"]);
        Assert.AreEqual(sourceHash, Hash(sourcePath));
        JsonObject sourceRow = LogValueProcessTests.Read(sourceCatalog)["cases"]!.AsArray().Single(item => (string?)item!["id"] == (string?)row["sourceCaseId"])!.AsObject();
        if ((string?)row["profile"] == "delivery") { DeliveryObservationExpectations.Validate(sourceRow, actual["source"]!.AsObject()); }
        else { OutputStateExpectations.Validate(sourceRow, actual["source"]!.AsObject()); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
