using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushCoordinationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("write-cancel")]
    [DataRow("write-deadline")]
    [DataRow("flush-cancel")]
    [DataRow("flush-deadline")]
    [DataRow("cohort")]
    [DataRow("dual")]
    [DataRow("pre-cancel")]
    [DataRow("fault")]
    [DataRow("complete-race")]
    public async Task ActualCallerDetachmentRunsInAnExactRuntimeChild(string mode) => await LifecycleProcessChecks.RunFunctional("flush-coordination", mode, TestContext);
}
