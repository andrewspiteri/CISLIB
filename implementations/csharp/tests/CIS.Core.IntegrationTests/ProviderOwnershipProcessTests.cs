using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderOwnershipProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderOwnershipExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderDisposalLeavesApplicationRuntimeOwnership(string mode)
    {
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync("provider-ownership", mode, TestContext);
        ProviderOwnershipExpectations.Validate(mode, captured.Report, captured.ProcessId);
    }
}
