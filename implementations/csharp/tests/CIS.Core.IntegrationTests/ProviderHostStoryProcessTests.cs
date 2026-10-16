using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderHostStoryProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => ProviderHostStoryExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderHostStoryCompletesCoreAcceptance(string mode)
    {
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync("provider-host-story", mode, TestContext);
        ProviderHostStoryExpectations.Validate(mode, captured.Report, captured.ProcessId);
    }
}
