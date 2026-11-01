using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopDrainProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => LifecycleProcessChecks.Modes("stop-drain", CIS.Tests.DiscoveryFixture.StopDrainExpectations.Modes);
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualSharedStopRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.RunFunctional("stop-drain", mode, TestContext);
}
