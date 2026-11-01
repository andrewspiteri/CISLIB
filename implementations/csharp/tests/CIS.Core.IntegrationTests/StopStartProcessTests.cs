using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopStartProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => LifecycleProcessChecks.Modes("stop-start", CIS.Tests.DiscoveryFixture.StopStartExpectations.Modes);
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualSharedStopRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.RunFunctional("stop-start", mode, TestContext);
}
