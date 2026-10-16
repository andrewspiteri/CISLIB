using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushStressProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("repeat-write")]
    [DataRow("repeat-flush")]
    [DataRow("repeat-progress")]
    [DataRow("saturation")]
    [DataRow("cohort")]
    [DataRow("failed-flush")]
    [DataRow("concurrent")]
    [DataRow("deadline-race")]
    public async Task ActualPublicFlushStressRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.Run("flush-stress", mode, TestContext);
}
