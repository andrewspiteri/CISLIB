using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class RuntimeCreateProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => LifecycleProcessChecks.Modes("runtime-create", CIS.Tests.DiscoveryFixture.RuntimeCreateExpectations.Modes);
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task PublicCreateRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.Run("runtime-create", mode, TestContext);
}
