using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushResultProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("empty")]
    [DataRow("filtered")]
    [DataRow("closed")]
    [DataRow("physical")]
    [DataRow("write-timeout")]
    [DataRow("flush-timeout")]
    [DataRow("cancel")]
    [DataRow("pre-cancel")]
    [DataRow("loss")]
    [DataRow("write-failure")]
    [DataRow("flush-failure")]
    [DataRow("later-failure")]
    [DataRow("exhaustion")]
    [DataRow("clock")]
    [DataRow("dual")]
    [DataRow("long-timeout")]
    [DataRow("system-timeout")]
    public async Task ActualPublicFlushRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.RunFunctionalFlush("flush-results", mode, TestContext);
}
