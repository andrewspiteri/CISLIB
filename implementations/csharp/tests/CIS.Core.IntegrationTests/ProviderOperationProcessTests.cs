using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderOperationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderOperationExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ScopeOperationIdentityUsesActualCoreCapture(string mode)
    {
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync("provider-operation", mode, TestContext);
        ProviderOperationExpectations.Validate(mode, captured.Report, captured.ProcessId, physical: true);
    }
}
