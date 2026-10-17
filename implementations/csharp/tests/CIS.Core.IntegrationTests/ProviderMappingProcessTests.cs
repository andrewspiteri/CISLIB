using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderMappingProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderMappingExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task MicrosoftMappingsMatchTheActualPhysicalCore(string mode)
    {
        ProviderReportCapture capture = await ProviderReportProcess.RunAsync("provider-mapping", mode, TestContext);
        JsonObject report = capture.Report;
        ProviderMappingExpectations.Validate(mode, report, capture.ProcessId, physical: true);
        JsonObject changedPid = report.DeepClone().AsObject(); changedPid["pid"] = capture.ProcessId + 1;
        Assert.AreEqual("actual child PID", Assert.ThrowsExactly<InvalidDataException>(() => ProviderMappingExpectations.Validate(mode, changedPid, capture.ProcessId, physical: true)).Message);
        JsonObject changedFacts = report.DeepClone().AsObject();
        string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(changedFacts["bytes"]!.GetValue<string>()))[..^1].Split('\n');
        JsonObject row = JsonNode.Parse(lines[0])!.AsObject(); row["resource"]!["process_id"] = 0;
        lines[0] = row.ToJsonString(); changedFacts["bytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
        Assert.AreEqual("automatic core facts", Assert.ThrowsExactly<InvalidDataException>(() => ProviderMappingExpectations.Validate(mode, changedFacts, capture.ProcessId, physical: true)).Message);
    }
}
