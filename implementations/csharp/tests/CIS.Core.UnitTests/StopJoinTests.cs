using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class StopJoinTests
{
    public static IEnumerable<object[]> Modes => StopJoinFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task CallerCancellationAndDisposalJoinTheOriginalBoundedOperation(string mode) =>
        StopJoinExpectations.Validate(mode, await StopJoinFixture.ExecuteAsync(mode));
}
