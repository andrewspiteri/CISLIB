using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopJoinProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => LifecycleProcessChecks.Modes("stop-join", CIS.Tests.DiscoveryFixture.StopJoinExpectations.Modes);
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualSharedStopRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.RunFunctional("stop-join", mode, TestContext);
}
