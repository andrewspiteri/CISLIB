using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderHostFilterProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderHostFilterExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFactoryAllowsIndependentCoreThresholds(string mode)
    {
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync("provider-host-filter", mode, TestContext);
        ProviderHostFilterExpectations.Validate(mode, captured.Report, captured.ConsoleText, captured.ProcessId);
    }
}
