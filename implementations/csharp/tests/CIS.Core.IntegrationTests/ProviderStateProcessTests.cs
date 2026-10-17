using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderStateProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderStateExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task StructuredStateUsesActualPhysicalCoreCapture(string mode)
    {
        ProviderReportCapture capture = await ProviderReportProcess.RunAsync("provider-state", mode, TestContext);
        JsonObject report = capture.Report;
        ProviderStateExpectations.Validate(mode, report, capture.ProcessId, physical: true);
        JsonObject changedPid = report.DeepClone().AsObject(); changedPid["pid"] = capture.ProcessId + 1;
        Assert.AreEqual("actual child PID", Assert.ThrowsExactly<InvalidDataException>(() => ProviderStateExpectations.Validate(mode, changedPid, capture.ProcessId, physical: true)).Message);
        if (Convert.FromBase64String(report["bytes"]!.GetValue<string>()).Length > 0)
        {
            JsonObject changedFacts = report.DeepClone().AsObject();
            string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(changedFacts["bytes"]!.GetValue<string>()))[..^1].Split('\n');
            JsonObject row = JsonNode.Parse(lines[0])!.AsObject(); row["resource"]!["process_id"] = 0;
            lines[0] = row.ToJsonString(); changedFacts["bytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
            Assert.AreEqual("automatic core facts", Assert.ThrowsExactly<InvalidDataException>(() => ProviderStateExpectations.Validate(mode, changedFacts, capture.ProcessId, physical: true)).Message);
        }
    }
}
