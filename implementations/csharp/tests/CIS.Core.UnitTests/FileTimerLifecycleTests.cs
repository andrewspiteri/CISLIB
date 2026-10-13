using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileTimerLifecycleTests
{
    public static IEnumerable<object[]> Modes => FileTimerLifecycleExpectations.Modes.Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFileTimerCancellationAndFaultsJoinOwnedCleanup(string mode)
    {
        using var root = new TemporaryDirectory();
        FileTimerLifecycleExpectations.Validate(mode, await FileTimerLifecycleFixture.ExecuteAsync(mode, root.Path));
    }
}
