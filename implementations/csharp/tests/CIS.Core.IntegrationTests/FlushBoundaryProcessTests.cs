using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushBoundaryProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("empty")]
    [DataRow("both")]
    [DataRow("saturated")]
    [DataRow("faulted")]
    [DataRow("closed")]
    [DataRow("concurrent")]
    [DataRow("physical")]
    public async Task RegisteredBoundaryFixtureRunsActualCoreInASeparateExactRuntime(string mode) => await LifecycleProcessChecks.Run("flush-boundary", mode, TestContext);
}
