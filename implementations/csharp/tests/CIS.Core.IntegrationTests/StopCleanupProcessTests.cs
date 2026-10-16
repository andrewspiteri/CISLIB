using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopCleanupProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => LifecycleProcessChecks.Modes("stop-cleanup", CIS.Tests.DiscoveryFixture.StopCleanupExpectations.Modes);
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualSharedStopRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.Run("stop-cleanup", mode, TestContext);
}
