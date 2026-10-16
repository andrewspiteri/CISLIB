using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderScopeProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderScopeExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderScopesUseActualCoreCapture(string mode)
    {
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync("provider-scope", mode, TestContext);
        ProviderScopeExpectations.Validate(mode, captured.Report, captured.ProcessId, physical: true);
    }
}
