using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class FlushCoordinationTests
{
    [TestMethod]
    [TestCategory("ExtendedStress")]
    [DataRow("write-cancel")]
    [DataRow("write-deadline")]
    [DataRow("flush-cancel")]
    [DataRow("flush-deadline")]
    [DataRow("cohort")]
    [DataRow("dual")]
    [DataRow("pre-cancel")]
    [DataRow("fault")]
    [DataRow("complete-race")]
    public async Task ActualBlockedWritersRetainOnlyTargetsAndLiveCallerAttachments(string mode)
    { using var root = new TemporaryDirectory(); FlushCoordinationExpectations.Validate(mode, await FlushCoordinationFixture.ExecuteAsync(mode, root.Path)); }
}
