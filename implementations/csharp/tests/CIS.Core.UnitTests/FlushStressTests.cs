using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FlushStressTests
{
    [TestMethod]
    [DataRow("repeat-write")]
    [DataRow("repeat-flush")]
    [DataRow("repeat-progress")]
    [DataRow("saturation")]
    [DataRow("cohort")]
    [DataRow("failed-flush")]
    [DataRow("concurrent")]
    [DataRow("deadline-race")]
    public async Task PublicFlushStressUsesActualQueuesWritersTimersAndMeasuredStorage(string mode)
    {
        using var root = new TemporaryDirectory();
        FlushStressExpectations.Validate(mode, await FlushStressFixture.ExecuteAsync(mode, root.Path));
    }
}
