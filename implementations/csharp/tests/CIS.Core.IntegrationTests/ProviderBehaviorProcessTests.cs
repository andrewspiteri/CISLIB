using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderBehaviorProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderBehaviorExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderInterestAndRejectionUseActualCore(string mode)
    {
        ProviderReportCapture capture = await ProviderReportProcess.RunAsync("provider-behavior", mode, TestContext);
        JsonObject report = capture.Report;
        bool physical = ProviderBehaviorExpectations.IsPhysicalMode(mode);
        ProviderBehaviorExpectations.Validate(mode, report, capture.ProcessId, physical);
        if (physical)
        {
            JsonObject changed = report.DeepClone().AsObject();
            changed["pid"] = capture.ProcessId + 1;
            Assert.AreEqual("actual child PID", Assert.ThrowsExactly<InvalidDataException>(() => ProviderBehaviorExpectations.Validate(mode, changed, capture.ProcessId, physical)).Message);
            changed = report.DeepClone().AsObject(); changed["leaseReleased"] = false;
            Assert.AreEqual("physical lifetime", Assert.ThrowsExactly<InvalidDataException>(() => ProviderBehaviorExpectations.Validate(mode, changed, capture.ProcessId, physical)).Message);
        }
    }
}
