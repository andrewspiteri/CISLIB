using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushStressProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    // All eight distinct stress/ownership cases run directly in FlushStressTests.
    // This concurrent child control additionally proves clean process exit,
    // isolated-root cleanup and exact-runtime execution.
    [DataRow("concurrent")]
    public async Task ActualPublicFlushStressRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.RunFunctional("flush-stress", mode, TestContext);
}
